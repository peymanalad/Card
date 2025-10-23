using System.IO;

namespace EnvLoader;

/// <summary>
/// Minimal environment loader that reads key/value pairs from a dotenv file and
/// populates the process environment variables so that the ASP.NET configuration
/// system can bind them.
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// Loads environment variables from the specified <paramref name="envFilePath"/>.
    /// If the file is not rooted, the path is resolved relative to the current
    /// working directory.
    /// </summary>
    /// <param name="envFilePath">Optional absolute or relative path to the dotenv file.</param>
    public static void Load(string? envFilePath = null)
    {
        var path = ResolvePath(envFilePath);

        if (path is null || !File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = trimmed[(separatorIndex + 1)..].Trim();

            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? ResolvePath(string? envFilePath)
    {
        if (string.IsNullOrWhiteSpace(envFilePath))
        {
            return Path.Combine(Directory.GetCurrentDirectory(), ".env");
        }

        if (File.Exists(envFilePath))
        {
            return Path.GetFullPath(envFilePath);
        }

        if (Directory.Exists(envFilePath))
        {
            return Path.Combine(Path.GetFullPath(envFilePath), ".env");
        }

        if (Path.IsPathRooted(envFilePath))
        {
            return envFilePath;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), envFilePath);
    }
}