using System;
using System.Text;

namespace PacketEngine;

/// Shared byte-level utilities for both PE and PP parsers.
/// Ported from A2Viewer.Packet.ProtocolUtils.
internal static class ProtocolUtils
{
    public static uint ReadVarint(byte[] data, ref int offset, int limit)
    {
        uint result = 0;
        int shift = 0;
        while (offset < limit)
        {
            byte b = data[offset++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 31) break;
        }
        return uint.MaxValue;
    }

    public static (int value, int length) ReadVarInt(ReadOnlySpan<byte> buf)
    {
        int val = 0, shift = 0, used = 0;
        do
        {
            if (used >= buf.Length) return (-1, -1);
            byte b = buf[used++];
            val |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return (val, used);
            shift += 7;
        }
        while (shift < 32);
        return (-1, -1);
    }

    public static string DecodeGameString(byte[] data, int offset, int maxLen)
    {
        byte[] buf = new byte[maxLen * 4];
        int pos = 0;
        int end = offset + maxLen;
        for (int i = offset; i < end; i++)
        {
            byte b = data[i];
            if (b == 0) break;
            if (b < 32)
            {
                int count = Math.Min(b, pos);
                for (int j = 0; j < count; j++)
                {
                    if (pos >= buf.Length) break;
                    buf[pos++] = buf[j];
                }
            }
            else if (pos < buf.Length)
            {
                buf[pos++] = b;
            }
        }
        string raw = Encoding.UTF8.GetString(buf, 0, pos);
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || (c >= '가' && c <= '힣'))
                sb.Append(c);
        }
        return sb.ToString();
    }

    public static bool IsAllDigits(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (!char.IsDigit(s[i])) return false;
        return true;
    }

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

    public static int IndexOf(System.Collections.Generic.List<byte> buffer, byte[] pattern)
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

    public static int FindPattern(byte[] data, int offset, int length, byte[] pattern)
    {
        int hi = offset + length - pattern.Length + 1;
        for (int i = offset; i < hi; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
