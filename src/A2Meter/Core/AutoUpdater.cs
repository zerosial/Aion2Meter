using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace A2Meter.Core;

/// Checks GitHub releases for a newer version. Download + replace is
/// triggered only after the user confirms via the toast.
internal static class AutoUpdater
{
    private const string RepoOwner = "a2meter";
    private const string RepoName = "Aion2Meter";
    private const string AssetName = "A2Meter.exe";

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "A2Meter-AutoUpdater" } },
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// Check only — returns (remoteVersion, downloadUrl, releaseNotes) or null.
    public static async Task<(Version Version, string Url, string Notes)?> CheckAsync(Action<string>? log = null)
    {
        try
        {
            log?.Invoke("[updater] checking for updates...");
            var release = await GetLatestReleaseAsync();
            if (release == null) return null;

            var remoteVer = ParseTag(release.TagName);
            if (remoteVer == null) return null;

            if (remoteVer <= CurrentVersion)
            {
                log?.Invoke($"[updater] up to date ({CurrentVersion})");
                return null;
            }

            string? downloadUrl = null;
            if (release.Assets != null)
                foreach (var a in release.Assets)
                    if (string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase))
                    { downloadUrl = a.BrowserDownloadUrl; break; }

            if (downloadUrl == null) { log?.Invoke("[updater] asset not found"); return null; }

            log?.Invoke($"[updater] update available: {remoteVer}");
            return (remoteVer, downloadUrl, release.Body ?? "");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[updater] check error: {ex.Message}");
            return null;
        }
    }

    /// Download the new exe and launch the replacer script.
    public static async Task ApplyAsync(string downloadUrl, Version version, Action<string>? log = null)
    {
        log?.Invoke($"[updater] downloading {downloadUrl}...");
        var tempPath = Path.Combine(Path.GetTempPath(), $"A2Meter_update_{version}.exe");
        using (var stream = await Http.GetStreamAsync(downloadUrl))
        using (var fs = File.Create(tempPath))
            await stream.CopyToAsync(fs);

        var currentExe = Environment.ProcessPath!;
        var batPath = Path.Combine(Path.GetTempPath(), "a2meter_update.bat");
        var bat = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            :retry
            del "{currentExe}" 2>nul
            if exist "{currentExe}" (
                timeout /t 1 /nobreak >nul
                goto retry
            )
            move /Y "{tempPath}" "{currentExe}"
            start "" "{currentExe}"
            del "%~f0"
            """;
        File.WriteAllText(batPath, bat);

        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        log?.Invoke("[updater] update script launched");
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<GitHubRelease>();
    }

    private static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]  public string? TagName { get; set; }
        [JsonPropertyName("body")]      public string? Body { get; set; }
        [JsonPropertyName("assets")]    public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                  public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")]  public string? BrowserDownloadUrl { get; set; }
    }
}
