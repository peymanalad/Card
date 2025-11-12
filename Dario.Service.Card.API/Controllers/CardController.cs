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

    public CardController(ILogger<CardController> logger, ICardServices srv, Meter meter)
    {
        _logger = logger;
        _srv = srv;
        _endpointRequestCounter = meter.CreateCounter<long>("card.endpoint.request.count");
        _endpointRequestDuration = meter.CreateHistogram<double>("card.endpoint.request.duration", unit: "ms");
    }

    private static KeyValuePair<string, object?> EndpointLabel(string value) => new("endpoint", value);

    [HttpPost(Name = "Pool")]
    public async Task<RayanResponse<CardResponse>> Pool(CardRequest request)
    {
        //_logger.LogInformation($"card is {request.CardPan.Substring(0,6)}");
        //return await Task.FromResult(await _srv.CardGetAsync(request));
        var endpointLabel = EndpointLabel("Pool");

        _endpointRequestCounter.Add(1, endpointLabel);
        var stopwatch = Stopwatch.StartNew();

        var cardBin = request.CardPan?.CardBin();
        if (!string.IsNullOrEmpty(cardBin))
        {
            _logger.LogInformation($"card is {request.CardPan.Substring(0, 6)}");
        }

        try
        {
            return await _srv.CardGetAsync(request);
        }
        finally
        {
            stopwatch.Stop();
            _endpointRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, endpointLabel);
        }
    }

    [HttpPost(Name = "Id")]
    public async Task<RayanResponse<CardResponse>> Id(CardRequest request)
    {
        //return await Task.FromResult(await _srv.CardGetByIdAsync(request));
        var endpointLabel = EndpointLabel("Id");

        _endpointRequestCounter.Add(1, endpointLabel);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await _srv.CardGetByIdAsync(request);
        }
        finally
        {
            stopwatch.Stop();
            _endpointRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, endpointLabel);
        }
    }
    [HttpPost(Name = "Data")]
    public async Task<RayanResponse<CardResponse>> Data(CardRequest request)
    {
        //return await Task.FromResult(await _srv.CardDataGetByIdAsync(request));
        var endpointLabel = EndpointLabel("Data");

        _endpointRequestCounter.Add(1, endpointLabel);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await _srv.CardDataGetByIdAsync(request);
        }
        finally
        {
            stopwatch.Stop();
            _endpointRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, endpointLabel);
        }
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
        var endpointLabel = EndpointLabel("Health");

        _endpointRequestCounter.Add(1, endpointLabel);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _srv.HealthAsync();
            return response.item;
        }
        finally
        {
            stopwatch.Stop();
            _endpointRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, endpointLabel);
        }
    }
}
