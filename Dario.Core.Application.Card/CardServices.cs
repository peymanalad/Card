using Dapper;
using Dario.Core.Abstraction.Card;
using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Domain.Card;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using Rayanparsi.Core.Domain.Entities;
using Rayanparsi.Utilities.Extensions;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Transactions;

namespace Dario.Core.Application.Card;

public class CardServices : ICardServices
{
    private readonly ILogger<CardServices> _logger;
    private readonly string _encryptionKey;
    private readonly IDbConnection _dbConnection;
    private readonly IDbConnection _dbConnectionQuery;
    private readonly bool _isSqlServer;

    public CardServices(IOptions<CardServicesOptions> configuration, ILogger<CardServices> logger)
    {
        _logger = logger;
        _encryptionKey = configuration.Value.EncryptionKey;
        (_dbConnection, _dbConnectionQuery, _isSqlServer) = InitializeConnections(configuration.Value);
    }

    private (IDbConnection Primary, IDbConnection Query, bool IsSqlServer) InitializeConnections(CardServicesOptions options)
    {
        var provider = CardDatabaseProviderResolver.Resolve(options);

        return provider switch
        {
            CardDatabaseProvider.SqlServer => InitializeSqlConnections(options),
            CardDatabaseProvider.Oracle => InitializeOracleConnections(options),
            CardDatabaseProvider.Fallback => InitializeWithFallback(options),
            _ => throw new InvalidOperationException($"Unsupported database provider '{options.DatabaseProvider}'."),
        };
    }

    private (IDbConnection Primary, IDbConnection Query, bool IsSqlServer) InitializeSqlConnections(CardServicesOptions options)
    {
        IDbConnection? sqlPrimary = null;
        IDbConnection? sqlQuery = null;
        try
        {
            sqlPrimary = new SqlConnection(options.ConnectionString); sqlPrimary.Open();

            sqlQuery = new SqlConnection(options.ConnectionStringQuery);
            sqlQuery.Open();

            return (sqlPrimary, sqlQuery, true);
        }
        catch (Exception sqlEx)
        {
            sqlPrimary?.Dispose();
            sqlQuery?.Dispose();
            throw new InvalidOperationException("Failed to initialize SQL Server connections.", sqlEx);
        }
    }
    private (IDbConnection Primary, IDbConnection Query, bool IsSqlServer) InitializeOracleConnections(CardServicesOptions options)
    {

        IDbConnection? oraclePrimary = null;
        IDbConnection? oracleQuery = null;
        try
        {
            oraclePrimary = new OracleConnection(options.ConnectionString);
            oraclePrimary.Open();

            oracleQuery = new OracleConnection(options.ConnectionStringQuery);
            oracleQuery.Open();

            return (oraclePrimary, oracleQuery, false);
        }
        catch (Exception oracleEx)
        {
            oraclePrimary?.Dispose();
            oracleQuery?.Dispose();
            throw new InvalidOperationException("Failed to initialize Oracle connections.", oracleEx);
        }
    }

    private (IDbConnection Primary, IDbConnection Query, bool IsSqlServer) InitializeWithFallback(CardServicesOptions options)
    {
        Exception? sqlInitializationException = null;

        try
        {
            _logger.LogInformation("Database provider fallback enabled; attempting SQL Server connection.");
            return InitializeSqlConnections(options);
        }
        catch (Exception sqlEx)
        {
            sqlInitializationException = sqlEx;
            _logger.LogWarning(sqlEx, "Unable to open SQL Server connections; attempting Oracle fallback.");
        }

        try
        {
            return InitializeOracleConnections(options);
        }
        catch (Exception oracleEx)
        {

            var message = "Failed to initialize database connections using SQL Server first, then Oracle.";
            if (sqlInitializationException is not null)
            {
                throw new InvalidOperationException(message, new AggregateException(sqlInitializationException, oracleEx));
            }

            throw new InvalidOperationException(message, oracleEx);
        }
    }

    private static async Task EnsureOpenAsync(IDbConnection connection)
    {
        if (connection.State == ConnectionState.Open)
        {
            return;
        }

        if (connection is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync();
            return;
        }

        connection.Open();
    }

    public async Task<RayanResponse<CardResponse>> CardGetAsync(CardRequest request)
    {
        const string procedureName = "DarioCardStorage";
        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = "",
        };
        try
        {
            var cardPan = request.CardPan ?? string.Empty;
            var cardBinText = cardPan.CardBin();
            var cardProduct = cardPan.CardProduct();
            var cardEnd = cardPan.CardEnd();
            var cardHash = cardPan.CardHash();
            var encryptedPan = cardPan.EncryptString(_encryptionKey);
            var cardExpDate = request.CardExDate ?? string.Empty;
            var encryptedExpDate = cardExpDate.EncryptString(_encryptionKey);

            if (!long.TryParse(cardBinText, out var cardBin))
            {
                entity.message = "Card BIN is invalid.";
                _logger.LogWarning("Unable to parse card BIN from PAN ending with {CardEnd}", cardEnd);
                return entity;
            }

            await EnsureOpenAsync(_dbConnection);
            var parameters = CreateCardGetParameters(cardHash, encryptedPan, cardBin, cardProduct, cardEnd, encryptedExpDate);
            var command = new CommandDefinition(procedureName, parameters, commandType: CommandType.StoredProcedure);

            await using var reader = (DbDataReader?)await _dbConnection.ExecuteReaderAsync(command);
            if (reader is not null && await reader.ReadAsync())
            {
                var cardIdOrdinal = reader.GetOrdinal("CARDID");
                var cardIdValue = reader.GetValue(cardIdOrdinal);
                var cardId = Convert.ToInt64(cardIdValue);

                entity.item = new CardResponse()
                {
                    CardId = cardId,
                    CardBin = cardBinText,
                    CardData = cardEnd,
                    CardProductCode = cardProduct
                };
                entity.statusCode = 0;
                entity.isError = false;
            }
            else
            {
                entity.message = "No card record returned from storage.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling DarioCardStorage for card ending with {CardEnd}", request.CardPan.CardEnd());
            entity.message = ex.Message;
        }
        return entity;
    }

    private SqlMapper.IDynamicParameters CreateCardGetParameters(string cardHash, string encryptedPan, long cardBin, string cardProduct, string cardEnd, string encryptedExpDate)
    {
        if (_isSqlServer)
        {
            return CreateSqlCardGetParameters(cardHash, encryptedPan, cardBin, cardProduct, cardEnd, encryptedExpDate);
        }

        return CreateOracleCardGetParameters(cardHash, encryptedPan, cardBin, cardProduct, cardEnd, encryptedExpDate);
    }

    private DynamicParameters CreateSqlCardGetParameters(string cardHash, string encryptedPan, long cardBin, string cardProduct, string cardEnd, string encryptedExpDate)
    {
        var parameters = new DynamicParameters();
        AddSqlInputParameter(parameters, DbType.String, cardHash, "p_CardHash", "CardHash");
        AddSqlInputParameter(parameters, DbType.String, encryptedPan, "p_CardData", "CardData");
        AddSqlInputParameter(parameters, DbType.Int64, cardBin, "p_CardBin", "CardBin");
        AddSqlInputParameter(parameters, DbType.String, cardProduct, "p_CardProduct", "CardProduct");
        AddSqlInputParameter(parameters, DbType.String, cardEnd, "p_CardEnd", "CardEnd");
        AddSqlInputParameter(parameters, DbType.String, encryptedExpDate, "p_CardExpDate", "CardExpDate");
        AddOutputCursor(parameters);
        return parameters;
    }

    private OracleDynamicParameters CreateOracleCardGetParameters(string cardHash, string encryptedPan, long cardBin, string cardProduct, string cardEnd, string encryptedExpDate)
    {
        var parameters = new OracleDynamicParameters();
        AddOracleInputParameter(parameters, OracleDbType.Varchar2, cardHash, "p_CardHash");
        AddOracleInputParameter(parameters, OracleDbType.Varchar2, encryptedPan, "p_CardData");
        AddOracleInputParameter(parameters, OracleDbType.Int64, cardBin, "p_CardBin");
        AddOracleInputParameter(parameters, OracleDbType.Varchar2, cardProduct, "p_CardProduct");
        AddOracleInputParameter(parameters, OracleDbType.Varchar2, cardEnd, "p_CardEnd");
        AddOracleInputParameter(parameters, OracleDbType.Varchar2, encryptedExpDate, "p_CardExpDate");
        AddOracleOutputCursor(parameters, "o_cursor");
        return parameters;
    }

    public async Task<RayanResponse<CardResponse>> CardGetByIdAsync(CardRequest request)
    {
        const string procedureName = "DarioCardByIdData";

        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = "",
        };
        try
        {
            var card = await ExecuteCardLookupAsync(procedureName, request.CardId);
            if (card is null)
            {
                entity.message = "No card record returned.";
                return entity;
            }

            entity.item = card;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling DarioCardByIdData for card id {CardId}", request.CardId);
            entity.message = ex.Message;
        }
        return entity;
    }

    public async Task<RayanResponse<CardResponse>> CardDataGetByIdAsync(CardRequest request)
    {
        const string procedureName = "DarioCardByIdData";


        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = "",
        };
        try
        {
            var card = await ExecuteCardLookupAsync(procedureName, request.CardId);
            if (card is null)
            {
                entity.message = "No card record returned.";
                return entity;
            }

            card.CardId = request.CardId;
            card.CardPan = card.CardData.DecryptString(_encryptionKey);
            card.CardExDate = card.CardExDate.DecryptString(_encryptionKey);
            entity.item = card;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling DarioCardByIdData for card id {CardId}", request.CardId);
            entity.message = ex.Message;
        }
        return entity;
    }

    public async Task<RayanResponse<bool>> HealthAsync()
    {
        const string oracleProcedureName = "SELECT 1 FROM DUAL";
        const string sqlServerProcedureName = "SELECT 1";

        RayanResponse<bool> entity = new RayanResponse<bool>()
        {
            isError = true,
            statusCode = 84,
            message = ""
        };
        try
        {
            await EnsureOpenAsync(_dbConnectionQuery);
            var procedureName = _isSqlServer ? sqlServerProcedureName : oracleProcedureName;

            var result = await _dbConnectionQuery.ExecuteScalarAsync(procedureName, commandType: CommandType.Text);
            entity.item = Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while executing health check against the active database provider.");
            entity.message = ex.Message;
        }
        return entity;
    }



    private async Task<CardResponse?> ExecuteCardLookupAsync(string procedureName, long cardId)
    {
        await EnsureOpenAsync(_dbConnection);
        var parameters = CreateCardLookupParameters(cardId);
        var command = new CommandDefinition(procedureName, parameters, commandType: CommandType.StoredProcedure);
        await using var reader = (DbDataReader?)await _dbConnection.ExecuteReaderAsync(command);
        if (reader is null)
        {
            return null;
        }
        await using (reader)
        {
            if (!await reader.ReadAsync())
            {
                return null;
            }
            return MapCardResponse(reader);
        }
    }

    private SqlMapper.IDynamicParameters CreateCardLookupParameters(long cardId)
    {
        if (_isSqlServer)
        {
            var parameters = new DynamicParameters();
            var parameterName = SelectParameterName("p_Id", "Id", "p_CardId", "CardId");
            AddSqlInputParameter(parameters, DbType.Int64, cardId, parameterName);
            AddOutputCursor(parameters);
            return parameters;
        }

        var oracleParameters = new OracleDynamicParameters();
        AddOracleInputParameter(oracleParameters, OracleDbType.Int64, cardId, "p_Id");
        AddOracleOutputCursor(oracleParameters, "o_cursor");
        return oracleParameters;
    }

    private void AddSqlInputParameter(DynamicParameters parameters, DbType dbType, object? value, params string[] names)
    {
        var parameterName = SelectParameterName(names);
        parameters.Add(parameterName, value, dbType, ParameterDirection.Input);
    }

    private static void AddOracleInputParameter(OracleDynamicParameters parameters, OracleDbType dbType, object? value, string name)
    {
        parameters.Add(name, dbType, value, ParameterDirection.Input);
    }

    private static void AddOracleOutputCursor(OracleDynamicParameters parameters, string name)
    {
        parameters.Add(name, OracleDbType.RefCursor, null, ParameterDirection.Output);
    }


    private void AddOutputCursor(DynamicParameters parameters)
    {
        if (_isSqlServer)
        {
            return;
        }
        parameters.AddDynamicParams(new OracleRefCursorParameter("o_cursor"));
    }

    private sealed class OracleRefCursorParameter : SqlMapper.IDynamicParameters
    {
        private readonly string _parameterName;

        public OracleRefCursorParameter(string parameterName)
        {
            _parameterName = parameterName;
        }

        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            if (command is OracleCommand oracleCommand)
            {
                var cursor = oracleCommand.Parameters.Add(_parameterName, OracleDbType.RefCursor);
                cursor.Direction = ParameterDirection.Output;
                return;
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = _parameterName;
            parameter.Direction = ParameterDirection.Output;
            command.Parameters.Add(parameter);
        }
    }

    private string SelectParameterName(params string[] names)
    {
        if (names is { Length: > 0 })
        {
            if (_isSqlServer)
            {
                foreach (var candidate in names)
                {
                    if (!candidate.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            return names[0];
        }

        throw new ArgumentException("At least one parameter name must be provided.", nameof(names));
    }
    private static CardResponse MapCardResponse(IDataRecord record)
    {
        return new CardResponse
        {
            CardId = GetInt64(record, "CARDID"),
            CardPan = GetString(record, "CARDPAN"),
            CardProductCode = GetString(record, "CARDPRODUCTCODE"),
            CardData = GetString(record, "CARDDATA"),
            CardHash = GetString(record, "CARDHASH"),
            CardExDate = GetString(record, "CARDEXDATE"),
            CardMask = GetString(record, "CARDMASK"),
            CardBin = GetString(record, "CARDBIN"),
            CardBinName = GetString(record, "CARDBINNAME"),
            CardName = GetString(record, "CARDNAME"),
            CardFamily = GetString(record, "CARDFAMILY"),
            CardNationalCode = GetString(record, "CARDNATIONALCODE"),
            CardIban = GetString(record, "CARDIBAN"),
        };
    }

    private static string GetString(IDataRecord record, string columnName)
    {
        return TryGetValue(record, columnName, value => value?.ToString() ?? string.Empty, string.Empty);
    }

    private static long GetInt64(IDataRecord record, string columnName)
    {
        return TryGetValue(record, columnName, value => Convert.ToInt64(value, CultureInfo.InvariantCulture), 0L);
    }

    private static T TryGetValue<T>(IDataRecord record, string columnName, Func<object, T> converter, T defaultValue)
    {
        if (TryGetOrdinal(record, columnName, out var ordinal) && !record.IsDBNull(ordinal))
        {
            return converter(record.GetValue(ordinal));
        }

        return defaultValue;
    }

    private static bool TryGetOrdinal(IDataRecord record, string columnName, out int ordinal)
    {
        for (var i = 0; i < record.FieldCount; i++)
        {
            var fieldName = record.GetName(i);
            if (fieldName.Equals(columnName, StringComparison.OrdinalIgnoreCase) ||
                NormalizeColumnName(fieldName).Equals(NormalizeColumnName(columnName), StringComparison.OrdinalIgnoreCase))
            {
                ordinal = i;
                return true;
            }
        }

        ordinal = -1;
        return false;
    }

    private static string NormalizeColumnName(string columnName)
    {
        return columnName.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private sealed class OracleDynamicParameters : SqlMapper.IDynamicParameters
    {
        private readonly List<Action<OracleCommand>> _parameterCallbacks = new();

        public void Add(string name, OracleDbType dbType, object? value, ParameterDirection direction)
        {
            _parameterCallbacks.Add(command =>
            {
                var parameter = command.Parameters.Add(name, dbType);
                parameter.Direction = direction;
                parameter.Value = value ?? DBNull.Value;
            });
        }

        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            if (command is not OracleCommand oracleCommand)
            {
                throw new InvalidOperationException("OracleDynamicParameters can only be used with OracleCommand.");
            }

            oracleCommand.BindByName = true;
            foreach (var callback in _parameterCallbacks)
            {
                callback(oracleCommand);
            }
        }
    }
}
