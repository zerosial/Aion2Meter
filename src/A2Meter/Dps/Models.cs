using System.Collections.Generic;

namespace A2Meter.Dps;

/// Wire shape sent to the JS UI as { type:"dps-update", data:<this> }.
/// Matches the original A2Viewer/A2Power schema so existing web assets render unchanged.
internal sealed class DpsSnapshot
{
    public double ElapsedSeconds { get; set; }
    public double WallElapsedSeconds { get; set; }
    public long   TotalPartyDamage { get; set; }
    public MobTarget? Target { get; set; }
    public List<ActorDps> Players { get; set; } = new();
}

internal sealed class ActorDps
{
    public int    EntityId { get; set; }
    public string Name { get; set; } = "";
    public int    JobCode { get; set; } = -1;
    public int    ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public int    CombatScore { get; set; }
    public int    CombatPower { get; set; }
    public long   TotalDamage { get; set; }
    public long   Dps { get; set; }
    public long   PartyDps { get; set; }
    public long   WallDps { get; set; }
    public double DamagePercent { get; set; }
    public double BossHpPercent { get; set; }
    public double CritRate { get; set; }
    public long   HealTotal { get; set; }
    public bool   IsUploader { get; set; }
    public long   Hits { get; set; }
    public long   DotDamage { get; set; }
    public List<SkillDps>? TopSkills { get; set; }
    public Dictionary<string, int>? SkillLevels { get; set; }
}

internal sealed class SkillDps
{
    public string Name { get; set; } = "";
    public long   Total { get; set; }
    public long   Hits { get; set; }
    public long   MaxHit { get; set; }
    public double CritRate { get; set; }
    public double BackRate { get; set; }      // 후방
    public double StrongRate { get; set; }    // 강타
    public double PerfectRate { get; set; }   // 완벽
    public double MultiHitRate { get; set; }  // 다단
    public double DodgeRate { get; set; }     // 회피
    public double BlockRate { get; set; }     // 막기
    public int[]? Specs { get; set; }         // 특화 tiers (1-based, sorted ascending)
}

internal sealed class MobTarget
{
    public int    EntityId { get; set; }
    public string Name { get; set; } = "";
    public long   MaxHp { get; set; }
    public long   CurrentHp { get; set; }
    public long   TotalDamageReceived { get; set; }
    public bool   IsBoss { get; set; }
}

/// Party roster member — schema matches the original A2Viewer.Packet.PartyMember
/// so the protocol parser can populate it directly.
internal sealed class PartyMember
{
    public uint   CharacterId { get; set; }
    public int    ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int    JobCode { get; set; }
    public string JobName { get; set; } = "";
    public int    Level { get; set; }
    public int    CombatPower { get; set; }
    /// True when this member is the local player (from UserInfo isSelf=1).
    public bool   IsSelf { get; set; }
    /// True when confirmed via actual party packet (PartyList/PartyUpdate/PartyAccept),
    /// not just seen nearby via UserInfo.
    public bool   IsPartyMember { get; set; }
}
