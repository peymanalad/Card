using Dario.Core.Abstraction.Card;
using Dario.Core.Application.Card;
using Dario.Core.Domain.Card;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rayanparsi.Core.Domain.Entities;
using Rayanparsi.Utilities.Extensions;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Dario.Service.Card.API.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
public class CardController : ControllerBase
{
    private readonly ILogger<CardController> _logger;
    private readonly ICardServices _srv;
    private readonly Counter<long> _endpointRequestCounter;
    private readonly Histogram<double> _endpointRequestDuration;
    private readonly ICardBinStatsService _cardBinStatsService;

    public CardController(ILogger<CardController> logger, ICardServices srv, IMeterFactory meterFactory, ICardBinStatsService cardBinStatsService)
    {
        _logger = logger;
        _srv = srv;
        _cardBinStatsService = cardBinStatsService;

        var meter = meterFactory.Create("Dario.Service.Card.API");
        _endpointRequestCounter = meter.CreateCounter<long>(
            name: "card.pool.requests",
            unit: "requests",
            description: "Number of card pool requests per BIN");

    }


    [HttpPost(Name = "Pool")]
    public async Task<RayanResponse<CardResponse>> Pool(CardRequest request)
    {
        var cardPan = request.CardPan ?? string.Empty;
        var cardBin = request.CardPan?.CardBin();

        if (!string.IsNullOrEmpty(cardBin))
        {
            _logger.LogInformation("card is {CardBin}", cardPan[..6]);

            _endpointRequestCounter.Add(
                1,
                new KeyValuePair<string, object?>("card.bin", cardBin));

            await _cardBinStatsService.IncrementAsync(cardBin);
        }
        else
        {
            _logger.LogWarning("Card PAN is empty or invalid.");
        }

        return await _srv.CardGetAsync(request);
    }

    [HttpPost(Name = "Id")]
    public async Task<RayanResponse<CardResponse>> Id(CardRequest request)
    {
        //return await Task.FromResult(await _srv.CardGetByIdAsync(request));
        return await _srv.CardGetByIdAsync(request);
    }
    [HttpPost(Name = "Data")]
    public async Task<RayanResponse<CardResponse>> Data(CardRequest request)
    {
        //return await Task.FromResult(await _srv.CardDataGetByIdAsync(request));
        return await _srv.CardDataGetByIdAsync(request);
    }

    [HttpGet(Name = "Clear")]
    public bool Clear()
    {
        GC.Collect(2);
        return true;
    }
    [HttpGet(Name = "Health")]
    public async Task<bool> Health()
    {
        //return  (_srv.HealthAsync().Result.item);
        var response = await _srv.HealthAsync();
        return response.item;
    }
    [HttpGet(Name = "BinStats")]
    public async Task<IEnumerable<CardBinStatsDto>> BinStats(CancellationToken cancellationToken)
    {
        var stats = await _cardBinStatsService.GetSummaryAsync(cancellationToken);
        return stats;
    }
}
