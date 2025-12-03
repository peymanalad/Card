using Dario.Core.Abstraction.Card.Data;
using Dario.Core.Abstraction.Card.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;

namespace Dario.Core.Application.Card.Data;

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly CardServicesOptions _options;
    private readonly ILogger<DbConnectionFactory> _logger;
    private DatabaseProviderType? _activeProvider;

    public DbConnectionFactory(IOptions<CardServicesOptions> options, ILogger<DbConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public DatabaseProviderType ActiveProvider => EnsureProviderInitialized();

    public async Task<DbConnection> CreateOpenConnectionAsync(ConnectionKind connectionKind, CancellationToken cancellationToken = default)
    {
        var provider = EnsureProviderInitialized();
        var connectionString = ResolveConnectionString(provider, connectionKind);

        DbConnection connection = provider switch
        {
            DatabaseProviderType.SqlServer => new SqlConnection(connectionString),
            DatabaseProviderType.Oracle => new OracleConnection(connectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public DbCommand CreateCommand(DbConnection connection, string commandText, CommandType commandType)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandType = commandType;
        return command;
    }

    public DbParameter CreateParameter(string parameterName, DbType dbType, object? value, ParameterDirection direction)
    {
        var provider = EnsureProviderInitialized();
        var formattedName = FormatParameterName(provider, parameterName);

        DbParameter parameter = provider switch
        {
            DatabaseProviderType.SqlServer => new SqlParameter(formattedName, value ?? DBNull.Value)
            {
                DbType = dbType,
                Direction = direction
            },
            DatabaseProviderType.Oracle => new OracleParameter
            {
                ParameterName = formattedName,
                DbType = dbType,
                Direction = direction,
                Value = value ?? DBNull.Value
            },
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };

        return parameter;
    }

    private DatabaseProviderType EnsureProviderInitialized()
    {
        if (_activeProvider.HasValue)
        {
            return _activeProvider.Value;
        }

        var provider = DetermineProvider();
        _activeProvider = provider;
        _logger.LogInformation("Database provider resolved to {Provider}", provider);
        return provider;
    }

    private DatabaseProviderType DetermineProvider()
    {
        var oraclePrimary = !string.IsNullOrWhiteSpace(_options.ConnectionString);
        var oracleQuery = !string.IsNullOrWhiteSpace(_options.ConnectionStringQuery);
        var sqlPrimary = !string.IsNullOrWhiteSpace(_options.SqlConnectionString);
        var sqlQuery = !string.IsNullOrWhiteSpace(_options.SqlConnectionStringQuery);

        if (oraclePrimary)
        {
            return DatabaseProviderType.Oracle;
        }

        if (sqlPrimary)
        {
            return DatabaseProviderType.SqlServer;
        }

        if (oracleQuery)
        {
            return DatabaseProviderType.Oracle;
        }

        if (sqlQuery)
        {
            return DatabaseProviderType.SqlServer;
        }

        throw new InvalidOperationException("No available database provider could be initialized.");
    }

    private string ResolveConnectionString(DatabaseProviderType provider, ConnectionKind connectionKind)
    {
        string? connectionString = provider switch
        {
            DatabaseProviderType.Oracle when connectionKind == ConnectionKind.Primary => _options.ConnectionString,
            DatabaseProviderType.Oracle => _options.ConnectionStringQuery,
            DatabaseProviderType.SqlServer when connectionKind == ConnectionKind.Primary => _options.SqlConnectionString,
            DatabaseProviderType.SqlServer => _options.SqlConnectionStringQuery,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = provider switch
            {
                DatabaseProviderType.Oracle => connectionKind == ConnectionKind.Primary
                    ? _options.ConnectionStringQuery
                    : _options.ConnectionString,
                DatabaseProviderType.SqlServer => connectionKind == ConnectionKind.Primary
                    ? _options.SqlConnectionStringQuery
                    : _options.SqlConnectionString,
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"No connection string configured for provider '{provider}' and connection kind '{connectionKind}'.");
        }

        return connectionString;
    }

    private static string FormatParameterName(DatabaseProviderType provider, string parameterName)
    {
        var trimmed = parameterName.Trim();

        return provider switch
        {
            DatabaseProviderType.SqlServer => trimmed.StartsWith("@", StringComparison.Ordinal)
                ? trimmed
                : $"@{trimmed}",
            DatabaseProviderType.Oracle => trimmed.StartsWith(":", StringComparison.Ordinal)
                ? trimmed
                : $":{trimmed}",
            _ => trimmed
        };
    }
}