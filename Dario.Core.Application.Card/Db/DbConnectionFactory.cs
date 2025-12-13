using System.Data.Common;
using Dario.Core.Abstraction.Card.Database;
using Dario.Core.Abstraction.Card.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace Dario.Core.Application.Card.Db;

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly CardServicesOptions _options;
    public DatabaseProvider Provider { get; }

    public DbConnectionFactory(IOptions<CardServicesOptions> options)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        Provider = DatabaseProviderParser.Parse(_options.Provider);

        if (Provider is DatabaseProvider.Unknown or DatabaseProvider.Ambiguous)
            throw new UnsupportedDatabaseProviderException(_options.Provider);
    }

    public DbConnection Create()
    {
        return Provider switch
        {
            DatabaseProvider.Oracle => new OracleConnection(_options.ConnectionString),
            DatabaseProvider.SqlServer => new SqlConnection(_options.ConnectionString),
            _ => throw new UnsupportedDatabaseProviderException(_options.Provider)
        };
    }
}