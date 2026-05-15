using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2Meter.Dps;

/// Persisted combat record — one per boss kill / session end.
internal sealed class CombatRecord
{
    public DateTime Timestamp { get; set; }
    public string? BossName { get; set; }
    public double DurationSec { get; set; }
    public long TotalDamage { get; set; }
    public long AverageDps { get; set; }
    public long PeakDps { get; set; }
    public DpsSnapshot Snapshot { get; set; } = new();
    public List<TimelineEntry>? Timeline { get; set; }
    public List<HitLogEntry>? HitLog { get; set; }
    public int? DungeonId { get; set; }
    public string? FieldName { get; set; }
}

/// Saves / loads combat records as JSON files under %AppData%\A2Meter\history\.
internal sealed class CombatHistory
{
    private static readonly string HistoryDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "history");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<CombatRecord> _records = new();
    public IReadOnlyList<CombatRecord> Records => _records;

    public CombatHistory()
    {
        LoadAll();
    }

    public void Save(CombatRecord record)
    {
        _records.Insert(0, record);
        TrimOld();
        try
        {
            Directory.CreateDirectory(HistoryDir);
            string fileName = $"{record.Timestamp:yyyyMMdd-HHmmss}.json";
            string path = Path.Combine(HistoryDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOpts));
        }
        catch { /* best effort */ }
    }

    private void LoadAll()
    {
        _records.Clear();
        if (!Directory.Exists(HistoryDir)) return;
        try
        {
            var files = Directory.GetFiles(HistoryDir, "*.json");
            Array.Sort(files);
            Array.Reverse(files); // newest first
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var rec = JsonSerializer.Deserialize<CombatRecord>(json, JsonOpts);
                    if (rec != null) _records.Add(rec);
                }
                catch { /* skip corrupt files */ }
            }
            TrimOld();
        }
        catch { }
    }

    /// Keep at most 100 records.
    private void TrimOld()
    {
        const int MaxRecords = 100;
        while (_records.Count > MaxRecords)
        {
            var oldest = _records[_records.Count - 1];
            _records.RemoveAt(_records.Count - 1);
            try
            {
                string fileName = $"{oldest.Timestamp:yyyyMMdd-HHmmss}.json";
                string path = Path.Combine(HistoryDir, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
