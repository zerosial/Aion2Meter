using System;
using System.Collections.Generic;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps.Protocol;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Dps;

/// Glues sniffer events to the meter, then pushes snapshots to the D2D canvas
/// at a fixed cadence (~10 Hz). Supports both WinForms (DpsCanvas) and headless
/// (ImGui polling via DataPushed event) modes.
internal sealed class DpsPipeline : IDisposable
{
    private readonly IPacketSource _source;
    private readonly DpsMeter _meter;
    private readonly PartyTracker _party;
    private readonly DpsCanvas? _canvas;
    private readonly System.Threading.Timer _pushTimer;
    private readonly CombatHistory _history = new();

    private const int PushIntervalMs = 100;
    /// Idle window after which an active session ends (matches original: 3s).
    private const double SessionIdleSeconds = 3.0;

    private MobTarget? _currentTarget;
    private int        _currentTargetId;
    private int        _sessionBossId;   // boss entityId when session ended (for new-boss reset)
    private DateTime   _lastHitUtc      = DateTime.MinValue;
    private long       _peakDpsThisSess;
    private bool       _sessionActive;
    private bool       _viewingHistory;
    private bool       _selfDetectedOnce;
    private DpsCanvas.SessionSummary? _lastSummary;
    private int        _currentDungeonId;

    // ── Countdown timer mode ──
    private int        _countdownSec;       // 0 = off, 30/60/90/... = active limit
    private DateTime   _countdownStart;     // combat start time for countdown
    private bool       _countdownExpired;   // true = timer ended, stop measuring

    // ── Network / performance monitors ──
    public PingMonitor Ping { get; } = new();

    // Cached last-frame data so we keep showing bars after session ends.
    private IReadOnlyList<DpsCanvas.PlayerRow>? _lastRows;
    private long   _lastTotal;
    private string _lastTimer = "";

    /// Per-actor running peak DPS this session (resets when session ends).
    private readonly Dictionary<int, long> _peakByActor = new();

    /// Fired at ~10 Hz with the latest snapshot data (for ImGui headless mode).
    public event Action<IReadOnlyList<DpsCanvas.PlayerRow>, long, string, MobTarget?, DpsCanvas.SessionSummary?>? DataPushed;

    public DpsPipeline(
        IPacketSource source,
        DpsMeter meter,
        PartyTracker party,
        DpsCanvas? canvas = null)
    {
        _source = source;
        _meter  = meter;
        _party  = party;
        _canvas = canvas;

        _source.CombatHit       += OnCombatHit;
        _source.TargetChanged   += OnTargetChanged;
        _source.PartyMemberSeen += OnPartyMemberSeen;
        _source.PartyLeft       += () => _party.ClearPartyFlags();
        _source.SegmentReceived += seg => Ping.Feed(seg);
        _source.DungeonDetected += (dId, stg) => _currentDungeonId = dId;

        _pushTimer = new System.Threading.Timer(_ => Push(), null,
            System.Threading.Timeout.Infinite, PushIntervalMs);
    }

    public CombatHistory History => _history;
    public void EnterHistoryView() => _viewingHistory = true;
    public void ExitHistoryView()  => _viewingHistory = false;

    /// Current countdown limit (0 = off).
    public int CountdownSeconds => _countdownSec;
    /// Whether the countdown has expired and DPS measurement is frozen.
    public bool CountdownExpired => _countdownExpired;

    /// Cycle countdown: off → 30 → 60 → 90 → 120 → 150 → 180 → off
    public void CycleCountdown()
    {
        if (_countdownSec >= 180) _countdownSec = 0;
        else _countdownSec += 30;
        _countdownExpired = false;
    }

    public void Start()
    {
        _source.Start();
        _pushTimer.Change(0, PushIntervalMs);
    }

    public void Stop()
    {
        _pushTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _source.Stop();
    }

    public void Reset()
    {
        _meter.Reset();
        _countdownExpired = false;
        _lastSummary = null;
        _lastRows = null;
        _sessionActive = false;
        _peakByActor.Clear();
        _peakDpsThisSess = 0;
    }

    private void OnPartyMemberSeen(PartyMember m)
    {
        _party.Upsert(m);

        // Trigger async skill level fetch from Plaync API (self + party only).
        if (!string.IsNullOrEmpty(m.Nickname) && m.ServerId > 0 && (m.IsSelf || m.IsPartyMember))
            Api.SkillLevelCache.Instance.EnsureLoaded(m.Nickname, m.ServerId);

        // Detection triggers immediate canvas refresh to show the new member row.
        if (_canvas != null)
            _canvas.Invalidate();

        // Zone transition: when self is re-detected, freeze session (stop timer)
        // but keep data visible until next combat or manual reset.
        if (m.IsSelf)
        {
            if (_selfDetectedOnce && _sessionActive)
            {
                _sessionActive = false;
                _currentTarget = null;
                _currentTargetId = 0;
            }
            _selfDetectedOnce = true;
        }
    }

    private void OnCombatHit(CombatHitArgs e)
    {
        // Self/party filter: if self is not known yet, ignore all combat.
        if (_party.SelfEntityId == null)
            return;
        // Self: always allowed.
        // Others: only allowed when a boss target is active (implies dungeon/instance
        // where only party members are present). Blocks town/open-world noise.
        if (e.ActorId != _party.SelfEntityId)
        {
            if (_currentTarget is not { IsBoss: true, MaxHp: > 0 })
                return;
        }

        // Countdown expired → ignore all hits until manual reset.
        if (_countdownExpired)
            return;

        // Session frozen (boss killed / idle ended):
        // Allow reset only when combat starts against a NEW boss.
        if (!_sessionActive && _lastSummary != null)
        {
            // New boss target detected → auto-reset for the new fight.
            if (_currentTargetId != 0 && _currentTargetId != _sessionBossId)
            {
                _meter.Reset();
                _peakByActor.Clear();
                _peakDpsThisSess = 0;
                _lastSummary = null;
                _lastRows = null;
                _sessionBossId = 0;
            }
            else
            {
                return; // Same boss or no boss — stay frozen.
            }
        }

        // First hit of a new session: purge non-party members and start countdown.
        if (!_sessionActive)
        {
            _party.PurgeNonParty();
            if (_countdownSec > 0)
                _countdownStart = DateTime.UtcNow;
        }

        _meter.RecordHit(e.ActorId, e.TargetId, e.Name, e.JobCode, e.Damage, e.HitFlags, e.IsHeal, e.Skill, e.ExtraHits, e.IsDot, e.Specs);
        _lastHitUtc = DateTime.UtcNow;
        _sessionActive = true;
    }

    /// Update target tracking. No automatic reset — data persists until manual Reset().
    private void OnTargetChanged(MobTarget? t)
    {
        _meter.SetTarget(t);
        _currentTarget = t;
        _currentTargetId = (t is { IsBoss: true }) ? t.EntityId : 0;
    }

    private void Push()
    {
        if (_viewingHistory) return;

        // Sync countdown state and perf monitors to canvas.
        if (_canvas != null)
        {
            _canvas.CountdownExpired = _countdownExpired;
            _canvas.PingMs = Ping.CurrentPingMs;
        }

        // When a boss is active, scope the canvas to that boss's damage only —
        // matches the original A2Power "기록 조회 중" view. Otherwise show the
        // party-wide roll-up.
        var snap = _currentTargetId != 0
            ? _meter.BuildTargetSnapshot(_currentTargetId)
            : _meter.BuildCurrentSnapshot();

        // Track per-session peak as the highest single-actor DPS we've observed.
        if (_sessionActive && snap.Players.Count > 0)
        {
            long top = snap.Players[0].Dps;
            if (top > _peakDpsThisSess) _peakDpsThisSess = top;
        }
        // Per-actor peak.
        foreach (var p in snap.Players)
        {
            if (!_peakByActor.TryGetValue(p.EntityId, out var cur) || p.Dps > cur)
                _peakByActor[p.EntityId] = p.Dps;
        }

        // Countdown timer expiry: freeze measurement when time is up.
        if (_sessionActive && _countdownSec > 0 && !_countdownExpired)
        {
            double elapsed = (DateTime.UtcNow - _countdownStart).TotalSeconds;
            if (elapsed >= _countdownSec)
            {
                _countdownExpired = true;
                _meter.Stop();
                _sessionActive = false;

                // Build final snapshot at countdown limit.
                var finalSnap = _currentTargetId != 0
                    ? _meter.BuildTargetSnapshot(_currentTargetId)
                    : _meter.BuildCurrentSnapshot();
                _lastRows = MapForCanvas(finalSnap.Players, _countdownSec);
                _lastTotal = finalSnap.TotalPartyDamage;
                _lastTimer = FormatTimer(_countdownSec);
                _lastSummary = BuildSummary(finalSnap);
                SaveRecord(finalSnap);
            }
        }

        // When countdown expired, show frozen data.
        if (_countdownExpired && _lastRows != null)
        {
            _canvas?.SetData(_lastRows, _lastTotal, _lastTimer, _currentTarget, _lastSummary);
            DataPushed?.Invoke(_lastRows, _lastTotal, _lastTimer, _currentTarget, _lastSummary);
            return;
        }

        // End-of-session detection:
        // - Boss killed (HP=0): stop timer immediately, freeze bars.
        // - 3s idle + boss NOT alive: stop timer, freeze bars.
        // - Boss alive: timer keeps running indefinitely.
        // After session ends, bars remain until manual Reset().
        bool bossKilled = _sessionActive && _currentTarget is { IsBoss: true, MaxHp: > 0, CurrentHp: <= 0 };
        bool isDummy = _currentTarget != null && IsDummy(_currentTarget.Name);
        bool bossAlive = _currentTarget is { IsBoss: true, MaxHp: > 0, CurrentHp: > 0 } && !isDummy;
        bool idleTimeout = _sessionActive && snap.TotalPartyDamage > 0 &&
            !bossAlive &&
            (DateTime.UtcNow - _lastHitUtc).TotalSeconds > SessionIdleSeconds;

        if (bossKilled || idleTimeout)
        {
            _lastSummary = BuildSummary(snap);
            SaveRecord(snap);
            _meter.Stop();
            _sessionActive = false;
            _sessionBossId = _currentTargetId;

            // Cache final frame — keep showing bars after session ends.
            _lastRows = MapForCanvas(snap.Players, snap.ElapsedSeconds);
            _lastTotal = snap.TotalPartyDamage;
            _lastTimer = FormatTimer(snap.ElapsedSeconds);
        }

        // After session ends, keep displaying the last frame instead of empty.
        if (!_sessionActive && _lastRows != null)
        {
            _canvas?.SetData(_lastRows, _lastTotal, _lastTimer, _currentTarget, _lastSummary);
            DataPushed?.Invoke(_lastRows, _lastTotal, _lastTimer, _currentTarget, _lastSummary);
            return;
        }

        // When countdown active, show remaining time instead of elapsed.
        var rows = MapForCanvas(snap.Players, snap.ElapsedSeconds);
        string timer;
        if (_countdownSec > 0 && _sessionActive)
        {
            double remaining = Math.Max(0, _countdownSec - (DateTime.UtcNow - _countdownStart).TotalSeconds);
            timer = FormatTimer(remaining);
        }
        else
        {
            timer = FormatTimer(snap.ElapsedSeconds);
        }
        _canvas?.SetData(rows, snap.TotalPartyDamage, timer, _currentTarget, _lastSummary);
        DataPushed?.Invoke(rows, snap.TotalPartyDamage, timer, _currentTarget, _lastSummary);
    }

    private DpsCanvas.SessionSummary BuildSummary(DpsSnapshot snap)
    {
        var top = snap.Players.Count > 0 ? snap.Players[0] : null;
        return new DpsCanvas.SessionSummary(
            DurationSec:    snap.ElapsedSeconds,
            TotalDamage:    snap.TotalPartyDamage,
            AverageDps:     snap.ElapsedSeconds > 0 ? (long)(snap.TotalPartyDamage / snap.ElapsedSeconds) : 0L,
            PeakDps:        _peakDpsThisSess,
            TopActorName:   top?.Name ?? "",
            TopActorDamage: top?.TotalDamage ?? 0,
            BossName:       _currentTarget?.Name);
    }

    private void SaveRecord(DpsSnapshot snap)
    {
        if (snap.TotalPartyDamage <= 0) return;

        // Enrich snapshot players with CP/Score, skill levels, and server info before saving.
        foreach (var p in snap.Players)
        {
            string cleanName = StripServerSuffix(p.Name);
            int sid = p.ServerId;
            string sname = p.ServerName;
            int cp = p.CombatPower;
            int score = p.CombatScore;

            foreach (var pm in _party.Members.Values)
            {
                if (string.Equals(StripServerSuffix(pm.Nickname), cleanName, StringComparison.Ordinal))
                {
                    if (cp == 0 && pm.CombatPower > 0) cp = pm.CombatPower;
                    if (pm.ServerId > 0 && sid == 0) { sid = pm.ServerId; sname = pm.ServerName; }
                    break;
                }
            }
            if (string.IsNullOrEmpty(sname) && sid > 0)
                sname = Protocol.ServerMap.GetName(sid);

            var apiData = Api.SkillLevelCache.Instance.Get(cleanName, sid);
            if (apiData != null)
            {
                if (cp == 0 && apiData.CombatPower > 0) cp = apiData.CombatPower;
                if (score == 0 && apiData.CombatScore > 0) score = apiData.CombatScore;
                if (apiData.SkillLevels is { Count: > 0 })
                    p.SkillLevels = apiData.SkillLevels;
            }

            p.CombatPower = cp;
            p.CombatScore = score;
            p.ServerId = sid;
            p.ServerName = sname;

            // Display name with server suffix for web upload.
            if (!string.IsNullOrEmpty(sname) && !p.Name.Contains('['))
                p.Name = $"{p.Name}[{sname}]";
        }

        string dungeonName = _currentDungeonId > 0 ? SkillDatabase.Shared.GetDungeonName(_currentDungeonId) : "필드";
        var record = new CombatRecord
        {
            Timestamp   = DateTime.Now,
            BossName    = _currentTarget?.Name,
            DurationSec = snap.ElapsedSeconds,
            TotalDamage = snap.TotalPartyDamage,
            AverageDps  = snap.ElapsedSeconds > 0 ? (long)(snap.TotalPartyDamage / snap.ElapsedSeconds) : 0,
            PeakDps     = _peakDpsThisSess,
            Snapshot    = snap,
            FieldName   = dungeonName,
        };

        _history.Save(record);

        // Upload combat stats to the Node.js backend (boss combat only)
        // Skip field mobs, dummies, and unnamed targets.
        bool isBossCombat = _currentTarget is { IsBoss: true } && !string.IsNullOrEmpty(record.BossName)
                            && !IsDummy(record.BossName);
        if (isBossCombat)
        {
            try
            {
                string selfName = "";
                string selfServer = "";
                foreach (var m in _party.Members.Values)
                {
                    if (m.IsSelf)
                    {
                        selfName = m.Nickname;
                        selfServer = m.ServerName;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(selfName))
                {
                    foreach (var p in snap.Players)
                    {
                        if (p.IsUploader)
                        {
                            selfName = StripServerSuffix(p.Name);
                            selfServer = p.ServerName;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selfName) && !string.IsNullOrEmpty(selfServer))
                {
                    // Safe fire-and-forget: Task.Run prevents async void crash propagation
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await Api.StatsUploader.UploadRecordAsync(record, selfName, selfServer);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[DpsPipeline] Upload background error: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DpsPipeline] Upload setup error: {ex.Message}");
            }
        }
    }

    private void ResetSession()
    {
        _meter.Reset();
        _peakDpsThisSess = 0;
        _peakByActor.Clear();
        _sessionActive = false;
    }

    /// Public entry to map a stored snapshot for history display.
    public IReadOnlyList<DpsCanvas.PlayerRow> MapSnapshotForCanvas(DpsSnapshot snap)
        => MapForCanvas(snap.Players, snap.ElapsedSeconds, filterParty: false);

    private IReadOnlyList<DpsCanvas.PlayerRow> MapForCanvas(IReadOnlyList<ActorDps> players, double elapsedSec, bool filterParty = true)
    {
        var rows = new List<DpsCanvas.PlayerRow>(players.Count);
        foreach (var p in players)
        {
            // Skip unidentified actors (mobs/targets that received damage but never
            // had a UserInfo packet — they leak into the meter because we record by
            // entityId regardless of role).
            if (string.IsNullOrEmpty(p.Name) || p.Name.StartsWith('#') || p.JobCode < 0)
                continue;

            // Filter out non-self actors when no boss context exists.
            if (filterParty && _currentTarget is not { IsBoss: true, MaxHp: > 0 }
                && p.EntityId != (_party.SelfEntityId ?? -1))
                continue;

            IReadOnlyList<DpsCanvas.SkillBar>? skills = null;
            if (p.TopSkills is { Count: > 0 } && p.TotalDamage > 0)
            {
                var sb = new List<DpsCanvas.SkillBar>(p.TopSkills.Count);
                foreach (var s in p.TopSkills)
                {
                    if (s.Total <= 0) continue;
                    sb.Add(new DpsCanvas.SkillBar(
                        Name: s.Name,
                        Total: s.Total,
                        Hits: s.Hits,
                        CritRate: s.CritRate,
                        PercentOfActor: (double)s.Total / p.TotalDamage,
                        BackRate: s.BackRate,
                        StrongRate: s.StrongRate,
                        PerfectRate: s.PerfectRate,
                        MultiHitRate: s.MultiHitRate,
                        DodgeRate: s.DodgeRate,
                        BlockRate: s.BlockRate,
                        MaxHit: s.MaxHit,
                        Specs: s.Specs));
                }
                skills = sb;
            }

            int cp = p.CombatPower;
            int score = p.CombatScore;
            int sid = p.ServerId;
            string sname = p.ServerName;
            string cleanName = StripServerSuffix(p.Name);
            // PartyTracker is keyed by CharacterId, not EntityId — look up by nickname.
            foreach (var pm in _party.Members.Values)
            {
                if (string.Equals(StripServerSuffix(pm.Nickname), cleanName, StringComparison.Ordinal))
                {
                    if (cp == 0 && pm.CombatPower > 0) cp = pm.CombatPower;
                    if (pm.ServerId > 0 && sid == 0) { sid = pm.ServerId; sname = pm.ServerName; }
                    break;
                }
            }
            if (string.IsNullOrEmpty(sname) && sid > 0)
                sname = ServerMap.GetName(sid);

            // Enrich CP/Score from API cache if packet-derived values are 0.
            var apiData = Api.SkillLevelCache.Instance.Get(cleanName, sid);
            if (apiData != null)
            {
                if (cp == 0 && apiData.CombatPower > 0) cp = apiData.CombatPower;
                if (score == 0 && apiData.CombatScore > 0) score = apiData.CombatScore;
            }
            long peak = _peakByActor.TryGetValue(p.EntityId, out var pk) ? pk : p.Dps;
            long avg  = elapsedSec > 0 ? (long)(p.TotalDamage / elapsedSec) : p.Dps;

            // Contribution %: "boss" mode uses boss MaxHp as denominator, "party" uses total party damage.
            double pct = p.DamagePercent;
            if (string.Equals(Core.AppSettings.Instance.DpsPercentMode, "boss", StringComparison.OrdinalIgnoreCase)
                && _currentTarget is { IsBoss: true, MaxHp: > 0 })
            {
                pct = (double)p.TotalDamage / _currentTarget.MaxHp;
            }

            string displayName = !string.IsNullOrEmpty(sname) && !p.Name.Contains('[') ? $"{p.Name}[{sname}]" : p.Name;
            rows.Add(new DpsCanvas.PlayerRow(
                Name:        displayName,
                JobIconKey:  JobCodeToKey(p.JobCode),
                Damage:      p.TotalDamage,
                Percent:     pct,
                DpsValue:    p.Dps,
                CritRate:    p.CritRate,
                HealTotal:   p.HealTotal,
                AccentColor: JobAccent(p.JobCode),
                Skills:      skills,
                CombatPower: cp,
                CombatScore: score,
                PeakDps:     peak,
                AvgDps:      avg,
                DotDamage:   p.DotDamage,
                ServerId:    sid,
                ServerName:  sname,
                SkillLevels: p.SkillLevels));
        }

        // Append detected party members who have no combat data yet as placeholder rows.
        // existingNames uses clean nicknames (without [서버명]) to avoid mismatch.
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) existingNames.Add(StripServerSuffix(r.Name));

        foreach (var pm in _party.Members.Values)
        {
            if (string.IsNullOrEmpty(pm.Nickname)) continue;
            string cleanNick = StripServerSuffix(pm.Nickname);
            if (existingNames.Contains(cleanNick)) continue;
            if (!pm.IsSelf && !pm.IsPartyMember) continue;
            existingNames.Add(cleanNick);

            int pmCp = pm.CombatPower;
            int pmScore = 0;
            int pmSid = pm.ServerId;
            string pmSname = pm.ServerName;
            if (string.IsNullOrEmpty(pmSname) && pmSid > 0)
                pmSname = ServerMap.GetName(pmSid);

            var pmApi = Api.SkillLevelCache.Instance.Get(pm.Nickname, pmSid);
            if (pmApi != null)
            {
                if (pmCp == 0 && pmApi.CombatPower > 0) pmCp = pmApi.CombatPower;
                if (pmScore == 0 && pmApi.CombatScore > 0) pmScore = pmApi.CombatScore;
            }

            string pmDisplayName = !string.IsNullOrEmpty(pmSname) && !pm.Nickname.Contains('[') ? $"{pm.Nickname}[{pmSname}]" : pm.Nickname;
            rows.Add(new DpsCanvas.PlayerRow(
                Name:        pmDisplayName,
                JobIconKey:  JobCodeToKey(pm.JobCode),
                Damage:      0,
                Percent:     0,
                DpsValue:    0,
                CritRate:    0,
                HealTotal:   0,
                AccentColor: JobAccent(pm.JobCode),
                CombatPower: pmCp,
                CombatScore: pmScore,
                ServerId:    pmSid,
                ServerName:  pmSname));
        }

        return rows;
    }

    private static bool IsDummy(string? name)
        => name != null && (name.Contains("허수아비") || name.Contains("샌드백"));

    private static string FormatTimer(double seconds)
    {
        var s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    private static string StripServerSuffix(string name)
    {
        int idx = name.IndexOf('[');
        return idx > 0 ? name[..idx] : name;
    }

    private static string JobCodeToKey(int gameCode) => JobMapping.GameToJobName(gameCode);

    /// Color palette indexed by UI archetype (0..7 from JobMapping.GameToUi).
    /// Matches the original A2Viewer web overlay palette.
    private static readonly D2DColor[] UiAccents = new[]
    {
        new D2DColor(0.525f, 0.867f, 0.953f, 1f), // 0 검성   #86DDF3
        new D2DColor(0.384f, 0.694f, 0.561f, 1f), // 1 궁성   #62B18F
        new D2DColor(0.718f, 0.549f, 0.949f, 1f), // 2 마도성  #B78CF2
        new D2DColor(0.643f, 0.906f, 0.608f, 1f), // 3 살성   #A4E79B
        new D2DColor(0.490f, 0.627f, 0.976f, 1f), // 4 수호성  #7DA0F9
        new D2DColor(0.812f, 0.420f, 0.816f, 1f), // 5 정령성  #CF6BD0
        new D2DColor(0.906f, 0.812f, 0.490f, 1f), // 6 치유성  #E7CF7D
        new D2DColor(0.894f, 0.647f, 0.357f, 1f), // 7 호법성  #E4A55B
    };

    private static D2DColor JobAccent(int gameCode)
    {
        int ui = JobMapping.GameToUiIndex(gameCode);
        if (ui < 0 || ui >= UiAccents.Length) return new D2DColor(0.70f, 0.70f, 0.70f, 1f);
        return UiAccents[ui];
    }

    public void Dispose()
    {
        _pushTimer.Dispose();
        _source?.Dispose();
    }
}
