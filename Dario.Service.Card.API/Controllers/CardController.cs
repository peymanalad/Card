using Dario.Core.Abstraction.Card;
using Dario.Core.Domain.Card;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rayanparsi.Core.Domain.Entities;

namespace Dario.Service.Card.API.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
public class CardController : ControllerBase
{
    private readonly ILogger<CardController> _logger;
    private readonly ICardServices _srv;

    public CardController(ILogger<CardController> logger, ICardServices srv)
    {
        _logger = logger;
        _srv = srv;
    }
    [HttpPost(Name = "Pool")]
    public async Task<RayanResponse<CardResponse>> Pool(CardRequest request)
    {
        _logger.LogInformation($"card is {request.CardPan.Substring(0,6)}");
        return await Task.FromResult(await _srv.CardGetAsync(request));
    }

    [HttpPost(Name = "Id")]
    public async Task<RayanResponse<CardResponse>> Id(CardRequest request)
    {
        return await Task.FromResult(await _srv.CardGetByIdAsync(request));
    }
    [HttpPost(Name = "Data")]
    public async Task<RayanResponse<CardResponse>> Data(CardRequest request)
    {
        return await Task.FromResult(await _srv.CardDataGetByIdAsync(request));
    }
    [HttpGet(Name ="Clear")]
    public bool Clear()
    {
        GC.Collect(2);
        return true;
    }
    [HttpGet(Name = "Health")]
    public bool Health()
    {
        return  (_srv.HealthAsync().Result.item);
    }
}
