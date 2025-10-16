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
using System.Transactions;

namespace Dario.Core.Application.Card;

public class CardServices : ICardServices
{
    private readonly IOptions<CardServicesOptions> _configuration;
    private readonly ILogger<CardServices> _logger;
    private readonly IDbConnection _dbConnection;
    private readonly IDbConnection _dbConnectionQuery;
    private static string _key = "19F83D0DFDE6C9ECE44B735AF7DEC8B3";
    public CardServices(IOptions<CardServicesOptions> configuration, ILogger<CardServices> logger)
    {
        _configuration = configuration;
        _logger = logger;
        //_dbConnection = new SqlConnection(configuration.Value.ConnectionString);
        //_dbConnectionQuery= new SqlConnection(configuration.Value.ConnectionStringQuery);
        _dbConnection = new OracleConnection(configuration.Value.ConnectionString);
        _dbConnectionQuery = new OracleConnection(configuration.Value.ConnectionStringQuery);
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
            //long res = await _dbConnection.QueryFirstOrDefaultAsync<long>("DarioCardStorage",
            //        param: new
            //        {
            //            CardHash = request.CardPan.CardHash(),
            //            CardData = request.CardPan.EncryptString(_key),
            //            CardBin = request.CardPan.CardBin(),
            //            CardProduct = request.CardPan.CardProduct(),
            //            CardEnd = request.CardPan.CardEnd(),
            //            CardExpDate = request.CardExDate.EncryptString(_key),
            //        }
            //   , commandType: CommandType.StoredProcedure);
            //entity.item = new CardResponse() { CardId = res,CardBin = request.CardPan.CardBin()
            //    ,CardData= request.CardPan.CardEnd(),CardProductCode = request.CardPan.CardProduct()
            //};
            //entity.statusCode = 0;
            //entity.isError = false;
            var cardPan = request.CardPan ?? string.Empty;
            var cardBinText = cardPan.CardBin();
            var cardProduct = cardPan.CardProduct();
            var cardEnd = cardPan.CardEnd();
            var cardHash = cardPan.CardHash();
            var encryptedPan = cardPan.EncryptString(_key);
            var cardExpDate = request.CardExDate ?? string.Empty;
            var encryptedExpDate = cardExpDate.EncryptString(_key);

            if (!long.TryParse(cardBinText, out var cardBin))
            {
                entity.message = "Card BIN is invalid.";
                _logger.LogWarning("Unable to parse card BIN from PAN ending with {CardEnd}", cardEnd);
                return entity;
            }

            using var connection = new OracleConnection(_configuration.Value.ConnectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
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
            entity.item = await _dbConnection.QueryFirstAsync<CardResponse>("DarioCardByIdData"
                 , param: new { Id = request.CardId }
           , commandType: CommandType.StoredProcedure);
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch { }
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
            CardResponse card = await _dbConnection.QueryFirstAsync<CardResponse>("DarioCardByIdData"
                  , param: new { Id = request.CardId }
            , commandType: CommandType.StoredProcedure);
            card.CardId = request.CardId;
            card.CardPan = card.CardData.DecryptString(_key);
            entity.item = card;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch { }
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
            //var res = _dbConnection.QueryAsync<CardRRBRequest>("GETALL", commandType: CommandType.StoredProcedure).Result.ToList();
            //foreach(var item in res)
            //{
            //   var card= CardGetAsync(new CardRequest() { 
            //    CardPan=item.RRNC
            //    }).Result.item;
            //    CardRRBZRequest request = new CardRRBZRequest()
            //    {
            //        RRN=item.RRN,Id=card.CardId
            //    };
            //   var ress= _dbConnection.QueryAsync<int>("GETALL_Update", request,commandType: CommandType.StoredProcedure).Result;
            //}
            //var healthCheck = await _dbConnection.QueryAsync<int>("SELECT 1;");
            _ = await _dbConnection.QueryAsync<int>("SELECT 1 FROM DUAL");
            entity.item = true;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch { }
        return entity;
    }
}
