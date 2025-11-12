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
using System.Diagnostics;
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
        const string operationName = "Pool";
        const string procedureName = "DarioCardStorage";
        var tags = CardServicesTelemetry.CreateOperationTags(operationName);
        CardServicesTelemetry.RequestCounter.Add(1, tags);
        using var activity = CardServicesTelemetry.ActivitySource.StartActivity($"CardServices.{operationName}", ActivityKind.Internal);
        activity?.SetTag("oracle.procedure", procedureName);
        activity?.SetTag("card.operation", operationName);

        var stopwatch = Stopwatch.StartNew();
        var isSuccess = false;
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
            activity?.SetTag("card.masked", cardHash);
            activity?.SetTag("card.bin", cardBinText);
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
            command.CommandText = procedureName;
            command.CommandType = CommandType.StoredProcedure;

            AddInputParameter(command, "p_CardHash", OracleDbType.NVarchar2, cardHash);
            AddInputParameter(command, "p_CardData", OracleDbType.NVarchar2, encryptedPan);
            AddInputParameter(command, "p_CardBin", OracleDbType.Int64, cardBin);
            AddInputParameter(command, "p_CardProduct", OracleDbType.NVarchar2, cardProduct);
            AddInputParameter(command, "p_CardEnd", OracleDbType.NVarchar2, cardEnd);
            AddInputParameter(command, "p_CardExpDate", OracleDbType.NVarchar2, encryptedExpDate);

            var cursorParameter = command.Parameters.Add("o_cursor", OracleDbType.RefCursor, ParameterDirection.Output);

            await ExecuteNonQueryWithTelemetryAsync(command, operationName, procedureName, "db.execute");

            var refCursor = (OracleRefCursor?)cursorParameter.Value;
            if (refCursor is null)
            {
                entity.message = "Cursor result was empty.";
                return entity;
            }

            using var cursor = refCursor;
            using var reader = GetCursorDataReaderWithTelemetry(command, cursor, operationName, procedureName);
            if (ReadCursorRowWithTelemetry(command, reader, operationName, procedureName))
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
                isSuccess = true;
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
            CardServicesTelemetry.ErrorCounter.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            CardServicesTelemetry.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            activity?.SetTag("card.request.success", isSuccess);
            if (isSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }
        return entity;
    }

    private static void AddInputParameter(OracleCommand command, string name, OracleDbType dbType, object? value)
    {
        var parameter = command.Parameters.Add(name, dbType, ParameterDirection.Input);
        parameter.Value = value ?? DBNull.Value;
    }

    public async Task<RayanResponse<CardResponse>> CardGetByIdAsync(CardRequest request)
    {
        const string operationName = "Id";
        const string procedureName = "DarioCardByIdData";
        var tags = CardServicesTelemetry.CreateOperationTags(operationName);
        CardServicesTelemetry.RequestCounter.Add(1, tags);
        using var activity = CardServicesTelemetry.ActivitySource.StartActivity($"CardServices.{operationName}", ActivityKind.Internal);
        activity?.SetTag("oracle.procedure", procedureName);
        activity?.SetTag("card.operation", operationName);
        activity?.SetTag("card.identifier", request.CardId);

        var stopwatch = Stopwatch.StartNew();
        var isSuccess = false;

        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = ""
        };
        try
        {
            var card = await ExecuteCardLookupAsync(procedureName, request.CardId, operationName);
            if (card is null)
            {
                entity.message = "No card record returned.";
                return entity;
            }

            entity.item = card;
            entity.statusCode = 0;
            entity.isError = false;
            isSuccess = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling DarioCardByIdData for card id {CardId}", request.CardId);
            entity.message = ex.Message;
            CardServicesTelemetry.ErrorCounter.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            CardServicesTelemetry.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            activity?.SetTag("card.request.success", isSuccess);
            if (isSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }
        return entity;
    }

    public async Task<RayanResponse<CardResponse>> CardDataGetByIdAsync(CardRequest request)
    {
        const string operationName = "Data";
        const string procedureName = "DarioCardByIdData";
        var tags = CardServicesTelemetry.CreateOperationTags(operationName);
        CardServicesTelemetry.RequestCounter.Add(1, tags);
        using var activity = CardServicesTelemetry.ActivitySource.StartActivity($"CardServices.{operationName}", ActivityKind.Internal);
        activity?.SetTag("oracle.procedure", procedureName);
        activity?.SetTag("card.operation", operationName);
        activity?.SetTag("card.identifier", request.CardId);

        var stopwatch = Stopwatch.StartNew();
        var isSuccess = false;


        RayanResponse<CardResponse> entity = new RayanResponse<CardResponse>()
        {
            isError = true,
            statusCode = 84,
            message = ""
        };
        try
        {
            var card = await ExecuteCardLookupAsync(procedureName, request.CardId, operationName);
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
            isSuccess = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling DarioCardByIdData for card id {CardId}", request.CardId);
            entity.message = ex.Message;
            CardServicesTelemetry.ErrorCounter.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            CardServicesTelemetry.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            activity?.SetTag("card.request.success", isSuccess);
            if (isSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }
        return entity;
    }

    public async Task<RayanResponse<bool>> HealthAsync()
    {
        const string operationName = "Health";
        const string procedureName = "SELECT 1 FROM DUAL";
        var tags = CardServicesTelemetry.CreateOperationTags(operationName);
        CardServicesTelemetry.RequestCounter.Add(1, tags);
        using var activity = CardServicesTelemetry.ActivitySource.StartActivity($"CardServices.{operationName}", ActivityKind.Internal);
        activity?.SetTag("oracle.procedure", procedureName);
        activity?.SetTag("card.operation", operationName);

        var stopwatch = Stopwatch.StartNew();
        var isSuccess = false;

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

            var result = await ExecuteScalarWithTelemetryAsync(command, operationName, procedureName, "db.scalar");
            entity.item = Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
            entity.statusCode = 0;
            entity.isError = false;
            isSuccess = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while executing health check against Oracle database.");
            entity.message = ex.Message;
            CardServicesTelemetry.ErrorCounter.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            CardServicesTelemetry.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            activity?.SetTag("card.request.success", isSuccess);
            if (isSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }
        return entity;
    }



    private OracleConnection CreateConnection()
        => new OracleConnection(_configuration.Value.ConnectionString);

    private OracleConnection CreateQueryConnection()
        => new OracleConnection(_configuration.Value.ConnectionStringQuery);

    private async Task ExecuteNonQueryWithTelemetryAsync(OracleCommand command, string endpoint, string dbOperation, string stage, int? attempt = null)
    {
        var statement = GetDbStatement(command, dbOperation);
        var activity = StartDatabaseActivity(endpoint, stage, command, dbOperation, statement, attempt);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await command.ExecuteNonQueryAsync();
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordDatabaseDuration(stopwatch.Elapsed.TotalMilliseconds, endpoint, dbOperation, stage, command, statement, attempt);
            activity?.Stop();
        }
    }

    private async Task<object?> ExecuteScalarWithTelemetryAsync(OracleCommand command, string endpoint, string dbOperation, string stage)
    {
        var statement = GetDbStatement(command, dbOperation);
        var activity = StartDatabaseActivity(endpoint, stage, command, dbOperation, statement, attempt: null);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await command.ExecuteScalarAsync();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordDatabaseDuration(stopwatch.Elapsed.TotalMilliseconds, endpoint, dbOperation, stage, command, statement, attempt: null);
            activity?.Stop();
        }
    }

    private OracleDataReader GetCursorDataReaderWithTelemetry(OracleCommand command, OracleRefCursor cursor, string endpoint, string dbOperation)
    {
        const string stage = "db.cursor.open";
        var statement = GetDbStatement(command, dbOperation);
        var activity = StartDatabaseActivity(endpoint, stage, command, dbOperation, statement, attempt: null);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reader = cursor.GetDataReader();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return reader;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordDatabaseDuration(stopwatch.Elapsed.TotalMilliseconds, endpoint, dbOperation, stage, command, statement, attempt: null);
            activity?.Stop();
        }
    }

    private bool ReadCursorRowWithTelemetry(OracleCommand command, OracleDataReader reader, string endpoint, string dbOperation)
    {
        const string stage = "db.cursor.read";
        var statement = GetDbStatement(command, dbOperation);
        var activity = StartDatabaseActivity(endpoint, stage, command, dbOperation, statement, attempt: null);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var hasRow = reader.Read();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return hasRow;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordDatabaseDuration(stopwatch.Elapsed.TotalMilliseconds, endpoint, dbOperation, stage, command, statement, attempt: null);
            activity?.Stop();
        }
    }

    private Activity? StartDatabaseActivity(string endpoint, string stage, OracleCommand command, string dbOperation, string? statement, int? attempt)
    {
        var activityName = $"CardServices.{endpoint}.{stage}";
        var activity = CardServicesTelemetry.ActivitySource.StartActivity(activityName, ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("db.system", "oracle");
        activity.SetTag("db.operation", dbOperation);
        activity.SetTag("db.stage", stage);
        if (!string.IsNullOrWhiteSpace(statement))
        {
            activity.SetTag("db.statement", statement);
        }

        if (attempt.HasValue)
        {
            activity.SetTag("db.attempt", attempt.Value);
        }

        ApplyConnectionTags(activity, command);

        return activity;
    }

    private static void ApplyConnectionTags(Activity activity, OracleCommand command)
    {
        var connection = command.Connection;
        if (connection is null)
        {
            return;
        }

        var builder = new OracleConnectionStringBuilder(connection.ConnectionString);
        if (!string.IsNullOrWhiteSpace(builder.UserID))
        {
            activity.SetTag("db.user", builder.UserID);
        }

        var (address, port) = ParseDataSource(connection.DataSource ?? builder.DataSource);
        if (!string.IsNullOrWhiteSpace(address))
        {
            activity.SetTag("server.address", address);
        }

        if (port.HasValue)
        {
            activity.SetTag("server.port", port.Value);
        }
    }

    private static void RecordDatabaseDuration(double durationMs, string endpoint, string dbOperation, string stage, OracleCommand command, string? statement, int? attempt)
    {
        var tags = CreateDatabaseTags(endpoint, dbOperation, stage, command, statement, attempt);
        CardServicesTelemetry.DatabaseCallDuration.Record(durationMs, tags);
    }

    private static TagList CreateDatabaseTags(string endpoint, string dbOperation, string stage, OracleCommand command, string? statement, int? attempt)
    {
        var tags = new TagList
        {
            { "endpoint", endpoint },
            { "db.system", "oracle" },
            { "db.operation", dbOperation },
            { "db.stage", stage }
        };

        if (!string.IsNullOrWhiteSpace(statement))
        {
            tags.Add("db.statement", statement);
        }

        if (attempt.HasValue)
        {
            tags.Add("db.attempt", attempt.Value);
        }

        var connection = command.Connection;
        if (connection != null)
        {
            var builder = new OracleConnectionStringBuilder(connection.ConnectionString);

            if (!string.IsNullOrWhiteSpace(builder.UserID))
            {
                tags.Add("db.user", builder.UserID);
            }

            var (address, port) = ParseDataSource(connection.DataSource ?? builder.DataSource);
            if (!string.IsNullOrWhiteSpace(address))
            {
                tags.Add("server.address", address);
            }

            if (port.HasValue)
            {
                tags.Add("server.port", port.Value);
            }
        }

        return tags;
    }

    private static string? GetDbStatement(OracleCommand command, string dbOperation)
    {
        if (command.CommandType == CommandType.StoredProcedure)
        {
            return dbOperation;
        }

        return command.CommandText;
    }

    private static (string? Address, int? Port) ParseDataSource(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return (null, null);
        }

        var trimmed = dataSource.Trim();

        if (trimmed.StartsWith("(DESCRIPTION", StringComparison.OrdinalIgnoreCase))
        {
            var host = ExtractDescriptorValue(trimmed, "HOST");
            var portValue = ExtractDescriptorValue(trimmed, "PORT");
            int? port = null;
            if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
            {
                port = parsedPort;
            }

            return (host, port);
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        var slashIndex = trimmed.IndexOf('/');
        var hostPort = slashIndex >= 0 ? trimmed[..slashIndex] : trimmed;
        var colonIndex = hostPort.LastIndexOf(':');

        string? host = hostPort;
        int? portNumber = null;

        if (colonIndex >= 0 && colonIndex < hostPort.Length - 1)
        {
            host = hostPort[..colonIndex];
            var portSegment = hostPort[(colonIndex + 1)..];
            if (int.TryParse(portSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
            {
                portNumber = parsedPort;
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = null;
        }

        return (host, portNumber);
    }

    private static string? ExtractDescriptorValue(string descriptor, string key)
    {
        var token = $"{key}=";
        var index = descriptor.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + token.Length;
        var end = descriptor.IndexOf(')', start);
        if (end < 0)
        {
            end = descriptor.Length;
        }

        return descriptor[start..end];
    }

    private async Task<CardResponse?> ExecuteCardLookupAsync(string procedureName, long cardId, string endpoint)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = procedureName;
        command.CommandType = CommandType.StoredProcedure;

        OracleRefCursor? refCursor = null;
        var parameterNames = new[] { "p_Id", "Id", "p_CardId" };
        for (var attempt = 0; attempt < parameterNames.Length; attempt++)
        {
            var parameterName = parameterNames[attempt];
            command.Parameters.Clear();
            AddInputParameter(command, parameterName, OracleDbType.Int64, cardId);
            var cursorParameter = command.Parameters.Add("o_cursor", OracleDbType.RefCursor, ParameterDirection.Output);

            try
            {
                await ExecuteNonQueryWithTelemetryAsync(command, endpoint, procedureName, "db.execute", attempt + 1);
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
        using var reader = GetCursorDataReaderWithTelemetry(command, cursor, endpoint, procedureName);
        if (!ReadCursorRowWithTelemetry(command, reader, endpoint, procedureName))
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
