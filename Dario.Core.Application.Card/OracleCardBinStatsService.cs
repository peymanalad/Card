using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card;
using Dario.Core.Domain.Card;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System.Data;

public class OracleCardBinStatsService : ICardBinStatsService
{
    private readonly IOptions<CardServicesOptions> _configuration;

    public OracleCardBinStatsService(IOptions<CardServicesOptions> configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task IncrementAsync(string bin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bin))
            return;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "IncrementCardBinDailyStat";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("p_Bin", OracleDbType.Varchar2, 6).Value = bin;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CardBinStatsDto>> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
WITH data_today AS (
    SELECT BIN, SUM(REQUESTCOUNT) AS TodayCount
    FROM CARDBINDAILYSTATS
    WHERE TRUNC(STATDATE) = TRUNC(SYSDATE)
    GROUP BY BIN
),
data_month AS (
    SELECT BIN, SUM(REQUESTCOUNT) AS MonthCount
    FROM CARDBINDAILYSTATS
    WHERE STATDATE >= TRUNC(SYSDATE, 'MM')
      AND STATDATE < ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1)
    GROUP BY BIN
),
data_total AS (
    SELECT BIN, SUM(REQUESTCOUNT) AS TotalCount
    FROM CARDBINDAILYSTATS
    GROUP BY BIN
)
SELECT
    tot.BIN,
    NVL(b.NAME, tot.BIN)      AS BankName,
    NVL(t.TodayCount, 0)      AS TodayCount,
    NVL(m.MonthCount, 0)      AS MonthCount,
    tot.TotalCount            AS TotalCount
FROM data_total tot
LEFT JOIN data_today t ON t.BIN = tot.BIN
LEFT JOIN data_month m ON m.BIN = tot.BIN
LEFT JOIN RPCARDBANK b ON tot.BIN = TO_CHAR(b.BIN)
ORDER BY tot.BIN";

        var result = new List<CardBinStatsDto>();

        await using var connection = CreateQueryConnection();
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var binOrdinal = reader.GetOrdinal("BIN");
        var bankNameOrdinal = reader.GetOrdinal("BANKNAME");
        var todayOrdinal = reader.GetOrdinal("TODAYCOUNT");
        var monthOrdinal = reader.GetOrdinal("MONTHCOUNT");
        var totalOrdinal = reader.GetOrdinal("TOTALCOUNT");

        const string logoBaseUrl = "http://localhost:13276/logos";

        while (await reader.ReadAsync(cancellationToken))
        {
            var bin = reader.IsDBNull(binOrdinal) ? string.Empty : reader.GetString(binOrdinal);
            var bankName = reader.IsDBNull(bankNameOrdinal) ? string.Empty : reader.GetString(bankNameOrdinal);

            var dto = new CardBinStatsDto
            {
                Bin = bin,
                BankName = bankName,
                TodayCount = reader.IsDBNull(todayOrdinal) ? 0 : reader.GetInt64(todayOrdinal),
                MonthCount = reader.IsDBNull(monthOrdinal) ? 0 : reader.GetInt64(monthOrdinal),
                TotalCount = reader.IsDBNull(totalOrdinal) ? 0 : reader.GetInt64(totalOrdinal),

                LogoUrl = $"{logoBaseUrl}/{bin}.png"
            };

            result.Add(dto);
        }

        return result;
    }

    private OracleConnection CreateConnection()
        => new OracleConnection(_configuration.Value.ConnectionString);

    private OracleConnection CreateQueryConnection()
        => new OracleConnection(_configuration.Value.ConnectionStringQuery);
}
