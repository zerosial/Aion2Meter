using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PacketEngine;

/// PP party-stream parser. Ported from A2Meter.Dps.Protocol.PartyStreamParser.
/// Fires events through the PartyState callback function pointers.
internal sealed class PartyParser
{
    // Event type constants matching A2Power's CbBatchEnd switch.
    private const int EVT_PARTY_LIST    = 1;
    private const int EVT_PARTY_UPDATE  = 2;
    private const int EVT_PARTY_REQUEST = 3;
    private const int EVT_PARTY_ACCEPT  = 4;

    private static readonly byte[] Magic = { 0x06, 0x00, 0x36 };

    private readonly PartyState _state;
    private readonly List<byte> _buffer = new();
    private readonly Dictionary<string, MemberHint> _memberHints = new(StringComparer.Ordinal);

    private bool _justLeft;
    private bool _boardRefreshing;
    private int  _boardRefreshGracePackets;
    private bool _pendingPartyLeft;
    private int  _pendingPartyLeftGracePackets;
    private int  _lastDungeonId;
    private long _zeroLatchedTickMs;
    private const int ZeroLatchHoldMs = 200;

    private sealed class MemberHint
    {
        public uint CharacterId;
        public int  JobCode;
        public int  CombatPower;
    }

    public PartyParser(PartyState state) => _state = state;

    public void Feed(byte[] data, int offset, int length)
    {
        if (length <= 0) return;
        for (int i = 0; i < length; i++)
            _buffer.Add(data[offset + i]);
        Flush();
    }

    public void Reset()
    {
        ClearBoardRefresh();
        _buffer.Clear();
        _pendingPartyLeft = false;
        _pendingPartyLeftGracePackets = 0;
        _justLeft = false;
        _lastDungeonId = 0;
    }

    // ─── framing ────────────────────────────────────────────────────

    private void Flush()
    {
        while (true)
        {
            int idx = ProtocolUtils.IndexOf(_buffer, Magic);
            if (idx < 0) break;
            if (idx > 0)
            {
                var range = _buffer.GetRange(0, idx + 3);
                _buffer.RemoveRange(0, idx + 3);
                if (range.Count > 3)
                {
                    try { ProcessPacket(range.ToArray()); }
                    catch { }
                }
            }
            else
            {
                _buffer.RemoveRange(0, 3);
            }
        }
        if (_buffer.Count > 524288) _buffer.Clear();
    }

    private void ProcessPacket(byte[] packet)
    {
        ScanDungeonIdRaw(packet);
        ScanCombatPowerRaw(packet);
        ScanBoardRefreshRaw(packet);
        ScanPartyLeftRaw(packet);

        bool parsed = false;
        const int MaxIters = 2048;
        Span<byte> span = packet.AsSpan();
        for (int i = 0; i < MaxIters; i++)
        {
            if (span.Length <= 3) break;

            var (declaredLen, varintLen) = ProtocolUtils.ReadVarInt(span);
            if (varintLen < 0) return;

            if (span.Length == declaredLen)
            {
                if (varintLen + 1 < span.Length && span[varintLen] == 0xFF && span[varintLen + 1] == 0xFF)
                {
                    if (span.Length < 10) return;
                    span = span.Slice(10);
                    continue;
                }
                if (!(parsed | ParsePerfectPacket(span.Slice(0, span.Length - 3))))
                    ScanPartyUpdateRaw(packet);
                return;
            }

            int payloadLen = declaredLen - 3;
            if (payloadLen > span.Length)
            {
                if (span.Length >= 10 && span[2] == 0xFF && span[3] == 0xFF)
                    span = span.Slice(10);
                else
                    span = span.Slice(1);
                continue;
            }
            if (payloadLen <= 0) { span = span.Slice(1); continue; }

            var slice = span.Slice(0, payloadLen);
            if (slice.Length > 3) parsed |= ParsePerfectPacket(slice);
            span = span.Slice(payloadLen);
        }
        if (!parsed) ScanPartyUpdateRaw(packet);
    }

    // ─── opcode scans ───────────────────────────────────────────────

    private void ScanDungeonIdRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 10; i++)
        {
            if (packet[i] != 2 || packet[i + 1] != 151) continue;
            int p = i + 2;
            if (p + 4 >= packet.Length || packet[p + 3] != 0) continue;
            p += 4;
            var (val, len) = ProtocolUtils.ReadVarInt(packet.AsSpan(p));
            if (len < 0 || val < 1 || val > 200) continue;
            p += len + val;
            if (p + 5 > packet.Length) continue;
            int marker = packet[p];
            if (marker == 4 || marker == 8)
            {
                p++;
                int dungeonId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(p));
                if (dungeonId >= 600000 && dungeonId < 700000 && dungeonId != _lastDungeonId)
                {
                    _lastDungeonId = dungeonId;
                    int stage = (p + 4 < packet.Length) ? packet[p + 4] : 0;
                    EmitDungeon(dungeonId, stage);
                }
            }
        }
    }

    private void ScanPartyUpdateRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 4; i++)
        {
            if (packet[i] != 2 || packet[i + 1] != 151) continue;
            int blockStart = -1, blockLen = 0;
            for (int j = 1; j <= 3 && i - j >= 0; j++)
            {
                int val = 0, shift = 0;
                bool ok = true;
                for (int k = 0; k < j; k++)
                {
                    byte b = packet[i - j + k];
                    val |= (b & 0x7F) << shift;
                    if (k < j - 1 && (b & 0x80) == 0) { ok = false; break; }
                    if (k == j - 1 && (b & 0x80) != 0) { ok = false; break; }
                    shift += 7;
                }
                if (!ok) continue;
                int payloadLen = val - 3;
                if (payloadLen > 30 && payloadLen < 500)
                {
                    blockStart = i - j;
                    blockLen = Math.Min(payloadLen, packet.Length - blockStart);
                    break;
                }
            }
            if (blockStart < 0 || blockLen <= 0) continue;
            int dataOffset = i + 2;
            int to = Math.Min(blockStart + blockLen, packet.Length);
            var members = CollectPartyMembers(packet, dataOffset, to);
            if (members.Count > 0)
            {
                EmitBatch(EVT_PARTY_UPDATE, members);
                break;
            }
        }
    }

    private void ScanCombatPowerRaw(byte[] packet)
    {
        bool hasCpMarker = false;
        for (int i = 0; i < packet.Length - 1; i++)
            if (packet[i] == 0 && packet[i + 1] == 146) { hasCpMarker = true; break; }
        if (!hasCpMarker) return;

        for (int sync = packet.Length - 3; sync >= 21; sync--)
        {
            if (packet[sync] != 6 || packet[sync + 1] != 0 || packet[sync + 2] != 54) continue;
            if (sync < 21) break;

            bool zeros = true;
            for (int z = sync - 5; z < sync; z++) if (packet[z] != 0) { zeros = false; break; }
            if (!zeros) continue;

            int cp = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 9));
            if (cp < 10000 || cp > 999999) continue;

            int level = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 13));
            if (level < 1000 || level > 5000) continue;
            if (BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 17)) != 0) continue;

            int classId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(sync - 21));
            if (classId < 1 || classId > 55) continue;

            for (int k = 0; k + 3 < sync - 21; k++)
            {
                int sid = packet[k] | (packet[k + 1] << 8);
                if (sid < 1001 || sid > 2021) continue;
                int nameLen = packet[k + 2];
                if (nameLen < 3 || nameLen > 48 || k + 3 + nameLen > sync - 21) continue;
                try
                {
                    string nick = Encoding.UTF8.GetString(packet, k + 3, nameLen);
                    if (string.IsNullOrEmpty(nick) || nick.Length < 2) continue;
                    bool clean = true;
                    foreach (char c in nick)
                    {
                        if ((c < '가' || c > '힣') && (c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9'))
                        { clean = false; break; }
                    }
                    if (!clean) continue;
                    RememberMemberHint(nick, sid, 0u, 0, cp);
                    _state.FireCombatPowerDetected(nick, sid, cp);
                    return;
                }
                catch { }
            }
        }
    }

    private void ScanBoardRefreshRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 1; i++)
        {
            if (packet[i] == 42 && packet[i + 1] == 151) { ArmBoardRefresh(); break; }
            if (packet[i] == 19 && packet[i + 1] == 151 && i + 3 < packet.Length && packet[i + 2] == 0 && packet[i + 3] == 0)
            { ArmBoardRefresh(); break; }
        }
    }

    private void ScanPartyLeftRaw(byte[] packet)
    {
        for (int i = 0; i < packet.Length - 1; i++)
        {
            if (packet[i] == 29 && packet[i + 1] == 151 && i + 3 < packet.Length && packet[i + 2] == 0 && packet[i + 3] == 0)
            { ArmPendingPartyLeft(); break; }
        }
    }

    // ─── perfect-frame parser ───────────────────────────────────────

    private bool ParsePerfectPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 3) return false;
        int varintLen = ProtocolUtils.ReadVarInt(packet).length;
        if (varintLen < 0) return false;

        int p = varintLen;
        if (p + 1 >= packet.Length) return false;

        byte op = packet[p];
        if (packet[p + 1] != 151) return false;
        int dataOff = p + 2;

        if (op != 42) { AdvanceBoardRefresh(); AdvancePendingPartyLeft(); }

        switch (op)
        {
            case 19:
                if (IsEmptyControlPacket(packet, dataOff)) { ArmBoardRefresh(); return false; }
                return false;

            case 1:
            {
                if (_justLeft) { _justLeft = false; return false; }
                if (dataOff + 1 < packet.Length && packet[dataOff] == 0 && packet[dataOff + 1] == 0)
                {
                    if (dataOff + 6 <= packet.Length)
                    {
                        int off = dataOff + 2;
                        int did = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(off, packet.Length - off));
                        if (did >= 600000 && did < 700000)
                        {
                            int stage = (dataOff + 6 < packet.Length) ? packet[dataOff + 6] : 0;
                            EmitDungeon(did, stage);
                        }
                    }
                    if (ConsumeBoardRefresh()) { ClearPendingPartyLeft(); return false; }
                    if (ConsumePendingPartyLeft())
                    {
                        _justLeft = true;
                        bool wasDungeon = _lastDungeonId != 0;
                        _lastDungeonId = 0;
                        ClearBoardRefresh();
                        if (wasDungeon) EmitDungeonZero();
                        _state.FirePartyLeft();
                        return false;
                    }
                    bool hadDungeon = _lastDungeonId != 0;
                    _lastDungeonId = 0;
                    if (hadDungeon) EmitDungeonZero();
                    _state.FirePartyEjected();
                    return false;
                }
                var members = CollectPartyMembers(packet, dataOff, packet.Length);
                if (members.Count > 0)
                {
                    ClearPendingPartyLeft(); ClearBoardRefresh();
                    EmitBatch(EVT_PARTY_LIST, members);
                    return true;
                }
                return false;
            }

            case 2:
            {
                TryParseDungeonId(packet, dataOff);
                var members = CollectPartyMembers(packet, dataOff, packet.Length);
                if (members.Count > 0)
                {
                    ClearPendingPartyLeft();
                    EmitBatch(EVT_PARTY_UPDATE, members);
                    return true;
                }
                return false;
            }

            case 7:
            {
                var req = ParsePartyRequest(packet, dataOff);
                if (req != null)
                {
                    ClearPendingPartyLeft();
                    RememberMemberHint(req.Value);
                    EmitSingle(EVT_PARTY_REQUEST, req.Value);
                    return true;
                }
                return false;
            }

            case 11:
            {
                var acc = ParsePartyAcceptMember(packet, dataOff);
                if (acc != null)
                {
                    ClearPendingPartyLeft();
                    RememberMemberHint(acc.Value);
                    EmitSingle(EVT_PARTY_ACCEPT, acc.Value);
                    return true;
                }
                return false;
            }

            case 4:
                if (_lastDungeonId != 0)
                {
                    _lastDungeonId = 0;
                    _state.FireDungeonDetected(0, 0);
                }
                return false;

            case 29:
            {
                if (IsEmptyControlPacket(packet, dataOff))
                {
                    ArmPendingPartyLeft();
                    return false;
                }
                ClearPendingPartyLeft();
                _justLeft = true;
                bool wasDungeon = _lastDungeonId != 0;
                _lastDungeonId = 0;
                ClearBoardRefresh();
                if (wasDungeon) EmitDungeonZero();
                _state.FirePartyLeft();
                return false;
            }

            case 42:
                ArmBoardRefresh();
                return false;

            default:
                return false;
        }
    }

    private void TryParseDungeonId(ReadOnlySpan<byte> packet, int dataOffset)
    {
        try
        {
            int p = dataOffset;
            if (p + 4 >= packet.Length) return;
            p += 3;
            if (packet[p] != 0) return;
            p++;
            var (val, len) = ProtocolUtils.ReadVarInt(packet.Slice(p));
            if (len < 0 || val < 0) return;
            p += len + val;
            if (p >= packet.Length) return;
            p++;
            if (p + 4 > packet.Length) return;
            int dungeonId = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(p));
            p += 4;
            if (dungeonId == 0)
            {
                if (_lastDungeonId != 0) { _lastDungeonId = 0; EmitDungeonZero(); }
            }
            else if (dungeonId >= 600000 && dungeonId < 700000)
            {
                int stage = (p < packet.Length) ? packet[p] : 0;
                EmitDungeon(dungeonId, stage);
            }
        }
        catch { }
    }

    private void EmitDungeon(int dungeonId, int stage)
    {
        if (!ZeroLatchActive(dungeonId))
            _state.FireDungeonDetected(dungeonId, stage);
    }

    private void EmitDungeonZero()
    {
        _zeroLatchedTickMs = Environment.TickCount64;
        _state.FireDungeonDetected(0, 0);
    }

    private bool ZeroLatchActive(int dungeonId)
    {
        if (dungeonId == 0) return false;
        if (_zeroLatchedTickMs == 0) return false;
        return (Environment.TickCount64 - _zeroLatchedTickMs) < ZeroLatchHoldMs;
    }

    // ─── member emission helpers ────────────────────────────────────

    private void EmitBatch(int eventType, List<MemberData> members)
    {
        _state.FireBatchStart(eventType, members.Count);
        foreach (var m in members)
        {
            _state.FireMember(eventType, m.CharacterId, m.ServerId, m.Nickname,
                              m.JobCode, m.JobName, m.Level, m.CombatPower);
        }
        _state.FireBatchEnd(eventType);
    }

    private void EmitSingle(int eventType, MemberData m)
    {
        _state.FireBatchStart(eventType, 1);
        _state.FireMember(eventType, m.CharacterId, m.ServerId, m.Nickname,
                          m.JobCode, m.JobName, m.Level, m.CombatPower);
        _state.FireBatchEnd(eventType);
    }

    // ─── member parsing ─────────────────────────────────────────────

    private struct MemberData
    {
        public uint CharacterId;
        public int  ServerId;
        public string Nickname;
        public int  JobCode;
        public string JobName;
        public int  Level;
        public int  CombatPower;
    }

    private List<MemberData> CollectPartyMembers(ReadOnlySpan<byte> packet, int from, int to)
    {
        var members = new List<MemberData>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int lo = Math.Max(0, from);
        int hi = Math.Min(packet.Length, to);
        for (int i = lo; i < hi; i++)
        {
            if (TryParsePartyMember(packet, i, hi, out var member))
            {
                var key = $"{member.ServerId}:{member.Nickname}";
                if (!seen.Contains(key))
                {
                    member = ApplyMemberHint(member);
                    seen.Add(key);
                    RememberMemberHint(member);
                    members.Add(member);
                }
            }
        }
        return members;
    }

    private bool TryParsePartyMember(ReadOnlySpan<byte> packet, int offset, int limit, out MemberData member)
    {
        member = default;
        int nameLen = packet[offset];
        if (nameLen < 2 || nameLen > 48) return false;
        if (offset + 1 + nameLen > limit) return false;

        string nick;
        try { nick = Encoding.UTF8.GetString(packet.Slice(offset + 1, nameLen)); }
        catch { return false; }
        if (!IsValidNickname(nick)) return false;

        int afterName = offset + 1 + nameLen;

        uint characterId = 0;
        if (offset >= 8)
        {
            int p = offset - 8;
            characterId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(p));
        }

        int sidGuess = -1;
        if (offset >= 2)
        {
            int s = packet[offset - 2] | (packet[offset - 1] << 8);
            if (ServerMap.IsValidServerId(s)) sidGuess = s;
        }

        int jobCode = 0, level = 0, cp = 0;
        if (TryReadFixedStats(packet, afterName, limit, out var jc, out var lv, out var cpv, out var sidv))
        {
            jobCode = jc; level = lv; cp = cpv;
            if (sidGuess <= 0) sidGuess = sidv;
        }
        if (jobCode == 0 && TryReadLeadingJob(packet, afterName, limit, out var jc2, out var lv2))
        {
            jobCode = jc2;
            if (level == 0) level = lv2;
        }

        bool gotCp = TryReadServerAndCpAfterName(packet, afterName, limit, sidGuess,
                                                  out var sidAfter, out var cpAfter, out var anchor);
        if (sidGuess <= 0) sidGuess = sidAfter;
        if (cp <= 0) cp = cpAfter;

        if ((jobCode == 0 || level == 0) && anchor >= 0
            && TryReadStatsFromAnchor(packet, anchor, limit, out var jc3, out var lv3, out var sid3))
        {
            if (jobCode == 0) jobCode = jc3;
            if (level == 0)   level   = lv3;
            if (sidGuess <= 0) sidGuess = sid3;
        }

        if (!ServerMap.IsValidServerId(sidGuess)) return false;
        if (!(characterId != 0 || jobCode != 0 || cp > 0 || gotCp)) return false;

        member = new MemberData
        {
            CharacterId = characterId,
            ServerId    = sidGuess,
            Nickname    = nick,
            JobCode     = jobCode,
            JobName     = JobMapping.GetName(jobCode),
            Level       = level,
            CombatPower = cp,
        };
        return true;
    }

    private static MemberData? ParsePartyRequest(ReadOnlySpan<byte> packet, int dataOffset)
    {
        int len = packet.Length;
        for (int i = 0; i <= 40; i++)
        {
            for (int j = 1; j <= 48; j++)
            {
                int posLen = len - j - 1 - i;
                if (posLen < dataOffset + 12 || packet[posLen] != j) continue;

                string nick;
                try { nick = Encoding.UTF8.GetString(packet.Slice(posLen + 1, j)); }
                catch { continue; }
                if (!IsValidNickname(nick)) continue;

                int posStats = posLen - 12;
                if (posStats < dataOffset) continue;

                uint jobCode = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(posStats));
                uint level   = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(posStats + 4));
                if (level >= 1 && level <= 55)
                {
                    int posCp = posLen + 1 + j + 6;
                    uint cp = 0;
                    if (posCp + 4 <= len)
                        cp = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(posCp));

                    int sid = 0;
                    if (dataOffset + 11 < len)
                        sid = packet[dataOffset + 10] | (packet[dataOffset + 11] << 8);

                    return new MemberData
                    {
                        ServerId    = sid,
                        Nickname    = nick,
                        JobCode     = (int)jobCode,
                        JobName     = JobMapping.GetName((int)jobCode),
                        Level       = (int)level,
                        CombatPower = (int)cp,
                    };
                }
            }
        }
        return null;
    }

    private static MemberData? ParsePartyAcceptMember(ReadOnlySpan<byte> packet, int dataOffset)
    {
        if (dataOffset + 25 > packet.Length) return null;
        byte tag = packet[dataOffset];
        if (tag != 26 && tag != 58) return null;

        uint characterId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(dataOffset + 2));
        int sid = packet[dataOffset + 8] | (packet[dataOffset + 9] << 8);
        int nameLen = packet[dataOffset + 10];
        if (nameLen < 1 || nameLen > 48) return null;
        if (dataOffset + 11 + nameLen + 12 > packet.Length) return null;

        string nick;
        try { nick = Encoding.UTF8.GetString(packet.Slice(dataOffset + 11, nameLen)); }
        catch { return null; }
        if (!IsValidNickname(nick)) return null;

        int afterName = dataOffset + 11 + nameLen;
        uint jobCode = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(afterName));
        uint level   = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(afterName + 4));
        if (level < 1 || level > 55) return null;

        uint cp;
        if (tag == 26)
        {
            cp = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(afterName + 8));
        }
        else
        {
            cp = 0;
            int sc1 = afterName + 13;
            int sc2 = afterName + 15;
            int mi  = afterName + 17;
            int cpP = afterName + 18;
            if (cpP < packet.Length && sc2 + 1 < packet.Length
                && (packet[sc1] | (packet[sc1 + 1] << 8)) == sid
                && (packet[sc2] | (packet[sc2 + 1] << 8)) == sid
                && packet[mi] == 4
                && TryReadCombatPower(packet, cpP, packet.Length, out var cpVal))
            {
                cp = (uint)cpVal;
            }
        }
        if (cp > 9999999) return null;

        return new MemberData
        {
            CharacterId = characterId,
            ServerId    = sid,
            Nickname    = nick,
            JobCode     = (int)jobCode,
            JobName     = JobMapping.GetName((int)jobCode),
            Level       = (int)level,
            CombatPower = (int)cp,
        };
    }

    // ─── stats helpers ──────────────────────────────────────────────

    private static bool TryReadFixedStats(ReadOnlySpan<byte> p, int off, int lim,
                                          out int jc, out int lv, out int cp, out int sid)
    {
        jc = 0; lv = 0; cp = 0; sid = -1;
        if (off + 21 > lim) return false;
        int j = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(off));
        if (!IsPlausibleJobCode(j)) return false;
        int l = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(off + 4));
        if (l < 1 || l > 55) return false;
        int c = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(off + 8));
        if (c < 500 || c > 9999999) return false;
        int sA = p[off + 12] | (p[off + 13] << 8);
        int sB = p[off + 14] | (p[off + 15] << 8);
        int s = ServerMap.IsValidServerId(sB) ? sB : (ServerMap.IsValidServerId(sA) ? sA : -1);
        if (s <= 0) return false;
        int cf = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(off + 17));
        jc = j; lv = l;
        cp = IsPlausibleCombatPower(cf) ? cf : 0;
        sid = s;
        return true;
    }

    private static bool TryReadLeadingJob(ReadOnlySpan<byte> p, int off, int lim, out int jc, out int lv)
    {
        jc = 0; lv = 0;
        if (off >= lim) return false;
        if (off + 8 <= lim)
        {
            int j = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(off));
            int l = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(off + 4));
            if (IsPlausibleJobCode(j) && l >= 1 && l <= 55) { jc = j; lv = l; return true; }
        }
        int j1 = p[off];
        if (!IsPlausibleJobCode(j1))
        {
            int v = ProtocolUtils.ReadVarInt(p.Slice(off)).value;
            if (!IsPlausibleJobCode(v)) return false;
            j1 = v;
        }
        jc = j1;
        if (off + 1 < lim) { int lb = p[off + 1]; if (lb >= 1 && lb <= 55) lv = lb; }
        return true;
    }

    private static bool TryReadServerAndCpAfterName(ReadOnlySpan<byte> p, int ne, int lim, int pref,
                                                     out int sid, out int cp, out int anchor)
    {
        sid = -1; cp = 0; anchor = -1;
        int hi = Math.Min(lim - 2, ne + 20);
        for (int i = ne; i < hi; i++)
        {
            int s = p[i] | (p[i + 1] << 8);
            if (!ServerMap.IsValidServerId(s)) continue;
            int cs = -1;
            if (i + 2 < lim && p[i + 2] == 4) cs = i + 3;
            else if (i + 4 < lim && (p[i + 2] | (p[i + 3] << 8)) == s && p[i + 4] == 4) cs = i + 5;
            if (cs >= 0 && (pref <= 0 || s == pref))
            {
                sid = s; anchor = i;
                if (TryReadCombatPower(p, cs, lim, out var c)) cp = c;
                return true;
            }
        }
        if (pref > 0 && TryReadPackedCpAfterStats(p, ne, lim, out var c2))
        { sid = pref; cp = c2; return true; }
        return false;
    }

    private static bool TryReadStatsFromAnchor(ReadOnlySpan<byte> p, int ap, int lim,
                                                out int jc, out int lv, out int sid)
    {
        jc = 0; lv = 0; sid = -1;
        for (int back = 12; back <= 16; back++)
        {
            int o = ap - back;
            if (o < 0 || o + 16 > lim) continue;
            int j = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(o));
            int l = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(o + 4));
            int sA = p[o + 12] | (p[o + 13] << 8);
            int sB = (o + 15 < lim) ? (p[o + 14] | (p[o + 15] << 8)) : -1;
            int s = ServerMap.IsValidServerId(sB) ? sB : (ServerMap.IsValidServerId(sA) ? sA : -1);
            if (IsPlausibleJobCode(j) && l >= 1 && l <= 55 && s > 0)
            { jc = j; lv = l; sid = s; return true; }
        }
        return false;
    }

    private static bool TryReadPackedCpAfterStats(ReadOnlySpan<byte> p, int off, int lim, out int cp)
    {
        cp = 0;
        int lo = Math.Min(lim, off + 8);
        int hi = Math.Min(lim - 3, off + 24);
        for (int i = lo; i < hi; i++)
        {
            if (p[i] == 4 && TryReadCombatPower(p, i + 1, lim, out var c)) { cp = c; return true; }
        }
        return false;
    }

    private static bool TryReadCombatPower(ReadOnlySpan<byte> p, int off, int lim, out int cp)
    {
        cp = 0;
        if (off + 4 <= lim)
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(off));
            if (IsPlausibleCombatPower(v)) { cp = v; return true; }
        }
        if (off + 3 <= lim)
        {
            int v = p[off] | (p[off + 1] << 8) | (p[off + 2] << 16);
            if (IsPlausibleCombatPower(v)) { cp = v; return true; }
        }
        return false;
    }

    // ─── hint cache ─────────────────────────────────────────────────

    private MemberData ApplyMemberHint(MemberData m)
    {
        if (m.ServerId <= 0 || string.IsNullOrEmpty(m.Nickname)) return m;
        var key = $"{m.ServerId}:{m.Nickname}";
        if (_memberHints.TryGetValue(key, out var h))
        {
            if (m.CharacterId == 0) m.CharacterId = h.CharacterId;
            if (m.JobCode <= 0 && h.JobCode > 0) m.JobCode = h.JobCode;
            if (h.CombatPower > m.CombatPower) m.CombatPower = h.CombatPower;
        }
        m.JobName = JobMapping.GetName(m.JobCode);
        return m;
    }

    private void RememberMemberHint(MemberData m)
        => RememberMemberHint(m.Nickname, m.ServerId, m.CharacterId, m.JobCode, m.CombatPower);

    private void RememberMemberHint(string nick, int sid, uint charId = 0, int jc = 0, int cp = 0)
    {
        if (sid <= 0 || string.IsNullOrEmpty(nick)) return;
        var key = $"{sid}:{nick}";
        if (!_memberHints.TryGetValue(key, out var h))
        {
            h = new MemberHint();
            _memberHints[key] = h;
        }
        if (charId != 0) h.CharacterId = charId;
        if (jc > 0) h.JobCode = jc;
        if (IsPlausibleCombatPower(cp) && cp > h.CombatPower) h.CombatPower = cp;
    }

    // ─── grace counters ─────────────────────────────────────────────

    private void ArmBoardRefresh()
    {
        _boardRefreshing = true;
        if (_boardRefreshGracePackets < 6) _boardRefreshGracePackets = 6;
    }

    private void ArmPendingPartyLeft()
    {
        _pendingPartyLeft = true;
        if (_pendingPartyLeftGracePackets < 12) _pendingPartyLeftGracePackets = 12;
    }

    private void AdvanceBoardRefresh()
    {
        if (!_boardRefreshing) return;
        if (_boardRefreshGracePackets > 0) _boardRefreshGracePackets--;
        if (_boardRefreshGracePackets <= 0) ClearBoardRefresh();
    }

    private void AdvancePendingPartyLeft()
    {
        if (!_pendingPartyLeft) return;
        if (_pendingPartyLeftGracePackets > 0) _pendingPartyLeftGracePackets--;
        if (_pendingPartyLeftGracePackets <= 0) ClearPendingPartyLeft();
    }

    private bool ConsumeBoardRefresh()
    {
        if (!_boardRefreshing) return false;
        ClearBoardRefresh(); return true;
    }

    private bool ConsumePendingPartyLeft()
    {
        if (!_pendingPartyLeft) return false;
        ClearPendingPartyLeft(); return true;
    }

    private void ClearBoardRefresh()
    {
        _boardRefreshing = false;
        _boardRefreshGracePackets = 0;
    }

    private void ClearPendingPartyLeft()
    {
        _pendingPartyLeft = false;
        _pendingPartyLeftGracePackets = 0;
    }

    // ─── util ───────────────────────────────────────────────────────

    private static bool IsEmptyControlPacket(ReadOnlySpan<byte> p, int off)
    {
        if (off + 1 < p.Length && p[off] == 0 && p[off + 1] == 0) return p.Length <= off + 2;
        return false;
    }

    private static bool IsValidNickname(string? nick)
    {
        if (string.IsNullOrEmpty(nick)) return false;
        if (Regex.IsMatch(nick, "^[0-9]+$")) return false;
        return Regex.IsMatch(nick, "^[가-힣a-zA-Z0-9]+$");
    }

    private static bool IsPlausibleJobCode(int jc) => jc >= 1 && jc <= 40;
    private static bool IsPlausibleCombatPower(int cp) => cp >= 10000 && cp <= 999999;
}
