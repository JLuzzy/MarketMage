using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MarketMage.Models;

namespace MarketMage.Services;

public sealed class UniversalisService : IDisposable
{
    private readonly HttpClient httpClient = new();
    private readonly IPluginLog log;

    public UniversalisService(IPluginLog log)
    {
        this.log = log;
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MarketMage/0.1");
    }

    public async Task<IReadOnlyList<MarketPriceSnapshot>> GetRecentSaleSnapshotsAsync(
        string world,
        IReadOnlyCollection<uint> itemIds,
        CancellationToken cancellationToken = default)
    {
        var trimmedWorld = world.Trim();
        if (string.IsNullOrWhiteSpace(trimmedWorld))
            throw new ArgumentException("World cannot be empty.", nameof(world));

        if (itemIds.Count == 0)
            return [];

        var itemIdText = string.Join(',', itemIds);
        var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(trimmedWorld)}/{itemIdText}?listings=0&entries=20";

        log.Information("Fetching Universalis data: {Url}", url);

        using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var snapshotsByItemId = new Dictionary<uint, MarketPriceSnapshot>();
        var root = document.RootElement;

        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var itemProperty in itemsElement.EnumerateObject())
            {
                if (uint.TryParse(itemProperty.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
                    snapshotsByItemId[itemId] = ParseSnapshot(itemId, itemProperty.Value);
            }
        }
        else
        {
            var itemId = TryGetItemId(root) ?? itemIds.First();
            snapshotsByItemId[itemId] = ParseSnapshot(itemId, root);
        }

        return itemIds
            .Select(itemId => snapshotsByItemId.TryGetValue(itemId, out var snapshot)
                ? snapshot
                : new MarketPriceSnapshot { ItemId = itemId })
            .ToList();
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static MarketPriceSnapshot ParseSnapshot(uint itemId, JsonElement itemElement)
    {
        if (!itemElement.TryGetProperty("recentHistory", out var historyElement) ||
            historyElement.ValueKind != JsonValueKind.Array)
        {
            return new MarketPriceSnapshot { ItemId = itemId };
        }

        var prices = new List<int>();
        DateTimeOffset? lastSaleTime = null;

        foreach (var entry in historyElement.EnumerateArray())
        {
            if (entry.TryGetProperty("pricePerUnit", out var priceElement) &&
                priceElement.TryGetInt32(out var price))
            {
                prices.Add(price);
            }

            if (entry.TryGetProperty("timestamp", out var timestampElement) &&
                timestampElement.TryGetInt64(out var timestamp))
            {
                var saleTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                if (lastSaleTime is null || saleTime > lastSaleTime)
                    lastSaleTime = saleTime;
            }
        }

        return new MarketPriceSnapshot
        {
            ItemId = itemId,
            MedianRecentSalePrice = GetMedian(prices),
            RecentSalesCount = prices.Count,
            LastSaleTime = lastSaleTime,
        };
    }

    private static uint? TryGetItemId(JsonElement itemElement)
    {
        if (itemElement.TryGetProperty("itemID", out var itemIdElement) &&
            itemIdElement.TryGetUInt32(out var itemId))
        {
            return itemId;
        }

        return null;
    }

    private static int GetMedian(List<int> prices)
    {
        if (prices.Count == 0)
            return 0;

        prices.Sort();
        var middle = prices.Count / 2;
        if (prices.Count % 2 == 1)
            return prices[middle];

        return (int)Math.Floor((prices[middle - 1] + prices[middle]) / 2m);
    }
}
