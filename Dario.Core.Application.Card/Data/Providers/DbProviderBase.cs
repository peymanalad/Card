using System.Data;
using System.Data.Common;
using Dario.Core.Abstraction.Card.Data;
using Dario.Core.Abstraction.Card.Options;

namespace Dario.Core.Application.Card.Data.Providers;

internal abstract class DbProviderBase : IDbProvider
{
    protected readonly CardServicesOptions Options;

    protected DbProviderBase(CardServicesOptions options)
    {
        Options = options;
    }

    public abstract DatabaseProviderType ProviderType { get; }

    public abstract DbCommand CreateCommand(DbConnection connection, string commandText, CommandType commandType);

    public abstract DbConnection CreateConnection(ConnectionKind kind);

    public abstract DbParameter CreateCursorParameter(string name);

    public abstract DbParameter CreateParameter(string name, DbType dbType, object? value, ParameterDirection direction);

    public virtual bool HasConnectionString(ConnectionKind kind)
    {
        var hasPrimary = !string.IsNullOrWhiteSpace(GetPrimaryConnectionString());
        var hasQuery = !string.IsNullOrWhiteSpace(GetQueryConnectionString());

        return kind == ConnectionKind.Query ? hasQuery || hasPrimary : hasPrimary;
    }

    protected abstract string GetPrimaryConnectionString();

    protected abstract string GetQueryConnectionString();

    protected string GetConnectionString(ConnectionKind kind)
    {
        var primary = GetPrimaryConnectionString();
        var query = GetQueryConnectionString();

        if (kind == ConnectionKind.Query && !string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        return primary;
    }
}