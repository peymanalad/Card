namespace Dario.Core.Domain.Card;
public class CardResponse
{
    public long CardId { get; set; }
    public string CardPan { get; set; } = String.Empty;
    public string CardProductCode { get; set; } = String.Empty;
    public string CardData { get; set; } = String.Empty;
    public string CardHash { get; set; } = String.Empty;
    public string CardExDate { get; set; } = String.Empty;
    public string CardMask { get; set; } = String.Empty;
    public string CardBin { get; set; } = String.Empty;
    public string CardBinName { get; set; } = String.Empty;
    public string CardName { get; set; } = String.Empty;
    public string CardFamily { get; set; } = String.Empty;
    public string CardNationalCode { get; set; } = String.Empty;
    public string CardIban { get; set; } = String.Empty;
}
