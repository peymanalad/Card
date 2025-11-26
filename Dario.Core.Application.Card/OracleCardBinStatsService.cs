using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System.Configuration;
using System.Data;

public class OracleCardBinStatsService : ICardBinStatsService
{
    private readonly string _connectionString;
    private readonly IOptions<CardServicesOptions> _configuration;

    public OracleCardBinStatsService(IOptions<CardServicesOptions> configuration)
    {
        _configuration = configuration;
    }

    public async Task IncrementAsync(string bin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bin))
            return;
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "IncrementCardBinDailyStat";
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add("p_Bin", OracleDbType.Varchar2, 6).Value = bin;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    private OracleConnection CreateConnection()
    => new OracleConnection(_configuration.Value.ConnectionString);

    private OracleConnection CreateQueryConnection()
        => new OracleConnection(_configuration.Value.ConnectionStringQuery);
}
