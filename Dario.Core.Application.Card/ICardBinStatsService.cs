using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dario.Core.Application.Card
{
    public interface ICardBinStatsService
    {
        Task IncrementAsync(string bin, CancellationToken cancellationToken = default);
    }

}
