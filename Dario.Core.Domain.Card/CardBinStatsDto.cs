namespace Dario.Core.Domain.Card
{
    public class CardBinStatsDto
    {
        public string Bin { get; set; } = default!;
        public string BankName { get; set; } = default!;
        public long TodayCount { get; set; }
        public long MonthCount { get; set; }
        public long TotalCount { get; set; }
        public string LogoUrl { get; set; } = default!;
    }

}
