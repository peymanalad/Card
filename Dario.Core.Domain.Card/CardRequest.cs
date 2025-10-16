using System.Numerics;

namespace Dario.Core.Domain.Card;
public class CardRequest
{
    public long CardId { get; set; }
    public string CardPan { get; set; } = String.Empty;
    public string CardExDate { get; set; } = String.Empty;
}
public class CardRRBRequest
{
    public string RRN { get; set; } = String.Empty;
    public string RRNC { get; set; } = String.Empty;
}
public class CardRRBZRequest
{
    public string RRN { get; set; } = String.Empty;
    public long Id { get; set; } =0;
}