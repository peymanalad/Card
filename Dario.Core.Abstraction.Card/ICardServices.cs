using Dario.Core.Domain.Card;
using Rayanparsi.Core.Domain.Entities;
namespace Dario.Core.Abstraction.Card;
public interface ICardServices
{
    Task<RayanResponse<CardResponse>> CardGetAsync(CardRequest request);
    Task<RayanResponse<CardResponse>> CardGetByIdAsync(CardRequest request);
    Task<RayanResponse<CardResponse>> CardDataGetByIdAsync(CardRequest request);
    Task<RayanResponse<bool>> HealthAsync();
}
