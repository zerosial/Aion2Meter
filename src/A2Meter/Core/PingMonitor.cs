using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using A2Meter.Dps;

namespace A2Meter.Core;

/// Estimates network latency (ping) to the game server by measuring TCP RTT
/// from packet timestamps. Tracks outgoing data packets and their ACKs.
internal sealed class PingMonitor
{
    private readonly object _lock = new();

    // Outgoing seq → send timestamp (only keep last N entries)
    private readonly Queue<(uint Seq, long Tick)> _pending = new();
    private const int MaxPending = 64;

    // Rolling window of RTT samples for averaging.
    private readonly Queue<double> _samples = new();
    private const int MaxSamples = 20;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "ping_debug.log");

    /// Current smoothed ping in milliseconds.
    public int CurrentPingMs { get; private set; }

    /// Server IP detected from traffic.
    public IPAddress? ServerAddress { get; private set; }

    private int _logCount;

    private static void Log(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    /// Feed a TCP segment to the monitor. Call for every captured packet.
    public void Feed(TcpSegment seg)
    {
        // Determine direction: game server uses port 13328.
        bool isOutgoing = seg.DstPort == 13328;
        bool isIncoming = seg.SrcPort == 13328;

        if (!isOutgoing && !isIncoming)
        {
            if (_logCount < 5)
            {
                _logCount++;
                Log($"Ignored seg: {seg.SrcPort}→{seg.DstPort} len={seg.Payload.Length}");
            }
            return;
        }

        if (_logCount < 20)
        {
            _logCount++;
            Log($"Feed: {(isOutgoing ? "OUT" : "IN")} {seg.SrcPort}→{seg.DstPort} len={seg.Payload.Length} ping={CurrentPingMs}");
        }

        if (isIncoming && ServerAddress == null)
            ServerAddress = seg.SrcAddress;

        lock (_lock)
        {
            if (isOutgoing && seg.Payload.Length > 0)
            {
                // Record outgoing data packet's seq + timestamp.
                long tick = Stopwatch.GetTimestamp();
                _pending.Enqueue((seg.SeqNumber + (uint)seg.Payload.Length, tick));
                if (_pending.Count > MaxPending)
                    _pending.Dequeue();
            }
            else if (isIncoming && seg.Payload.Length >= 0)
            {
                // Incoming packet implies the server received our data.
                // Use the timestamp difference as a rough RTT estimate.
                // This is approximate since we can't see individual ACK numbers
                // from the simplified TcpSegment (no AckNumber field).
                // Instead, we use a simple heuristic: measure time between
                // our last outgoing packet and the next incoming response.
                if (_pending.Count > 0)
                {
                    var (_, sendTick) = _pending.Dequeue();
                    long now = Stopwatch.GetTimestamp();
                    double rttMs = (double)(now - sendTick) / Stopwatch.Frequency * 1000.0;

                    // Sanity: ignore obviously wrong values.
                    if (rttMs > 0 && rttMs < 5000)
                    {
                        _samples.Enqueue(rttMs);
                        if (_samples.Count > MaxSamples)
                            _samples.Dequeue();

                        // Compute average.
                        double sum = 0;
                        foreach (var s in _samples) sum += s;
                        CurrentPingMs = (int)Math.Round(sum / _samples.Count);
                    }
                }
            }
        }
    }
}
