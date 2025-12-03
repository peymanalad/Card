using System.Data;
using System.Data.Common;
using Dario.Core.Abstraction.Card.Data;
using Dario.Core.Abstraction.Card.Options;
using Oracle.ManagedDataAccess.Client;

namespace Dario.Core.Application.Card.Data.Providers;

internal sealed class OracleDbProvider : DbProviderBase
{
    public OracleDbProvider(CardServicesOptions options) : base(options)
    {
    }

    public override DatabaseProviderType ProviderType => DatabaseProviderType.Oracle;

    public override DbCommand CreateCommand(DbConnection connection, string commandText, CommandType commandType)
    {
        var command = (OracleCommand)connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = commandText;
        command.CommandType = commandType;
        return command;
    }

    public override DbConnection CreateConnection(ConnectionKind kind)
    {
        return new OracleConnection(GetConnectionString(kind));
    }

    public override DbParameter CreateCursorParameter(string name)
    {
        return new OracleParameter
        {
            ParameterName = name,
            OracleDbType = OracleDbType.RefCursor,
            Direction = ParameterDirection.Output
        };
    }

    public override DbParameter CreateParameter(string name, DbType dbType, object? value, ParameterDirection direction)
    {
        var parameter = new OracleParameter
        {
            ParameterName = name,
            OracleDbType = MapOracleType(dbType),
            Direction = direction,
            Value = value ?? DBNull.Value
        };

        return parameter;
    }

    protected override string GetPrimaryConnectionString() => Options.ConnectionString;

    protected override string GetQueryConnectionString() => Options.ConnectionStringQuery;

    private static OracleDbType MapOracleType(DbType dbType)
    {
        return dbType switch
        {
            DbType.Int64 => OracleDbType.Int64,
            DbType.Date => OracleDbType.Date,
            DbType.DateTime => OracleDbType.TimeStamp,
            _ => OracleDbType.NVarchar2
        };
    }
}