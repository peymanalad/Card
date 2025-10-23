using System;
using System.IO;

public static class EnvLoader
{
    public static void Load(string path = ".env")
    {
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                continue;

            var idx = trimmed.IndexOf('=');
            if (idx < 0) continue;

            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim();

            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value[1..^1];

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
