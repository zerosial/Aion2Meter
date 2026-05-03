using System;
using System.Collections.Generic;
using System.Text;

namespace A2Meter.Dps.Protocol;

/// Low-level helpers ported from A2Viewer.Packet.ProtocolUtils.
/// Keeps the original byte-exact heuristics so server-port detection,
/// magic-payload sniffing and TLS rejection continue to behave identically.
internal static class ProtocolUtils
{
    /// 3-byte sync marker that bounds every framed game packet (06 00 36).
    private static readonly byte[] SyncMarker = new byte[] { 0x06, 0x00, 0x36 };

    public static string HexDump(ReadOnlySpan<byte> data, int offset = 0, int maxBytes = 200)
    {
        int start = Math.Max(0, offset);
        int len   = Math.Min(data.Length - start, maxBytes);
        if (len <= 0) return "";

        var sb = new StringBuilder(len * 3);
        for (int i = 0; i < len; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[start + i].ToString("X2"));
        }
        if (start + len < data.Length) sb.Append("...");
        return sb.ToString();
    }

    public static string HexDump(byte[] data, int offset = 0, int maxBytes = 200)
        => HexDump(data.AsSpan(), offset, maxBytes);

    public static int IndexOf(byte[] buffer, byte[] pattern)
    {
        for (int i = 0; i <= buffer.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (buffer[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    public static int IndexOf(List<byte> buffer, byte[] pattern)
    {
        for (int i = 0; i <= buffer.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (buffer[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    /// Reject SSL/TLS records (content type 20-24, version 3.x).
    public static bool LooksLikeTlsRecord(byte[] data)
    {
        if (data.Length < 5) return false;
        byte ct = data[0];
        if (ct >= 20 && ct <= 24 && data[1] == 3) return data[2] <= 4;
        return false;
    }

    public static bool ContainsSyncMarker(byte[] data) => IndexOf(data, SyncMarker) >= 0;

    public static bool HasSyncTail(byte[] data)
    {
        if (data.Length >= 3 && data[^3] == SyncMarker[0] && data[^2] == SyncMarker[1])
            return data[^1] == SyncMarker[2];
        return false;
    }

    /// Heuristic used by the original sniffer to confirm a TCP stream carries
    /// the game's framed varint protocol (and not TLS / random garbage).
    public static bool LooksLikeGameMagicPayload(byte[] data)
    {
        if (data.Length <= 3)        return false;
        if (LooksLikeTlsRecord(data)) return false;
        if (!ContainsSyncMarker(data)) return false;
        if (!HasSyncTail(data))        return false;

        int pos = 0, frames = 0;
        while (pos < data.Length)
        {
            int frameStart = pos;
            uint val = ReadVarintCounting(data, ref pos, data.Length, out int consumed);
            if (val == uint.MaxValue || consumed <= 0) return false;
            int payloadLen = (int)val + consumed - 4;
            if (payloadLen <= 0) return false;
            int frameEnd = frameStart + payloadLen;
            if (frameEnd > data.Length) return false;
            pos = frameEnd;
            frames++;
        }
        return frames > 0 && pos == data.Length;
    }

    public static bool IsGamePacket(byte[] data)
    {
        if (data.Length < 3) return false;
        if (data[0] - 20 <= 3 && data[1] == 3) return data[2] <= 4;
        return false;
    }

    private static uint ReadVarintCounting(byte[] data, ref int pos, int end, out int bytesConsumed)
    {
        uint value = 0;
        int  shift = 0;
        int  start = pos;
        while (pos < end)
        {
            byte b = data[pos++];
            value |= (uint)((b & 0x7F) << shift);
            if ((b & 0x80) == 0)
            {
                bytesConsumed = pos - start;
                return value;
            }
            shift += 7;
            if (shift > 28) { bytesConsumed = 0; return uint.MaxValue; }
        }
        bytesConsumed = 0;
        return uint.MaxValue;
    }

    public static uint ReadVarint(byte[] data, ref int offset, int limit)
    {
        uint value = 0;
        int  shift = 0;
        while (offset < limit)
        {
            byte b = data[offset++];
            value |= (uint)((b & 0x7F) << shift);
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift > 31) break;
        }
        return uint.MaxValue;
    }

    /// Decodes a back-reference compressed UTF-8 string used by the game protocol:
    /// bytes 0x00..0x1F act as "repeat first N bytes of the output buffer";
    /// bytes >= 0x20 are appended verbatim. Output is UTF-8 then filtered to
    /// letters/digits/Hangul (the original strips control/punctuation).
    public static string DecodeGameString(byte[] data, int offset, int maxLen)
    {
        byte[] buf = new byte[maxLen * 4];
        int    n   = 0;
        int    end = offset + maxLen;
        for (int i = offset; i < end; i++)
        {
            byte b = data[i];
            if (b == 0) break;
            if (b < 32)
            {
                int copy = Math.Min(b, n);
                for (int j = 0; j < copy; j++)
                {
                    if (n >= buf.Length) break;
                    buf[n++] = buf[j];
                }
            }
            else if (n < buf.Length)
            {
                buf[n++] = b;
            }
        }

        var raw = Encoding.UTF8.GetString(buf, 0, n);
        var sb  = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || (c >= '가' && c <= '힣'))
                sb.Append(c);
        }
        return sb.ToString();
    }

    public static ulong Fnv1aHash(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= 1099511628211UL;
        }
        return h;
    }

    public static bool IsAllDigits(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (!char.IsDigit(s[i])) return false;
        return true;
    }

    public static int FindPattern(byte[] data, int offset, int length, byte[] pattern)
    {
        int end = offset + length - pattern.Length + 1;
        for (int i = offset; i < end; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
