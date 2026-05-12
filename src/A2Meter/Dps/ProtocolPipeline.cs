using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using A2Meter.Dps.Protocol;

namespace A2Meter.Dps;

/// Glues TcpSegment events from any IPacketSource into the C# protocol stack
/// (PacketProcessor → StreamProcessor → PacketDispatcher) and re-raises the
/// resulting combat events on the source itself, so DpsPipeline keeps using
/// IPacketSource.CombatHit / TargetChanged / PartyMemberSeen without knowing
/// that there's a parser sitting in between.
///
/// When the native PacketEngine.dll is available, it is used as the primary
/// parser for combat data (higher fidelity). The C# PacketDispatcher serves
/// as a fallback when the DLL is absent.
internal sealed class ProtocolPipeline : IDisposable
{
    private readonly IPacketSource _source;
    private readonly SkillDatabase _skills;
    private readonly PacketDispatcher _dispatcher;
    private readonly NativePacketEngine? _native;
    private readonly PacketProcessor _processor;
    private readonly PartyStreamParser _party;

    /// entityId → (nickname, jobCode) populated by UserInfo packets.
    /// Damage events arrive before/after name packets in any order, so we
    /// stash names here and enrich CombatHit on emission.
    private readonly ConcurrentDictionary<int, (string Name, int JobCode)> _identities = new();

    /// petEntityId → ownerEntityId. Populated by Summon events so that
    /// summon damage is attributed to the summoning player.
    private readonly ConcurrentDictionary<int, int> _summons = new();

    /// Sub-hit skill codes whose damage should be attributed to a parent skill
    /// name. Matches original A2Power SubHitParentMap.
    private static readonly Dictionary<int, int> SubHitParentMap = new() { { 17040000, 17050000 } };

    /// Currently focused boss. Set on the first damage that lands on a known
    /// boss — *not* on MobSpawn — so multi-boss zones (where two bosses spawn
    /// almost simultaneously) latch onto whichever pull actually starts first.
    private MobTarget? _currentTarget;
    private int _currentTargetEntityId;
    /// All bosses we've seen MobSpawn for, indexed by entityId, so the damage
    /// path can promote one of them to _currentTarget when hits arrive.
    private readonly System.Collections.Generic.Dictionary<int, MobTarget> _knownBosses = new();

    public ProtocolPipeline(IPacketSource source, SkillDatabase? skills = null, Action<string>? log = null)
    {
        _source = source;
        _skills = skills ?? SkillDatabase.Shared;

        _dispatcher = new PacketDispatcher(_skills, log);
        _native = NativePacketEngine.TryCreate(_skills, log);
        log?.Invoke(_native != null
            ? "[Pipeline] using NATIVE PacketEngine"
            : "[Pipeline] using C# PacketDispatcher fallback");
        _party = new PartyStreamParser();

        _processor  = new PacketProcessor(
            messageHook: (data, off, len) =>
            {
                if (_native != null)
                    _native.Dispatch(data, off, len);
                else
                    _dispatcher.Dispatch(data, off, len);
                _party.Feed(new ReadOnlySpan<byte>(data, off, len));
            },
            logSink: log);

        _source.SegmentReceived += OnSegment;

        // Wire events from whichever engine is active.
        if (_native != null)
        {
            _native.Damage      += OnDamage;
            _native.UserInfo    += OnUserInfo;
            _native.MobSpawn    += OnMobSpawn;
            _native.BossHp      += OnBossHp;
            _native.CombatPower += OnCombatPower;
            _native.Summon      += OnSummon;
        }
        else
        {
            _dispatcher.Damage      += OnDamage;
            _dispatcher.UserInfo    += OnUserInfo;
            _dispatcher.MobSpawn    += OnMobSpawn;
            _dispatcher.BossHp      += OnBossHp;
            _dispatcher.CombatPower += OnCombatPower;
        }

        _party.PartyList    += OnPartyRoster;
        _party.PartyUpdate  += OnPartyRoster;
        _party.PartyAccept  += OnPartyMember;
        _party.PartyRequest += OnPartyMember;
        _party.PartyLeft    += OnPartyLeft;
        _party.PartyEjected += OnPartyLeft;
        _party.CombatPowerDetected += OnPartyCpByName;
        _party.DungeonDetected += (dId, stg) => (_source as IInternalEventRaise)?.RaiseDungeonChanged(dId);
    }

    public void Dispose()
    {
        _source.SegmentReceived -= OnSegment;
        _native?.Dispose();
    }

    private void OnSegment(TcpSegment seg) => _processor.Feed(seg);

    private void OnDamage(int actorId, int targetId, int skillCode, byte damageType,
                          int damage, uint specialFlags, int multiHitCount, int multiHitDamage,
                          int healAmount, int isDot)
    {
        bool isHeal = healAmount > 0 && damage == 0;
        long total  = damage + multiHitDamage + healAmount;
        if (total <= 0) return;

        // Resolve summon → owner so pet damage is attributed to the player.
        int resolvedActor = _summons.TryGetValue(actorId, out var owner) ? owner : actorId;

        // Decode specialization tiers from the raw → base skill code delta.
        int[]? specs = null;
        int rawSkill = _skills.LastRawSkillCode;
        if (isDot == 0)
        {
            if (rawSkill != 0 && rawSkill != skillCode)
                specs = SkillDatabase.DecodeSpecializations(rawSkill, skillCode);
        }

        // SubHitParentMap: remap sub-hit skill codes to parent for name lookup (matches original).
        int nameCode = skillCode;
        if (isDot == 0 && rawSkill % 10 != 0 && SubHitParentMap.TryGetValue(skillCode, out var parentCode))
            nameCode = parentCode;

        var skillName = _skills.GetSkillName(nameCode) ?? $"스킬#{nameCode}";
        var (name, jobCode) = _identities.TryGetValue(resolvedActor, out var id)
            ? id : ($"#{resolvedActor}", -1);

        TriggerCombatHit(resolvedActor, targetId, name, jobCode, total, specialFlags, isHeal, skillName, multiHitCount, isDot != 0, specs);

        if (isHeal) return;

        // Late-bind the focused boss to whichever known boss is actually being
        // hit. This is the key to multi-boss zones (환영의 회랑) where two
        // bosses spawn near-simultaneously — we follow the damage flow.
        if (_knownBosses.TryGetValue(targetId, out var bossForHit))
        {
            if (_currentTargetEntityId != targetId)
            {
                _currentTargetEntityId = targetId;
                _currentTarget = bossForHit;
                TriggerTargetChanged(_currentTarget);
            }
            bossForHit.CurrentHp = Math.Max(0, bossForHit.CurrentHp - (damage + multiHitDamage));
            bossForHit.TotalDamageReceived += damage + multiHitDamage;
            TriggerTargetChanged(bossForHit);
        }
    }

    private void OnUserInfo(int entityId, string nickname, int serverId, int jobCode, int isSelf)
    {
        _identities[entityId] = (nickname, jobCode);
        TriggerPartyMemberSeen(new PartyMember
        {
            CharacterId = (uint)entityId,
            Nickname    = nickname,
            ServerId    = serverId,
            ServerName  = ServerMap.GetName(serverId),
            JobCode     = jobCode,
            IsSelf      = isSelf == 1,
        });
    }

    private void OnSummon(int actorId, int petId)
    {
        _summons[petId] = actorId;
    }

    private void OnMobSpawn(int mobId, int mobCode, int hp, int isBoss)
    {
        if (isBoss == 0 || hp <= 0) return;
        var name = _skills.GetMobName(mobCode);
        if (name == null)
        {
            if (Core.AppSettings.Instance.AdminMode)
            {
                name = $"[신규보스] 코드_{mobCode}";
                _skills.AddMobAndSave(mobCode, name, true);
                Console.WriteLine($"[AdminMode] Auto-registered new boss: {name} (HP: {hp:N0})");
            }
            else
            {
                return;
            }
        }
        else if (name.StartsWith("M_PD_") || name.Contains("Invisible"))
        {
            return;
        }

        var t = new MobTarget
        {
            EntityId  = mobId,
            Name      = name,
            MaxHp     = hp,
            CurrentHp = hp,
            IsBoss    = true,
        };
        _knownBosses[mobId] = t;

        // Don't latch _currentTarget yet — wait for actual damage to land
        // (multi-boss zones may spawn 2+ bosses near-simultaneously).
        // But if this is the very first boss we've ever seen, surface it so
        // the canvas can at least show its name + full HP.
        if (_currentTarget == null)
        {
            _currentTargetEntityId = mobId;
            _currentTarget = t;
            TriggerTargetChanged(_currentTarget);
        }
    }

    private void OnPartyRoster(System.Collections.Generic.List<PartyMember> members)
    {
        foreach (var m in members) OnPartyMember(m);
    }

    private void OnPartyMember(PartyMember m)
    {
        m.IsPartyMember = true;
        if (m.CharacterId != 0)
        {
            _identities[(int)m.CharacterId] = (m.Nickname, m.JobCode);
        }
        TriggerPartyMemberSeen(m);
    }

    private void OnPartyLeft()
    {
        // Signal that the party has disbanded — downstream clears party flags.
        (_source as IInternalEventRaise)?.RaisePartyLeft();
    }

    /// CP-by-name doesn't carry an entityId. Best we can do is enrich any future
    /// member with the same nickname+server via the existing hint cache, which
    /// PartyStreamParser already maintains internally.
    private void OnPartyCpByName(string nickname, int serverId, int cp)
    {
        TriggerPartyMemberSeen(new PartyMember
        {
            Nickname    = nickname,
            ServerId    = serverId,
            ServerName  = ServerMap.GetName(serverId),
            CombatPower = cp,
        });
    }

    private void OnCombatPower(int entityId, int combatPower)
    {
        // Re-emit as a (sparse) PartyMember with the existing identity if known.
        if (!_identities.TryGetValue(entityId, out var id)) return;
        TriggerPartyMemberSeen(new PartyMember
        {
            CharacterId = (uint)entityId,
            Nickname    = id.Name,
            JobCode     = id.JobCode,
            CombatPower = combatPower,
        });
    }

    private void OnBossHp(int entityId, int currentHp)
    {
        if (!_knownBosses.TryGetValue(entityId, out var t)) return;
        t.CurrentHp = Math.Max(0, currentHp);
        if (entityId == _currentTargetEntityId) TriggerTargetChanged(t);
    }

    // The IPacketSource events are publicly read-only; we route through reflection-free
    // helpers that the source itself exposes (PacketSniffer/PcapReplaySource each
    // expose internal raise methods).
    private void TriggerCombatHit(int actorId, int targetId, string? name, int jobCode, long damage, uint hitFlags, bool isHeal, string? skill, int extraHits, bool isDot, int[]? specs = null)
        => (_source as IInternalEventRaise)?.RaiseCombatHit(
            new CombatHitArgs(actorId, targetId, name ?? "", jobCode, damage, hitFlags, isHeal, skill, extraHits, isDot, specs));

    private void TriggerTargetChanged(MobTarget target)
        => (_source as IInternalEventRaise)?.RaiseTargetChanged(target);

    private void TriggerPartyMemberSeen(PartyMember member)
        => (_source as IInternalEventRaise)?.RaisePartyMemberSeen(member);
}

/// Internal hook that lets ProtocolPipeline re-raise events on its source
/// without exposing the events as writable from outside the source itself.
internal interface IInternalEventRaise
{
    void RaiseCombatHit(CombatHitArgs args);
    void RaiseTargetChanged(MobTarget? target);
    void RaisePartyMemberSeen(PartyMember member);
    void RaisePartyLeft();
    void RaiseDungeonChanged(int dungeonId);
    void RaiseBuffEvent(int entityId, int buffId, int type, uint durationMs, long timestamp);
}
