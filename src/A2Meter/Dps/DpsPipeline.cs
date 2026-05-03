using System;
using System.Collections.Generic;
using System.Windows.Forms;
using A2Meter.Direct2D;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Dps;

/// Glues sniffer events to the meter, then pushes snapshots to the D2D canvas
/// at a fixed cadence (~10 Hz). Used to also push to a WebBridge for an HTML
/// overlay; that path is gone now that the overlay is fully native.
internal sealed class DpsPipeline : IDisposable
{
    private readonly IPacketSource _source;
    private readonly DpsMeter _meter;
    private readonly PartyTracker _party;
    private readonly DpsCanvas _canvas;
    private readonly System.Windows.Forms.Timer _pushTimer;
    private readonly CombatHistory _history = new();

    private const int PushIntervalMs = 100;
    /// Idle window after which an active session ends and gets summarized.
    private const double SessionIdleSeconds = 5.0;

    private MobTarget? _currentTarget;
    private int        _currentTargetId;
    private DateTime   _lastHitUtc      = DateTime.MinValue;
    private long       _peakDpsThisSess;
    private bool       _sessionActive;
    private bool       _viewingHistory;
    private DpsCanvas.SessionSummary? _lastSummary;

    /// Per-actor running peak DPS this session (resets when session ends).
    private readonly Dictionary<int, long> _peakByActor = new();

    public DpsPipeline(
        IPacketSource source,
        DpsMeter meter,
        PartyTracker party,
        DpsCanvas canvas)
    {
        _source = source;
        _meter  = meter;
        _party  = party;
        _canvas = canvas;

        _source.CombatHit       += OnCombatHit;
        _source.TargetChanged   += OnTargetChanged;
        _source.PartyMemberSeen += m => _party.Upsert(m);

        _pushTimer = new System.Windows.Forms.Timer { Interval = PushIntervalMs };
        _pushTimer.Tick += (_, _) => Push();
    }

    public CombatHistory History => _history;
    public void EnterHistoryView() => _viewingHistory = true;
    public void ExitHistoryView()  => _viewingHistory = false;

    public void Start()
    {
        _source.Start();
        _pushTimer.Start();
    }

    public void Stop()
    {
        _pushTimer.Stop();
        _source.Stop();
    }

    public void Reset() => _meter.Reset();

    private void OnCombatHit(CombatHitArgs e)
    {
        // Exit history viewing mode when new combat starts.
        _viewingHistory = false;

        // Deferred reset: previous session ended but data was kept for review.
        // Clear now that a new fight is starting.
        if (!_sessionActive && _lastSummary != null)
        {
            _meter.Reset();
            _peakByActor.Clear();
            _peakDpsThisSess = 0;
            _lastSummary = null;
        }

        _meter.RecordHit(e.ActorId, e.TargetId, e.Name, e.JobCode, e.Damage, e.HitFlags, e.IsHeal, e.Skill, e.ExtraHits, e.IsDot, e.Specs);
        _lastHitUtc = DateTime.UtcNow;
        _sessionActive = true;
    }

    /// Match the original A2Power "single pull = single session" semantics:
    /// every fresh boss spawn finalises the previous session and resets the
    /// meter so the new fight starts at 0. Non-boss target changes are ignored.
    private void OnTargetChanged(MobTarget? t)
    {
        bool isNewBoss = t is { IsBoss: true, MaxHp: > 0 } &&
                         (_currentTarget == null
                          || !_currentTarget.IsBoss
                          || !ReferenceEquals(_currentTarget, t) && _currentTarget.MaxHp != t.MaxHp);

        _meter.SetTarget(t);
        _currentTarget = t;
        _currentTargetId = (t is { IsBoss: true }) ? t.EntityId : 0;

        if (isNewBoss)
        {
            if (_sessionActive)
            {
                var prev = _meter.BuildCurrentSnapshot();
                if (prev.TotalPartyDamage > 0)
                {
                    _lastSummary = BuildSummary(prev);
                    SaveRecord(prev);
                }
            }
            ResetSession();
        }
    }

    private void Push()
    {
        if (_viewingHistory) return;

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

        // End-of-session detection: N seconds since last damage event.
        // Don't reset — keep bars visible so the user can click to inspect.
        // Reset is deferred to OnCombatHit when the next fight starts.
        if (_sessionActive && snap.TotalPartyDamage > 0 &&
            (DateTime.UtcNow - _lastHitUtc).TotalSeconds > SessionIdleSeconds)
        {
            _lastSummary = BuildSummary(snap);
            SaveRecord(snap);
            _meter.Stop();
            _sessionActive = false;
        }

        _canvas.SetData(MapForCanvas(snap.Players, snap.ElapsedSeconds), snap.TotalPartyDamage,
                        FormatTimer(snap.ElapsedSeconds), _currentTarget, _lastSummary);
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
        _history.Save(new CombatRecord
        {
            Timestamp   = DateTime.Now,
            BossName    = _currentTarget?.Name,
            DurationSec = snap.ElapsedSeconds,
            TotalDamage = snap.TotalPartyDamage,
            AverageDps  = snap.ElapsedSeconds > 0 ? (long)(snap.TotalPartyDamage / snap.ElapsedSeconds) : 0,
            PeakDps     = _peakDpsThisSess,
            Snapshot    = snap,
        });
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

            // Only show party members. Skip for history view or when no party data exists.
            if (filterParty && _party.Members.Count > 0 && !_party.Members.ContainsKey((uint)p.EntityId))
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
                        Specs: s.Specs));
                }
                skills = sb;
            }

            int cp = 0;
            if (_party.Members.TryGetValue((uint)p.EntityId, out var pm)) cp = pm.CombatPower;
            long peak = _peakByActor.TryGetValue(p.EntityId, out var pk) ? pk : p.Dps;
            long avg  = elapsedSec > 0 ? (long)(p.TotalDamage / elapsedSec) : p.Dps;

            rows.Add(new DpsCanvas.PlayerRow(
                Name:        p.Name,
                JobIconKey:  JobCodeToKey(p.JobCode),
                Damage:      p.TotalDamage,
                Percent:     p.DamagePercent,
                DpsValue:    p.Dps,
                CritRate:    p.CritRate,
                HealTotal:   p.HealTotal,
                AccentColor: JobAccent(p.JobCode),
                Skills:      skills,
                CombatPower: cp,
                PeakDps:     peak,
                AvgDps:      avg,
                DotDamage:   p.DotDamage));
        }
        return rows;
    }

    private static string FormatTimer(double seconds)
    {
        var s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
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
        _source.Dispose();
    }
}
