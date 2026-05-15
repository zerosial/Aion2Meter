using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using A2Meter.Dps;

namespace A2Meter.Api;

internal static class StatsUploader
{
    private static readonly HttpClient Http;

    static StatsUploader()
    {
        // Default to production server; override with UPLOAD_URL env var for dev/testing.
        string uploadUrl = Environment.GetEnvironmentVariable("UPLOAD_URL")
                           ?? "https://aion2.cielui.com";
        if (!uploadUrl.EndsWith("/")) uploadUrl += "/";

        Http = new HttpClient
        {
            BaseAddress = new Uri(uploadUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };

        Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        Http.DefaultRequestHeaders.Add("Accept", "application/json");

        string apiKey = Environment.GetEnvironmentVariable("UPLOAD_API_KEY")
                        ?? "ciel_a2m_secure_tr_9f3b8a1c6e2d4d";
        if (!string.IsNullOrEmpty(apiKey))
        {
            Http.DefaultRequestHeaders.Add("X-Upload-Api-Key", apiKey);
        }
    }

    /// <summary>
    /// Upload a completed boss combat record to the backend.
    /// Only uploads records with a valid boss name (skips field/dummy combat).
    /// Returns Task for safe fire-and-forget usage.
    /// </summary>
    public static async Task UploadRecordAsync(CombatRecord record, string selfName, string selfServer)
    {
        // Guard: skip if essential data is missing
        if (record == null || string.IsNullOrEmpty(selfName) || string.IsNullOrEmpty(selfServer))
            return;

        // Guard: only upload boss combat (skip field mobs, dummies, etc.)
        if (string.IsNullOrEmpty(record.BossName))
            return;

        try
        {
            var payload = new
            {
                uploaderName = selfName,
                uploaderServer = selfServer,
                bossName = record.BossName,
                field = record.FieldName ?? "필드",
                durationSec = record.DurationSec,
                timestamp = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                members = record.Snapshot.Players.Select(p => new
                {
                    name = StripServerSuffix(p.Name),
                    // Fallback: if party member's server is unknown, use uploader's server
                    server = string.IsNullOrEmpty(p.ServerName) ? selfServer : p.ServerName,
                    className = JobCodeToKey(p.JobCode),
                    dps = p.Dps,
                    sharePct = Math.Round(p.DamagePercent * 100, 1),
                    critPct = Math.Round(p.CritRate * 100, 1),
                    combatPower = p.CombatPower,
                    combatScore = p.CombatScore
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var resp = await Http.PostAsync("/api/logs/upload", content);
            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[StatsUploader] Uploaded stats successfully for boss {record.BossName} ({record.FieldName})");

                // Trigger a background scrape of aion.ing for the player to fetch ranking/profile data
                try
                {
                    _ = Http.GetAsync($"/api/character?server={Uri.EscapeDataString(selfServer)}&name={Uri.EscapeDataString(selfName)}&force=true");
                }
                catch { /* fire and forget background task error suppression */ }
            }
            else
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                Console.Error.WriteLine($"[StatsUploader] Upload failed: HTTP {resp.StatusCode} | {errBody}");
            }
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[StatsUploader] Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("[StatsUploader] Upload timed out.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[StatsUploader] Upload exception: {ex.Message}");
        }
    }

    private static string StripServerSuffix(string name)
    {
        int idx = name.IndexOf('[');
        return idx > 0 ? name[..idx] : name;
    }

    private static string JobCodeToKey(int gameCode) => JobMapping.GameToJobName(gameCode);
}
