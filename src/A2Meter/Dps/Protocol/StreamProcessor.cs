using System;
using System.Buffers;
using System.Text;

namespace A2Meter.Dps.Protocol;

/// Frame extractor: takes the ordered TCP byte stream from TcpReassembler and
/// emits message-sized chunks to the dispatch hook. Handles the protocol's
/// varint length prefix, sync-marker resync on corrupt frames, and the LZ4
/// container format used for batched messages.
///
/// Notes vs. the original A2Viewer.Packet.StreamProcessor:
///  - The native PacketEngineInterop.PE_Dispatch call is intentionally omitted.
///    Combat parsing in this port is pure C# (PartyStreamParser + PacketDispatcher),
///    fed via the message dispatch hook. If we later decide to use the bundled
///    PacketEngine.dll, this is the place to plug it back in.
///  - Diagnostics (calls/lz4/sync counters) are preserved to keep parity with
///    the original logs when we cross-check captures.
internal sealed class StreamProcessor
{
    private const int InitialCapacity = 65536;
    private const int MaxMessageLen   = 2_000_000;
    private const int MaxBufferBytes  = 4_000_000;

    private readonly Action<byte[], int, int> _messageDispatchHook;
    private readonly Action<string>?         _logSink;

    private string _flowKey      = "unknown";
    private string _protocolKind = "unknown";

    private byte[] _buffer    = new byte[InitialCapacity];
    private int    _bufferLen;

    private int _processDataCalls;
    private int _dispatchCalls;
    private int _syncRecoverCount;
    private int _lz4DecompCount;
    private int _lz4FailCount;
    private int _invalidLenCount;
    private int _incompleteCount;
    private DateTime _lastDiagLog = DateTime.MinValue;

    public string Diag => $"calls={_processDataCalls} dispatch={_dispatchCalls} " +
                          $"lz4={_lz4DecompCount} lz4fail={_lz4FailCount} " +
                          $"sync={_syncRecoverCount} invalid={_invalidLenCount} " +
                          $"incomplete={_incompleteCount} bufLen={_bufferLen}";

    public StreamProcessor(Action<byte[], int, int> messageDispatchHook, Action<string>? logSink = null)
    {
        _messageDispatchHook = messageDispatchHook;
        _logSink = logSink;
    }

    public void SetFlowContext(string flowKey, string protocolKind)
    {
        _flowKey      = string.IsNullOrWhiteSpace(flowKey)      ? "unknown" : flowKey;
        _protocolKind = string.IsNullOrWhiteSpace(protocolKind) ? "unknown" : protocolKind;
    }

    public void ProcessData(byte[] data)
    {
        _processDataCalls++;
        EmitDiagPeriodic();
        if (data.Length == 0) return;

        long total = (long)_bufferLen + data.Length;
        if (total > MaxBufferBytes)
        {
            Log($"buffer overflow: bufLen={_bufferLen} + dataLen={data.Length} = {total}, resetting");
            _bufferLen = 0;
            return;
        }

        EnsureCapacity((int)total);
        Buffer.BlockCopy(data, 0, _buffer, _bufferLen, data.Length);
        _bufferLen += data.Length;

        int i = 0;
        while (i < _bufferLen)
        {
            // Skip leading null padding between messages.
            while (i < _bufferLen && _buffer[i] == 0) i++;
            if (i >= _bufferLen) break;

            int frameStart = i;
            uint val = ReadVarintCounting(_buffer, ref i, _bufferLen, out int consumed);
            if (val == uint.MaxValue || consumed == 0)
            {
                i = frameStart;
                _incompleteCount++;
                break;
            }

            int msgLen = (int)val + consumed - 4;
            if (msgLen <= 0 || msgLen > MaxMessageLen)
            {
                _invalidLenCount++;
                if (_invalidLenCount <= 5)
                    Log($"invalid msgLen={msgLen} varint=0x{val:X} varintBytes={consumed} " +
                        $"at bufOffset={frameStart} bufLen={_bufferLen} first8={HexSnippet(_buffer, frameStart, 8)}");

                int sync = FindSyncPattern(frameStart + 1);
                if (sync < 0) { i = _bufferLen; break; }
                _syncRecoverCount++;
                i = sync;
                continue;
            }

            if (frameStart + msgLen > _bufferLen)
            {
                // Wait for the rest of the message to arrive.
                i = frameStart;
                break;
            }

            i = frameStart + msgLen;
            if (msgLen > consumed)
                ProcessMessage(_buffer, frameStart, msgLen, consumed);
        }

        if (i > 0) Compact(i);
    }

    private void ProcessMessage(byte[] buf, int start, int len, int varintLen)
    {
        if (len <= varintLen) return;

        int p = varintLen;
        // Optional 1-byte type marker prefix (0xF0..0xFE).
        if (p < len && (buf[start + p] & 0xF0) == 0xF0 && buf[start + p] != 0xFF) p++;

        // 0xFF 0xFF <int32 decompressedSize> <lz4 block> indicates an LZ4 batch frame.
        if (len >= p + 2 && buf[start + p] == 0xFF && buf[start + p + 1] == 0xFF)
        {
            int rd  = start + p + 2;
            int end = start + len;
            if (rd + 4 > end) return;

            int decompSize = BitConverter.ToInt32(buf, rd);
            rd += 4;
            if (decompSize <= 0 || decompSize > MaxMessageLen) return;

            byte[] rented = ArrayPool<byte>.Shared.Rent(decompSize);
            try
            {
                int compLen = end - rd;
                int written = Lz4Decoder.Decompress(buf, rd, compLen, rented, 0, decompSize);
                if (written > 0)
                {
                    _lz4DecompCount++;
                    ProcessDecompressedBlock(rented, 0, written);
                    return;
                }

                _lz4FailCount++;
                if (_lz4FailCount <= 5) Log($"lz4 fail: compLen={compLen} decompSize={decompSize} result={written}");
                return;
            }
            finally { ArrayPool<byte>.Shared.Return(rented); }
        }

        _dispatchCalls++;
        Dispatch(buf, start, len);
    }

    private void ProcessDecompressedBlock(byte[] data, int offset, int length)
    {
        int i = offset;
        int end = offset + length;
        while (i < end)
        {
            while (i < end && data[i] == 0) i++;
            if (i >= end) break;

            int frameStart = i;
            uint val = ReadVarintCounting(data, ref i, end, out int consumed);
            if (val == uint.MaxValue || consumed == 0)
            {
                if (end - frameStart > 0) Dispatch(data, frameStart, end - frameStart);
                break;
            }

            int msgLen = (int)val + consumed - 4;
            if (msgLen <= 0 || frameStart + msgLen > end)
            {
                if (end - frameStart > 0) Dispatch(data, frameStart, end - frameStart);
                break;
            }

            _dispatchCalls++;
            ProcessSubMessage(data, frameStart, msgLen, consumed);
            i = frameStart + msgLen;
        }
    }

    private void ProcessSubMessage(byte[] data, int start, int len, int varintLen)
    {
        int p = start + varintLen;
        if (p < start + len && (data[p] & 0xF0) == 0xF0 && data[p] != 0xFF) p++;

        if (p + 2 <= start + len && data[p] == 0xFF && data[p + 1] == 0xFF)
        {
            p += 2;
            if (p + 4 > start + len) return;

            int decompSize = BitConverter.ToInt32(data, p);
            p += 4;
            if (decompSize <= 0 || decompSize > MaxMessageLen) return;

            byte[] rented = ArrayPool<byte>.Shared.Rent(decompSize);
            try
            {
                int compLen = start + len - p;
                int written = Lz4Decoder.Decompress(data, p, compLen, rented, 0, decompSize);
                if (written > 0)
                {
                    if (written < rented.Length) rented[written] = 0;
                    ProcessDecompressedBlock(rented, 0, written);
                }
                return;
            }
            finally { ArrayPool<byte>.Shared.Return(rented); }
        }

        Dispatch(data, start, len);
    }

    private void Dispatch(byte[] data, int offset, int length)
    {
        int len = Math.Min(length, data.Length - offset);
        if (len <= 0) return;
        _messageDispatchHook(data, offset, len);
    }

    public string FlowKey      => _flowKey;
    public string ProtocolKind => _protocolKind;

    private void Compact(int consumedBytes)
    {
        if (consumedBytes > _bufferLen) consumedBytes = _bufferLen;
        _bufferLen -= consumedBytes;
        if (_bufferLen > 0 && consumedBytes + _bufferLen <= _buffer.Length)
            Buffer.BlockCopy(_buffer, consumedBytes, _buffer, 0, _bufferLen);
        else if (_bufferLen < 0 || consumedBytes + _bufferLen > _buffer.Length)
        {
            Log($"buffer compact bounds error: offset={consumedBytes} bufLen={_bufferLen} arrLen={_buffer.Length}, resetting");
            _bufferLen = 0;
        }
    }

    private void EnsureCapacity(int needed)
    {
        if (_buffer.Length < needed)
        {
            var bigger = new byte[Math.Max(_buffer.Length * 2, needed)];
            Buffer.BlockCopy(_buffer, 0, bigger, 0, _bufferLen);
            _buffer = bigger;
        }
    }

    private int FindSyncPattern(int startOffset)
    {
        // 06 00 36 — same marker as ProtocolUtils.SyncMarker.
        for (int i = startOffset; i + 2 < _bufferLen; i++)
            if (_buffer[i] == 0x06 && _buffer[i + 1] == 0x00 && _buffer[i + 2] == 0x36)
                return i;
        return -1;
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
            if ((b & 0x80) == 0) { bytesConsumed = pos - start; return value; }
            shift += 7;
            if (shift > 28) { bytesConsumed = 0; return uint.MaxValue; }
        }
        bytesConsumed = 0;
        return uint.MaxValue;
    }

    private static string HexSnippet(byte[] data, int offset, int maxBytes)
    {
        int n = Math.Min(maxBytes, data.Length - offset);
        if (n <= 0) return "";
        var sb = new StringBuilder(n * 3);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[offset + i].ToString("X2"));
        }
        return sb.ToString();
    }

    private void Log(string msg) => _logSink?.Invoke("[StreamProc] " + msg);

    private void EmitDiagPeriodic()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDiagLog).TotalSeconds < 30) return;
        _lastDiagLog = now;
        Log("stats: " + Diag);
    }
}
