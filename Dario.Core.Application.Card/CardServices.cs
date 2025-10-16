using Dapper;
using Dario.Core.Abstraction.Card;
using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Domain.Card;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
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
            long res = await _dbConnection.QueryFirstOrDefaultAsync<long>("DarioCardStorage",
                    param: new
                    {
                        CardHash = request.CardPan.CardHash(),
                        CardData = request.CardPan.EncryptString(_key),
                        CardBin = request.CardPan.CardBin(),
                        CardProduct = request.CardPan.CardProduct(),
                        CardEnd = request.CardPan.CardEnd(),
                        CardExpDate = request.CardExDate.EncryptString(_key),
                    }
               , commandType: CommandType.StoredProcedure);
            entity.item = new CardResponse() { CardId = res,CardBin = request.CardPan.CardBin()
                ,CardData= request.CardPan.CardEnd(),CardProductCode = request.CardPan.CardProduct()
            };
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch { }
        return entity;
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
            var healthCheck = await _dbConnection.QueryAsync<int>("SELECT 1;");
            entity.item = true;
            entity.statusCode = 0;
            entity.isError = false;
        }
        catch { }
        return entity;
    }
}
