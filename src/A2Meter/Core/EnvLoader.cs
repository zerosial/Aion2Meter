using System;
using System.IO;

namespace A2Meter.Core;

internal static class EnvLoader
{
    public static void Load(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#") || trimmed.StartsWith("//")) continue;

                int idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;

                string key = trimmed[..idx].Trim();
                string val = trimmed[(idx + 1)..].Trim();

                if (val.StartsWith("\"") && val.EndsWith("\""))
                    val = val[1..^1];
                else if (val.StartsWith("'") && val.EndsWith("'"))
                    val = val[1..^1];

                Environment.SetEnvironmentVariable(key, val);
            }
        }
        catch { /* suppression for safety */ }
    }
}
