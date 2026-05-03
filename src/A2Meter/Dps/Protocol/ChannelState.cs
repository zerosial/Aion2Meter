using System;

namespace A2Meter.Dps.Protocol;

/// Per-flow state: one TcpReassembler for each direction plus a StreamProcessor
/// that turns the server-to-client byte stream into framed messages.
internal sealed class ChannelState
{
    public TcpReassembler ServerToClient { get; }
    public TcpReassembler ClientToServer { get; }
    public StreamProcessor Stream { get; }

    public ChannelState(Action<byte[], int, int> messageHook, Action<string>? logSink = null)
    {
        Stream = new StreamProcessor(messageHook, logSink);
        ServerToClient = new TcpReassembler(buf => Stream.ProcessData(buf));
        // Client->server bytes carry no combat data; we still keep a reassembler
        // so a future feature (uplink commands) can plug in without surgery.
        ClientToServer = new TcpReassembler(_ => { });
    }

    public void Reset()
    {
        ServerToClient.Reset();
        ClientToServer.Reset();
    }
}
