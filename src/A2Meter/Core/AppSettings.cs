using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace A2Meter.Core;

/// Persisted application settings. Singleton. Backend-only fields
/// (skill catalog, uploader id) will be added when those modules land.
internal sealed class AppSettings
{
    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static AppSettings? _instance;
    public static AppSettings Instance => _instance ??= Load();

    // ── overlay behavior ──
    public bool OverlayOnlyWhenAion { get; set; } = true;
    public bool FrameGenerationCompatMode { get; set; }
    public bool PerformanceMode { get; set; }
    public string GpuMode { get; set; } = "on";   // "on" | "off"
    public bool GpuModeUserOverride { get; set; }
    public bool PromiscuousMode { get; set; }

    // ── visual ──
    public int Opacity { get; set; } = 90;       // 0..100
    public int TextScale { get; set; } = 100;
    public int FontScale { get; set; } = 100;
    public int RowHeight { get; set; } = 90;
    public string UiStyle { get; set; } = "modern";
    public string? ThemeJson { get; set; }

    // ── shortcuts ──
    public ShortcutSettings Shortcuts { get; set; } = new();

    // ── DPS panel preferences ──
    public bool KeepPartyOnRefresh { get; set; } = true;
    public bool KeepSelfOnRefresh { get; set; } = true;
    public bool AutoTabSwitch { get; set; } = true;
    public string DpsPercentMode { get; set; } = "party";
    public string ScoreDisplay { get; set; } = "both";
    public string ScoreFormat { get; set; } = "full";
    public string DpsTimeMode { get; set; } = "wallclock";

    // ── secondary windows ──
    public int DetailPanelWidth { get; set; } = 900;
    public int DetailPanelHeight { get; set; } = 400;
    public int CombatRecordsPanelWidth { get; set; } = 620;
    public int CombatRecordsPanelHeight { get; set; } = 520;

    // ── consent / identity ──
    public string? ConsentVersion { get; set; }
    public DateTime? ConsentedAt { get; set; }

    // ── per-machine state stored in a sibling file ──
    [JsonIgnore]
    public WindowState WindowState { get; set; } = new();

    // ── debounced save ──
    private static readonly object _saveLock = new();
    private static System.Threading.Timer? _debounceTimer;
    private const int DebounceMs = 400;

    public void Save()
    {
        SaveJson("app_settings.json", this);
        SaveJson("window_state.json", WindowState);
    }

    /// Coalesces rapid Save() calls within ~400ms into one disk write.
    public void SaveDebounced()
    {
        lock (_saveLock)
        {
            _debounceTimer ??= new System.Threading.Timer(_ =>
            {
                try { Save(); } catch { /* swallow — best-effort persistence */ }
            }, null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private static AppSettings Load()
    {
        Directory.CreateDirectory(CacheDir);

        var settings = LoadJson<AppSettings>("app_settings.json") ?? new AppSettings();

        var ws = LoadJson<WindowState>("window_state.json");
        if (ws != null)
        {
            // Sanitize obviously bogus coordinates from legacy/multimonitor configs.
            if (ws.X <= -9000 || ws.Y <= -9000) { ws.X = -1; ws.Y = 20; }
            if (ws.X == -1)
                ws.X = (Screen.PrimaryScreen?.WorkingArea.Width ?? 1920) - Math.Max(1, ws.Width) - 20;
            settings.WindowState = ws;
        }
        else
        {
            // Default position: top-right of primary screen. Size needs to fit
            // the focused row (30) + focus stats line (14) + 6 skill bars (~108)
            // + 3 more rows (~100) plus header (28) + boss bar (16) + paddings.
            settings.WindowState = new WindowState
            {
                X = (Screen.PrimaryScreen?.WorkingArea.Width ?? 1920) - 480,
                Y = 80,
                Width = 460,
                Height = 500,
            };
        }

        // GPU mode autocorrect: if the user never explicitly chose "off", revert.
        if (!settings.GpuModeUserOverride
            && string.Equals(settings.GpuMode, "off", StringComparison.OrdinalIgnoreCase))
        {
            settings.GpuMode = "on";
        }

        settings.Shortcuts ??= new ShortcutSettings();
        return settings;
    }

    private static T? LoadJson<T>(string filename) where T : class
    {
        foreach (var path in EnumerateReadCandidates(Path.Combine(CacheDir, filename)))
        {
            try
            {
                if (!File.Exists(path)) continue;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
            }
            catch { /* try next candidate */ }
        }
        return null;
    }

    private static void SaveJson<T>(string filename, T data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var content = JsonSerializer.Serialize(data, JsonOpts);
            WriteTextAtomically(Path.Combine(CacheDir, filename), content);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[settings] save error: " + ex.Message);
        }
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateReadCandidates(string path)
    {
        yield return path;
        yield return path + ".bak";
    }

    /// Atomic write with a .bak fallback so a crash mid-write never loses settings.
    private static void WriteTextAtomically(string path, string content)
    {
        var tmp = path + ".tmp";
        var bak = path + ".bak";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
        else
        {
            File.Move(tmp, path, overwrite: true);
            try { File.Copy(path, bak, overwrite: true); } catch { }
        }
    }
}
