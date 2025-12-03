using System.Data;
using System.Data.Common;

namespace Dario.Core.Abstraction.Card.Data;

public enum ConnectionKind
{
    Primary,
    Query
}

public enum DatabaseProviderType
{
    Oracle,
    SqlServer
}

public interface IDbConnectionFactory
{
    DatabaseProviderType ActiveProvider { get; }

    Task<DbConnection> CreateOpenConnectionAsync(ConnectionKind connectionKind, CancellationToken cancellationToken = default);

    DbCommand CreateCommand(DbConnection connection, string commandText, CommandType commandType);

    DbParameter CreateParameter(string parameterName, DbType dbType, object? value, ParameterDirection direction);
}