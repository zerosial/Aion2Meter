using System;
using System.Net;

namespace A2Meter.Dps;

/// Single TCP segment payload, direction-tagged. Live capture and pcap replay
/// both produce these so downstream parsers don't care which one is the source.
internal readonly record struct TcpSegment(
    DateTime TimestampUtc,
    IPAddress SrcAddress, int SrcPort,
    IPAddress DstAddress, int DstPort,
    uint SeqNumber,
    byte[] Payload);

/// Aggregated combat hit raised by the protocol parser through IPacketSource.
/// hitFlags bitmask: 0x01=back, 0x02|0x04=block, 0x08=perfect, 0x10=hardHit, 0x100=crit
internal readonly record struct CombatHitArgs(
    int ActorId, int TargetId, string Name, int JobCode,
    long Damage, uint HitFlags, bool IsHeal, string? Skill,
    int ExtraHits, bool IsDot, int[]? Specs);

/// Common contract for anything that produces ordered TCP segments.
/// Two implementations: LivePacketSniffer (Npcap/SharpPcap) and PcapReplaySource (offline file).
internal interface IPacketSource : IDisposable
{
    bool IsRunning { get; }
    event Action<TcpSegment>? SegmentReceived;

    /// Combat events parsed downstream. Wired up by the protocol parser, not the source itself.
    event Action<CombatHitArgs>? CombatHit;
    event Action<MobTarget?>? TargetChanged;
    event Action<PartyMember>? PartyMemberSeen;
    event Action? PartyLeft;
    event Action<int, int>? DungeonDetected;

    void Start();
    void Stop();
}
