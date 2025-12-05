using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using Dario.Core.Abstraction.Card.Data;
using Dario.Core.Abstraction.Card.Options;

namespace Dario.Core.Application.Card.Data.Providers;

internal sealed class SqlServerDbProvider : DbProviderBase
{
    public SqlServerDbProvider(CardServicesOptions options) : base(options)
    {
    }

    public override DatabaseProviderType ProviderType => DatabaseProviderType.SqlServer;

    public override DbCommand CreateCommand(DbConnection connection, string commandText, CommandType commandType)
    {
        var command = (SqlCommand)connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandType = commandType;
        return command;
    }

    public override DbConnection CreateConnection(ConnectionKind kind)
    {
        return new SqlConnection(GetConnectionString(kind));
    }

    public override DbParameter CreateCursorParameter(string name)
    {
        return new SqlParameter
        {
            ParameterName = name,
            SqlDbType = SqlDbType.Variant,
            Direction = ParameterDirection.Output
        };
    }

    public override DbParameter CreateParameter(string name, DbType dbType, object? value, ParameterDirection direction)
    {
        var parameter = new SqlParameter
        {
            ParameterName = name,
            SqlDbType = MapSqlType(dbType),
            Direction = direction,
            Value = value ?? DBNull.Value
        };

        return parameter;
    }

    protected override string GetPrimaryConnectionString() => Options.SqlConnectionString;

    protected override string GetQueryConnectionString() => Options.SqlConnectionStringQuery;

    private static SqlDbType MapSqlType(DbType dbType)
    {
        return dbType switch
        {
            DbType.Int64 => SqlDbType.BigInt,
            DbType.Date => SqlDbType.Date,
            DbType.DateTime => SqlDbType.DateTime2,
            _ => SqlDbType.NVarChar
        };
    }
}