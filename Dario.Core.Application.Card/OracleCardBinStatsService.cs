using Dario.Core.Abstraction.Card.Database;
using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card.Db;
using Dario.Core.Domain.Card;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Globalization;

namespace Dario.Core.Application.Card;

public class OracleCardBinStatsService : ICardBinStatsService
{
    private readonly CardServicesOptions _configuration;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<OracleCardBinStatsService> _logger;

    public OracleCardBinStatsService(
        IOptions<CardServicesOptions> configuration,
        ILogger<OracleCardBinStatsService> logger,
        IDbConnectionFactory dbConnectionFactory)
    {
        _configuration = configuration.Value ?? throw new ArgumentNullException(nameof(configuration));
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task IncrementAsync(string bin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bin))
            return;

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "IncrementCardBinDailyStat";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add("p_Bin", OracleDbType.Varchar2, 6).Value = bin;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (OracleException ex) when (ex.Number == 257)
        {
            _logger.LogWarning(ex, "Unable to increment BIN stats because the Oracle archiver is full (ORA-00257). Skipping stat update.");
        }
    }
    public async Task<IReadOnlyList<CardBinStatsDto>> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var (monthStart, monthEnd) = GetCurrentPersianMonthRange();

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
    WHERE STATDATE >= :p_MonthStart
      AND STATDATE <  :p_MonthEnd
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
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;

        cmd.CommandTimeout = 0;

        cmd.Parameters.Add("p_MonthStart", OracleDbType.Date).Value = monthStart;
        cmd.Parameters.Add("p_MonthEnd", OracleDbType.Date).Value = monthEnd;

        await using var reader = await cmd.ExecuteReaderAsync();

        var binOrdinal = reader.GetOrdinal("BIN");
        var bankNameOrdinal = reader.GetOrdinal("BANKNAME");
        var todayOrdinal = reader.GetOrdinal("TODAYCOUNT");
        var monthOrdinal = reader.GetOrdinal("MONTHCOUNT");
        var totalOrdinal = reader.GetOrdinal("TOTALCOUNT");

        const string logoBaseUrl = "http://192.168.13.11:5601/logos";

        while (await reader.ReadAsync())
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

    private static (DateTime MonthStart, DateTime MonthEnd) GetCurrentPersianMonthRange()
    {
        var now = DateTime.Now;
        var pc = new PersianCalendar();

        var year = pc.GetYear(now);
        var month = pc.GetMonth(now);

        var monthStart = pc.ToDateTime(year, month, 1, 0, 0, 0, 0);

        var nextYear = month == 12 ? year + 1 : year;
        var nextMonth = month == 12 ? 1 : month + 1;
        var monthEnd = pc.ToDateTime(nextYear, nextMonth, 1, 0, 0, 0, 0);

        return (monthStart, monthEnd);
    }

    private OracleConnection CreateConnection()
    {
        if (_dbConnectionFactory.Provider != DatabaseProvider.Oracle)
        {
            throw new UnsupportedDatabaseProviderException(_dbConnectionFactory.Provider.ToString());
        }

        if (_dbConnectionFactory.Create() is not OracleConnection connection)
        {
            throw new UnsupportedDatabaseProviderException(_dbConnectionFactory.Provider.ToString());
        }

        return connection;
    }

    private OracleConnection CreateQueryConnection()
    {
        var connection = CreateConnection();

        if (!string.IsNullOrWhiteSpace(_configuration.ConnectionStringQuery))
        {
            connection.ConnectionString = _configuration.ConnectionStringQuery;
        }

        return connection;
    }
}
