namespace Dario.Core.Abstraction.Card.Database;

public static class DatabaseProviderParser
{
    public static DatabaseProvider Parse(string? providerRaw)
    {
        var raw = (providerRaw ?? string.Empty).Trim();

        if (raw.Length == 0)
            return DatabaseProvider.Unknown;

        if (raw.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.Oracle;

        if (raw.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Sql Server", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.SqlServer;

        if (raw.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.Unknown;

        if (raw.Equals("Ambiguous", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains(",") || raw.Contains("|") || raw.Contains(";"))
            return DatabaseProvider.Unknown;

        return DatabaseProvider.Unknown;
    }
}