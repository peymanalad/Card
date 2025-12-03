using Dapper;
using Dario.Core.Abstraction.Card;
using Dario.Core.Abstraction.Card.Data;
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
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<CardServices> _logger;
    private readonly string _encryptionKey;
    public CardServices(IOptions<CardServicesOptions> configuration, IDbConnectionFactory connectionFactory, ILogger<CardServices> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _encryptionKey = string.IsNullOrWhiteSpace(configuration.Value.EncryptionKey)
            ? "19F83D0DFDE6C9ECE44B735AF7DEC8B3"
            : configuration.Value.EncryptionKey;
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

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ConnectionKind.Primary);
            await using var command = _connectionFactory.CreateCommand(connection, procedureName, CommandType.StoredProcedure);

            AddInputParameter(command, "p_CardHash", DbType.String, cardHash);
            AddInputParameter(command, "p_CardData", DbType.String, encryptedPan);
            AddInputParameter(command, "p_CardBin", DbType.Int64, cardBin);
            AddInputParameter(command, "p_CardProduct", DbType.String, cardProduct);
            AddInputParameter(command, "p_CardEnd", DbType.String, cardEnd);
            AddInputParameter(command, "p_CardExpDate", DbType.String, encryptedExpDate);
            AddOutputCursor(command);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
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

    private void AddInputParameter(DbCommand command, string name, DbType dbType, object? value)
    {
        var parameter = _connectionFactory.CreateParameter(name, dbType, value, ParameterDirection.Input);
        command.Parameters.Add(parameter);
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
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ConnectionKind.Query);

            var procedureName = _connectionFactory.ActiveProvider == DatabaseProviderType.SqlServer
                ? sqlServerProcedureName
                : oracleProcedureName;

            await using var command = _connectionFactory.CreateCommand(connection, procedureName, CommandType.Text);

            var result = await command.ExecuteScalarAsync();
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
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ConnectionKind.Primary);
        await using var command = _connectionFactory.CreateCommand(connection, procedureName, CommandType.StoredProcedure);

        DbDataReader? reader = null;
        var parameterNames = new[] { "p_Id", "Id", "p_CardId" };
        for (var attempt = 0; attempt < parameterNames.Length; attempt++)
        {
            var parameterName = parameterNames[attempt];
            command.Parameters.Clear();
            AddInputParameter(command, parameterName, DbType.Int64, cardId);
            AddOutputCursor(command);

            try
            {
                reader = await command.ExecuteReaderAsync();
                break;
            }
            catch (DbException ex) when (IsParameterBindingException(ex))
            {
                reader?.Dispose();
                reader = null;
                continue;
            }
        }

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
    private static bool IsParameterBindingException(DbException ex)
    {
        if (ex.GetType().Name == "OracleException")
        {
            var numberProperty = ex.GetType().GetProperty("Number");
            if (numberProperty?.GetValue(ex) is int number && (number == 6550 || number == 1036))
            {
                return true;
            }
        }

        return false;
    }

    private void AddOutputCursor(DbCommand command)
    {
        var cursorParameter = _connectionFactory.CreateCursorParameter("o_cursor");
        if (!command.Parameters.Contains(cursorParameter.ParameterName))
        {
            command.Parameters.Add(cursorParameter);
        }
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

}
