using System;
using System.Collections.Generic;

namespace A2Meter.Dps.Protocol;

/// Per-flow TCP byte stream reassembler. Sequence-number aware, with bounded
/// out-of-order buffering and a force-flush escape valve so a missing segment
/// never stalls the pipeline indefinitely. Ported verbatim from the original.
internal sealed class TcpReassembler
{
    private const long MaxBufferBytes = 4 * 1024 * 1024; // 4 MB
    private const long TimeoutMs      = 2000L;
    private const int  MaxGapStuck    = 50;

    private readonly Action<byte[]> _callback;
    private readonly SortedDictionary<uint, byte[]> _outOfOrder = new();

    private uint _expectedSeq;
    private bool _initialized;
    private long _totalBuffered;
    private long _lastActivityTicks;

    private int _deliveredCount;
    private int _oooCount;
    private int _forceFlushCount;
    private int _gapStuckCount;

    public string DiagInfo =>
        $"delivered={_deliveredCount} ooo={_oooCount} flush={_forceFlushCount} " +
        $"buffered={_totalBuffered} pending={_outOfOrder.Count} gapStuck={_gapStuckCount}";

    public TcpReassembler(Action<byte[]> callback) => _callback = callback;

    public void Feed(uint seqNum, byte[] data)
    {
        if (data.Length == 0) return;

        if (!_initialized)
        {
            _expectedSeq = seqNum;
            _initialized = true;
        }

        long now = Environment.TickCount64;
        if (_outOfOrder.Count > 0 && _lastActivityTicks > 0 && now - _lastActivityTicks > TimeoutMs)
            ForceFlush();

        int delta = (int)(seqNum - _expectedSeq);
        if (delta == 0)
        {
            _callback(data);
            _expectedSeq      = seqNum + (uint)data.Length;
            _lastActivityTicks = now;
            _deliveredCount++;
            _gapStuckCount = 0;
            DrainOrdered();
        }
        else if (delta < 0)
        {
            // Retransmit overlapping the head of the expected window — deliver only the new tail.
            int overlap = -delta;
            if (overlap < data.Length)
            {
                var tail = new byte[data.Length - overlap];
                Buffer.BlockCopy(data, overlap, tail, 0, tail.Length);
                _callback(tail);
                _expectedSeq      += (uint)tail.Length;
                _lastActivityTicks = now;
                _deliveredCount++;
                _gapStuckCount = 0;
                DrainOrdered();
            }
        }
        else
        {
            // Out-of-order: park until the gap fills or we have to give up.
            _oooCount++;
            _gapStuckCount++;
            if (!_outOfOrder.ContainsKey(seqNum))
            {
                _outOfOrder[seqNum] = data;
                _totalBuffered     += data.Length;
            }
            if (_totalBuffered > MaxBufferBytes || _gapStuckCount > MaxGapStuck)
                ForceFlush();
        }
    }

    private void DrainOrdered()
    {
        while (_outOfOrder.Count > 0)
        {
            uint key = 0;
            using (var en = _outOfOrder.GetEnumerator())
                if (en.MoveNext()) key = en.Current.Key;

            int delta = (int)(key - _expectedSeq);
            if (delta > 0) break;

            _outOfOrder.Remove(key, out var value);
            if (value is null) continue;
            _totalBuffered -= value.Length;

            if (delta == 0)
            {
                _callback(value);
                _expectedSeq += (uint)value.Length;
                continue;
            }

            int overlap = -delta;
            if (overlap < value.Length)
            {
                var tail = new byte[value.Length - overlap];
                Buffer.BlockCopy(value, overlap, tail, 0, tail.Length);
                _callback(tail);
                _expectedSeq += (uint)tail.Length;
            }
        }
    }

    private void ForceFlush()
    {
        _forceFlushCount++;
        _gapStuckCount = 0;
        foreach (var kv in _outOfOrder)
        {
            _callback(kv.Value);
            _expectedSeq = kv.Key + (uint)kv.Value.Length;
        }
        _outOfOrder.Clear();
        _totalBuffered = 0;
    }

    public void Reset()
    {
        _initialized   = false;
        _expectedSeq   = 0;
        _outOfOrder.Clear();
        _totalBuffered = 0;
    }
}
