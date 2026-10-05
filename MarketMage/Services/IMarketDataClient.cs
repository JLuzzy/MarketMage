using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketMage.Models;

namespace MarketMage.Services;

public interface IMarketDataClient
{
    Task<IReadOnlyList<uint>> GetRecentItemsAsync(string dataCenter, CancellationToken token);
    Task<IReadOnlyList<MarketPriceSnapshot>> GetSnapshotsAsync(string scope, IReadOnlyCollection<uint> itemIds,
        bool hq, bool listings, CancellationToken cancellationToken, bool includeHistory = false);
}
