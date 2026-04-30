using System;

namespace MarketMage.Models;

public sealed class MarketPriceSnapshot
{
    public uint ItemId { get; init; }
    public int MedianRecentSalePrice { get; init; }
    public int RecentSalesCount { get; init; }
    public DateTimeOffset? LastSaleTime { get; init; }
}
