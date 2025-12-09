using System;
using Dario.Core.Abstraction.Card.Options;

namespace Dario.Core.Application.Card;

internal enum CardDatabaseProvider
{
    SqlServer,
    Oracle,
    Fallback
}

internal static class CardDatabaseProviderResolver
{
    public static CardDatabaseProvider Resolve(CardServicesOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabaseProvider))
        {
            throw new InvalidOperationException("DatabaseProvider must be specified.");
        }

        if (options.DatabaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return CardDatabaseProvider.SqlServer;
        }

        if (options.DatabaseProvider.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return CardDatabaseProvider.Oracle;
        }

        if (options.DatabaseProvider.Equals("Fallback", StringComparison.OrdinalIgnoreCase))
        {
            return CardDatabaseProvider.Fallback;
        }

        throw new InvalidOperationException($"Unsupported database provider '{options.DatabaseProvider}'.");
    }
}