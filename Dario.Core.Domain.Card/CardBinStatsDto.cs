using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
