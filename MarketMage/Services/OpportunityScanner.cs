using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketMage.Models;

namespace MarketMage.Services;

public sealed record ScanScope(string SaleWorld, string DataCenter, IReadOnlySet<string> SourceWorlds);
public sealed record ScanUpdate(IReadOnlyList<uint> ItemIds, IReadOnlyList<GilOpportunity> Opportunities, int Completed, int Total);
public sealed record ScanSummary(int Evaluated, int Total, int NextOffset, string? Error);

public sealed class OpportunityScanner(IMarketDataClient market)
{
    public const int RotationSize = 100;
    public const int BatchSize = 25;

    public static uint[] PlanCandidates(IReadOnlyList<ItemCatalogEntry> catalog, IEnumerable<uint> recent,
        IEnumerable<uint> priority, int offset)
    {
        if (catalog.Count == 0) return [];
        var valid = catalog.Select(i => i.ItemId).ToHashSet();
        var rotation = Enumerable.Range(0, Math.Min(RotationSize, catalog.Count))
            .Select(i => catalog[(Math.Max(0, offset) + i) % catalog.Count].ItemId);
        return priority.Where(valid.Contains).Distinct().Take(100)
            .Concat(recent.Where(valid.Contains).Distinct().Take(200)).Concat(rotation).Distinct().ToArray();
    }

    public async Task<ScanSummary> ScanAsync(ScanScope scope, IReadOnlyList<ItemCatalogEntry> catalog,
        IReadOnlyDictionary<uint, IReadOnlyList<CraftingRecipe>> recipes, IEnumerable<uint> priority,
        int offset, OpportunitySettings settings, Action<ScanUpdate> report, CancellationToken token)
    {
        var completed = 0;
        var total = 0;
        try
        {
            var recent = await market.GetRecentItemsAsync(scope.DataCenter, token).ConfigureAwait(false);
            var ids = PlanCandidates(catalog, recent, priority, offset);
            var names = catalog.ToDictionary(i => i.ItemId);
            total = ids.Length;
            report(new ScanUpdate([], [], 0, total));
            var ingredientCache = new Dictionary<uint, MarketPriceSnapshot>();
            foreach (var batch in ids.Chunk(BatchSize))
            {
                token.ThrowIfCancellationRequested();
                var nq = (await market.GetSnapshotsAsync(scope.SaleWorld, batch, false, true, token, true).ConfigureAwait(false)).ToDictionary(s => s.ItemId);
                var hqIds = batch.Where(id => names[id].CanBeHq).ToArray();
                var hq = (await market.GetSnapshotsAsync(scope.SaleWorld, hqIds, true, true, token, true).ConfigureAwait(false)).ToDictionary(s => s.ItemId);
                var dcNq = SourceOnly(await market.GetSnapshotsAsync(scope.DataCenter, batch, false, true, token).ConfigureAwait(false), scope);
                var dcHq = SourceOnly(await market.GetSnapshotsAsync(scope.DataCenter, hqIds, true, true, token).ConfigureAwait(false), scope);
                foreach (var pair in dcNq) ingredientCache[pair.Key] = pair.Value;
                var ingredientIds = batch.Where(recipes.ContainsKey).SelectMany(id => recipes[id]).SelectMany(r => r.Ingredients)
                    .Select(i => i.ItemId).Distinct().Where(id => !ingredientCache.ContainsKey(id)).ToArray();
                foreach (var pair in SourceOnly(await market.GetSnapshotsAsync(scope.DataCenter, ingredientIds, false, true, token).ConfigureAwait(false), scope))
                    ingredientCache[pair.Key] = pair.Value;
                var rows = new List<GilOpportunity>();
                var now = DateTimeOffset.UtcNow;
                foreach (var id in batch)
                {
                    token.ThrowIfCancellationRequested();
                    var options = recipes.GetValueOrDefault(id) ?? [];
                    if (nq.TryGetValue(id, out var normal))
                        rows.AddRange(OpportunityEngine.Evaluate(names[id], false, scope.SaleWorld, normal, dcNq.GetValueOrDefault(id), options, ingredientCache, settings, now));
                    if (hq.TryGetValue(id, out var high))
                        rows.AddRange(OpportunityEngine.Evaluate(names[id], true, scope.SaleWorld, high, dcHq.GetValueOrDefault(id), options, ingredientCache, settings, now));
                }
                completed += batch.Length;
                token.ThrowIfCancellationRequested();
                report(new ScanUpdate(batch, rows, completed, total));
            }
            return new ScanSummary(completed, total, catalog.Count == 0 ? 0 : (offset + Math.Min(RotationSize, catalog.Count)) % catalog.Count, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            return new ScanSummary(completed, total, offset, "Market data unavailable. Partial results remain until they expire; automatic retry in 10 minutes.");
        }
    }

    private static Dictionary<uint, MarketPriceSnapshot> SourceOnly(IReadOnlyList<MarketPriceSnapshot> snapshots, ScanScope scope) =>
        snapshots.ToDictionary(s => s.ItemId, s => new MarketPriceSnapshot
        { ItemId = s.ItemId, Listings = s.Listings.Where(l => scope.SourceWorlds.Contains(l.World)).ToList() });
}
