using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace A2Meter.Dps.Protocol;

/// Slimmed port of A2Viewer.Packet.PacketProcessor.
///
/// Differences vs. the original:
///   * Single-threaded — TcpSegments are processed inline on the source thread.
///     The original used a worker pool to spread load across cores; replay and
///     a typical capture stream don't need that, and synchronous processing
///     keeps the call stack debuggable.
///   * No native PacketEngine.dll. The C# PacketDispatcher (next port) handles
///     damage/user-info parsing on its own.
///   * Server-port detection mirrors the original: known seed (13328) confirms
///     in 1 hit, other ports require a magic-payload hit.
internal sealed class PacketProcessor
{
    private const int KnownSeedServerPort = 13328;

    private readonly Action<byte[], int, int> _messageHook;
    private readonly Action<string>? _logSink;
    private readonly bool _enableTcpReorder;

    private readonly ConcurrentDictionary<FlowKey, ChannelState> _channels = new();
    private int _serverPort;            // 0 until detected
    private readonly object _portLock = new();

    public int ServerPort => _serverPort;

    public PacketProcessor(
        Action<byte[], int, int> messageHook,
        Action<string>? logSink = null,
        int initialServerPort = 0,
        bool enableTcpReorder = true)
    {
        _messageHook = messageHook;
        _logSink = logSink;
        _serverPort = initialServerPort;
        _enableTcpReorder = enableTcpReorder;
    }

    public void Reset()
    {
        lock (_portLock) _serverPort = 0;
        foreach (var c in _channels.Values) c.Reset();
        _channels.Clear();
    }

    public void Feed(in Dps.TcpSegment seg)
    {
        if (seg.Payload.Length == 0) return;

        if (!TryAcceptByPort(seg, out bool isFromServer)) return;

        var key = isFromServer
            ? new FlowKey(seg.SrcAddress, seg.SrcPort, seg.DstAddress, seg.DstPort)
            : new FlowKey(seg.DstAddress, seg.DstPort, seg.SrcAddress, seg.SrcPort);
        // Channels are keyed in server→client orientation so both directions of
        // the same socket pair share state.
        var channel = _channels.GetOrAdd(key, _ => new ChannelState(_messageHook, _logSink));
        channel.Stream.SetFlowContext(
            $"{key.Src}:{key.SrcPort}->{key.Dst}:{key.DstPort}",
            "game");

        var reassembler = isFromServer ? channel.ServerToClient : channel.ClientToServer;
        if (_enableTcpReorder)
            reassembler.Feed(seg.SeqNumber, seg.Payload);
        else if (isFromServer)
            channel.Stream.ProcessData(seg.Payload);
    }

    private bool TryAcceptByPort(in Dps.TcpSegment seg, out bool isFromServer)
    {
        isFromServer = false;

        int sp = _serverPort;
        if (sp != 0)
        {
            if (seg.SrcPort == sp) { isFromServer = true;  return true; }
            if (seg.DstPort == sp) { isFromServer = false; return true; }
            return false;
        }

        // Detection phase.
        lock (_portLock)
        {
            if (_serverPort == 0)
            {
                int candidate = ResolveServerPortCandidate(seg);
                if (candidate == 0) return false;
                _serverPort = candidate;
                _logSink?.Invoke($"[PacketProcessor] Combat port detected: {candidate}");
            }
            sp = _serverPort;
        }
        if (seg.SrcPort == sp) { isFromServer = true;  return true; }
        if (seg.DstPort == sp) { isFromServer = false; return true; }
        return false;
    }

    private static int ResolveServerPortCandidate(in Dps.TcpSegment seg)
    {
        // Fast path for the well-known game port.
        if (seg.SrcPort == KnownSeedServerPort) return KnownSeedServerPort;
        if (seg.DstPort == KnownSeedServerPort) return KnownSeedServerPort;

        // Heuristic for any other port: needs to look like a framed game payload
        // and originate from a non-ephemeral high port (the original required >1024
        // on both ends).
        if (seg.SrcPort <= 1024 || seg.DstPort <= 1024) return 0;
        if (!ProtocolUtils.LooksLikeGameMagicPayload(seg.Payload)) return 0;
        return Math.Max(seg.SrcPort, seg.DstPort);
    }
}
