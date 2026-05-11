using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace A2Meter.Api;

internal sealed class SkillIconCache
{
    private const string OldBase = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";
    private const string NewBase = "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@v1.0.0/Assets/Icon/Skill/";

    private static readonly Lazy<SkillIconCache> _instance = new(() => new SkillIconCache());
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "skill_icons");

    private readonly Dictionary<string, string> _urlMap = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Image> _images = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    public static SkillIconCache Instance => _instance.Value;

    public event Action? IconReady;

    private SkillIconCache()
    {
        Directory.CreateDirectory(CacheDir);
        LoadCatalog();
    }

    public Image? Get(string skillName)
    {
        if (_images.TryGetValue(skillName, out var img)) return img;

        var diskPath = DiskPath(skillName);
        if (diskPath != null && File.Exists(diskPath))
        {
            try
            {
                using var fs = File.OpenRead(diskPath);
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                img = Image.FromStream(ms);
                _images[skillName] = img;
                return img;
            }
            catch { }
        }

        if (_urlMap.TryGetValue(skillName, out var url) && _pending.TryAdd(skillName, 0))
            _ = DownloadAsync(skillName, url);

        return null;
    }

    private async Task DownloadAsync(string skillName, string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            var diskPath = DiskPath(skillName);
            if (diskPath != null)
                await File.WriteAllBytesAsync(diskPath, bytes).ConfigureAwait(false);

            var img = Image.FromStream(new MemoryStream(bytes));
            _images[skillName] = img;
            IconReady?.Invoke();
        }
        catch { }
        finally
        {
            _pending.TryRemove(skillName, out _);
        }
    }

    private string? DiskPath(string skillName)
    {
        if (_urlMap.TryGetValue(skillName, out var url))
        {
            var span = url.AsSpan();
            int lastSlash = span.LastIndexOf('/');
            if (lastSlash >= 0)
                return Path.Combine(CacheDir, span[(lastSlash + 1)..].ToString());
        }
        return null;
    }

    private void LoadCatalog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "known_skills_catalog.json");
            if (!File.Exists(path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var category in doc.RootElement.EnumerateObject())
            {
                foreach (var skill in category.Value.EnumerateArray())
                {
                    var name = skill.GetProperty("name").GetString();
                    var iconUrl = skill.GetProperty("iconUrl").GetString();
                    if (name != null && iconUrl != null)
                        _urlMap.TryAdd(name, iconUrl.Replace(OldBase, NewBase));
                }
            }
        }
        catch { }
    }
}
