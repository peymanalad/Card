using System.Data;
using System.Data.Common;
using Dario.Core.Abstraction.Card.Data;

namespace Dario.Core.Application.Card.Data.Providers;

internal interface IDbProvider
{
    DatabaseProviderType ProviderType { get; }

    DbConnection CreateConnection(ConnectionKind kind);

    DbCommand CreateCommand(DbConnection connection, string commandText, CommandType commandType);

    DbParameter CreateParameter(string name, DbType dbType, object? value, ParameterDirection direction);

    DbParameter CreateCursorParameter(string name);

    bool HasConnectionString(ConnectionKind kind);
}