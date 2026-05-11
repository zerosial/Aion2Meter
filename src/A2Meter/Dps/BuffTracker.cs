using System;
using System.Collections.Generic;
using A2Meter.Dps.Protocol;

namespace A2Meter.Dps;

internal sealed class BuffTracker
{
    private sealed class BuffInfo
    {
        public DateTime StartedAt;
        public DateTime ExpiresAt;
        public double AccumulatedSec;
    }

    private readonly SkillDatabase _skills;
    private readonly Dictionary<int, Dictionary<int, BuffInfo>> _buffs = new();

    private const uint MAX_REASONABLE_DURATION_MS = 3_600_000;

    public BuffTracker(SkillDatabase? skills = null)
    {
        _skills = skills ?? SkillDatabase.Shared;
    }

    public void Reset()
    {
        _buffs.Clear();
    }

    public void Start()
    {
    }

    public void OnBuff(int entityId, int buffId, int type, uint durationMs, long timestamp)
    {
        if (durationMs == 0 || durationMs == uint.MaxValue || durationMs > MAX_REASONABLE_DURATION_MS)
            return;

        int skillCode = ResolveSkillCode(buffId);
        if (skillCode < 0) return;

        if (!_buffs.TryGetValue(entityId, out var byBuff))
        {
            byBuff = new Dictionary<int, BuffInfo>();
            _buffs[entityId] = byBuff;
        }

        var now = DateTime.UtcNow;
        double durSec = durationMs / 1000.0;

        if (byBuff.TryGetValue(skillCode, out var existing))
        {
            if (now >= existing.ExpiresAt)
            {
                existing.AccumulatedSec += (existing.ExpiresAt - existing.StartedAt).TotalSeconds;
                existing.StartedAt = now;
            }
            existing.ExpiresAt = now + TimeSpan.FromSeconds(durSec);
        }
        else
        {
            byBuff[skillCode] = new BuffInfo
            {
                StartedAt = now,
                ExpiresAt = now + TimeSpan.FromSeconds(durSec),
                AccumulatedSec = 0.0
            };
        }
    }

    public List<BuffUptime> BuildSnapshot(int entityId, double elapsedSeconds)
    {
        var result = new List<BuffUptime>();
        if (elapsedSeconds <= 0) return result;
        if (!_buffs.TryGetValue(entityId, out var byBuff)) return result;

        var now = DateTime.UtcNow;
        foreach (var (skillCode, info) in byBuff)
        {
            double total = info.AccumulatedSec;
            total = now < info.ExpiresAt
                ? total + (now - info.StartedAt).TotalSeconds
                : total + (info.ExpiresAt - info.StartedAt).TotalSeconds;

            if (total <= 0) continue;

            double uptime = Math.Min(1.0, total / elapsedSeconds);
            string name = _skills.GetSkillName(skillCode) ?? $"버프#{skillCode}";

            bool merged = false;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Name == name)
                {
                    if (uptime > result[i].Uptime)
                        result[i] = new BuffUptime(name, skillCode, uptime);
                    merged = true;
                    break;
                }
            }
            if (!merged)
                result.Add(new BuffUptime(name, skillCode, uptime));
        }

        result.Sort((a, b) => b.Uptime.CompareTo(a.Uptime));
        return result;
    }

    public Dictionary<int, List<BuffUptime>> BuildAllSnapshots(double elapsedSeconds)
    {
        var dict = new Dictionary<int, List<BuffUptime>>();
        foreach (var eid in _buffs.Keys)
        {
            var list = BuildSnapshot(eid, elapsedSeconds);
            if (list.Count > 0) dict[eid] = list;
        }
        return dict;
    }

    private int ResolveSkillCode(int buffId)
    {
        if (_skills.GetSkillName(buffId) != null) return buffId;

        int alt = (buffId >= 100_000_000 && buffId <= 999_999_999) ? buffId / 10 : buffId;
        if (alt != buffId && _skills.GetSkillName(alt) != null) return alt;

        int baseCode = alt / 10000 * 10000;
        if (baseCode != alt && _skills.GetSkillName(baseCode) != null) return baseCode;

        if (_skills.IsKnownBuffCode(buffId)) return buffId;

        return -1;
    }
}
