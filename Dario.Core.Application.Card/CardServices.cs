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
using System.Data.SqlClient;
using System.Globalization;
using System.Transactions;

namespace Dario.Core.Application.Card;

public class CardServices : ICardServices
{
    private readonly IOptions<CardServicesOptions> _configuration;
    private readonly ILogger<CardServices> _logger;
    private readonly string _encryptionKey;
    public CardServices(IOptions<CardServicesOptions> configuration, ILogger<CardServices> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _encryptionKey = configuration.Value.EncryptionKey;
    }

    public async Task<RayanResponse<CardResponse>> CardGetAsync(CardRequest request)
    {
        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = ""
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

            using var connection = CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = "DarioCardStorage";
            command.CommandType = CommandType.StoredProcedure;

            AddInputParameter(command, "p_CardHash", OracleDbType.NVarchar2, cardHash);
            AddInputParameter(command, "p_CardData", OracleDbType.NVarchar2, encryptedPan);
            AddInputParameter(command, "p_CardBin", OracleDbType.Int64, cardBin);
            AddInputParameter(command, "p_CardProduct", OracleDbType.NVarchar2, cardProduct);
            AddInputParameter(command, "p_CardEnd", OracleDbType.NVarchar2, cardEnd);
            AddInputParameter(command, "p_CardExpDate", OracleDbType.NVarchar2, encryptedExpDate);

            var cursorParameter = command.Parameters.Add("o_cursor", OracleDbType.RefCursor, ParameterDirection.Output);

            await command.ExecuteNonQueryAsync();

            var refCursor = (OracleRefCursor?)cursorParameter.Value;
            if (refCursor is null)
            {
                entity.message = "Cursor result was empty.";
                return entity;
            }

            using var cursor = refCursor;
            using var reader = cursor.GetDataReader();
            if (reader.Read())
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
        //catch { }
        return entity;
    }

    private static void AddInputParameter(OracleCommand command, string name, OracleDbType dbType, object? value)
    {
        var parameter = command.Parameters.Add(name, dbType, ParameterDirection.Input);
        parameter.Value = value ?? DBNull.Value;
    }

    public async Task<RayanResponse<CardResponse>> CardGetByIdAsync(CardRequest request)
    {
        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = ""
        };
        try
        {
            var card = await ExecuteCardLookupAsync("DarioCardByIdData", request.CardId);
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
        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = ""
        };
        try
        {
            var card = await ExecuteCardLookupAsync("DarioCardByIdData", request.CardId);
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
        RayanResponse<bool> entity = new RayanResponse<bool>()
        {
            isError = true,
            statusCode = 84,
            message = ""
        };
        try
        {
            using var connection = CreateQueryConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            command.CommandType = CommandType.Text;

            var result = await command.ExecuteScalarAsync();
            entity.item = Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while executing health check against Oracle database.");
            entity.message = ex.Message;
        }
        return entity;
    }



    private OracleConnection CreateConnection()
        => new OracleConnection(_configuration.Value.ConnectionString);

    private OracleConnection CreateQueryConnection()
        => new OracleConnection(_configuration.Value.ConnectionStringQuery);

    private async Task<CardResponse?> ExecuteCardLookupAsync(string procedureName, long cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = procedureName;
        command.CommandType = CommandType.StoredProcedure;

        OracleRefCursor? refCursor = null;
        foreach (var parameterName in new[] { "p_Id", "Id", "p_CardId" })
        {
            command.Parameters.Clear();
            AddInputParameter(command, parameterName, OracleDbType.Int64, cardId);
            var cursorParameter = command.Parameters.Add("o_cursor", OracleDbType.RefCursor, ParameterDirection.Output);

            try
            {
                await command.ExecuteNonQueryAsync();
                refCursor = cursorParameter.Value as OracleRefCursor;
                if (refCursor != null)
                {
                    break;
                }
            }
            catch (OracleException ex) when (IsParameterBindingException(ex))
            {
                continue;
            }
        }

        if (refCursor is null)
        {
            return null;
        }

        using var cursor = refCursor;
        using var reader = cursor.GetDataReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapCardResponse(reader);
    }

    private static bool IsParameterBindingException(OracleException ex)
        => ex.Number is 6550 or 1036;

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
