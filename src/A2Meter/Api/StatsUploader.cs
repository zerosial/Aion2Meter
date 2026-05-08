using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using A2Meter.Dps;

namespace A2Meter.Api;

internal static class StatsUploader
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("http://localhost:3001"),
        Timeout = TimeSpan.FromSeconds(5),
    };

    static StatsUploader()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        Http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public static async void UploadRecordAsync(CombatRecord record, string selfName, string selfServer)
    {
        if (record == null || string.IsNullOrEmpty(selfName) || string.IsNullOrEmpty(selfServer))
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
                Console.WriteLine($"[StatsUploader] Uploaded stats successfully for boss {record.BossName}");

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
