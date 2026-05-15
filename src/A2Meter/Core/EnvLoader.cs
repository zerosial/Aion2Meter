using System;
using System.IO;
using System.Reflection;

namespace A2Meter.Core;

internal static class EnvLoader
{
    public static void Load(string filePath)
    {
        // 1. If physical .env file exists next to the executable (e.g., for overriding), load it
        if (File.Exists(filePath))
        {
            try
            {
                ParseAndLoadLines(File.ReadAllLines(filePath));
                return;
            }
            catch { /* fallback */ }
        }

        // 2. Otherwise, check for embedded .env resource (e.g., for pre-packaged release)
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "A2Meter.env";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var lines = new System.Collections.Generic.List<string>();
                while (reader.ReadLine() is { } line)
                {
                    lines.Add(line);
                }
                ParseAndLoadLines(lines);
            }
        }
        catch { /* best effort */ }
    }

    private static void ParseAndLoadLines(System.Collections.Generic.IEnumerable<string> lines)
    {
        foreach (var line in lines)
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
}
