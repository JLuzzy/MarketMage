using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MarketMage.Models;
using MarketMage.Services;

namespace MarketMage.Windows;

public sealed class OpportunitiesPanel : IDisposable
{
    private readonly IPlayerState player;
    private readonly OpportunityScanner scanner;
    private readonly IReadOnlyList<ItemCatalogEntry> catalog;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<CraftingRecipe>> recipes;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> dcWorlds;
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly LatestRequest<ScanSummary> request = new();
    private readonly ConcurrentQueue<(int Generation, ScanUpdate Update)> updates = new();
    private readonly OpportunityBook book = new();
    private readonly HashSet<uint> checkedItems = [];
    private ScanScope? scope;
    private DateTimeOffset nextScan;
    private int generation;
    private int rotation;
    private int scanned;
    private int total;
    private string status = "Log in to detect your sale world and local data center.";
    private string? selectedKey;
    private bool showCrafts = true;
    private bool showResales = true;
    private bool showHq = true;
    private string search = string.Empty;
    private bool disposed;

    public OpportunitiesPanel(IPlayerState player, UniversalisService market, IReadOnlyList<ItemCatalogEntry> catalog,
        IReadOnlyDictionary<uint, IReadOnlyList<CraftingRecipe>> recipes, IReadOnlyDictionary<string, IReadOnlySet<string>> dcWorlds, Configuration config,
        IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.player = player;
        scanner = new OpportunityScanner(market);
        this.catalog = catalog.OrderByDescending(i => i.ItemId).ToArray();
        this.recipes = recipes;
        this.dcWorlds = dcWorlds;
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.log = log;
        NormalizeSettings();
    }

    private OpportunitySettings Settings => new()
    {
        Budget = config.GilBudget, MinimumProfit = config.MinimumProfit,
        MinimumRoi = config.MinimumRoiPercent / 100d, MinimumSales = config.MinimumSales,
        MaximumDataAge = TimeSpan.FromHours(config.MaximumAgeHours),
    };

    // Called by Dalamud's framework update. Worker tasks only enqueue immutable results.
    public void Update(bool visible)
    {
        if (disposed) return;
        if (!player.IsLoaded || player.HomeWorld.RowId == 0 || player.CurrentWorld.RowId == 0)
        {
            Suspend();
            scope = null;
            book.Clear();
            checkedItems.Clear();
            rotation = 0;
            status = "Log in to detect your sale world and local data center.";
            return;
        }
        var home = player.HomeWorld.Value.Name.ToString();
        var dc = player.CurrentWorld.Value.DataCenter.Value.Name.ToString();
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(dc)) { Suspend(); scope = null; return; }
        if (scope?.SaleWorld != home || scope.DataCenter != dc)
        {
            Suspend();
            book.Clear(); checkedItems.Clear(); rotation = 0;
            // Only public worlds in the player's current DC can be buying sources.
            if (!dcWorlds.TryGetValue(dc, out var worlds))
            {
                scope = null;
                status = "The current data center has no supported public worlds.";
                return;
            }
            scope = new ScanScope(home, dc, worlds);
            nextScan = DateTimeOffset.MinValue;
            status = "Ready to discover crafting and resale opportunities.";
        }
        if (!visible) { Suspend(); return; }
        var finished = request.TryTake(out var summary);
        while (updates.TryDequeue(out var queued))
        {
            if (queued.Generation != generation) continue;
            book.Apply(queued.Update);
            checkedItems.UnionWith(queued.Update.ItemIds);
            scanned = queued.Update.Completed;
            total = queued.Update.Total;
            status = $"Scanning {scanned}/{total} candidates; results appear as each batch finishes.";
        }
        if (finished)
        {
            rotation = summary!.NextOffset;
            nextScan = DateTimeOffset.UtcNow.AddMinutes(10);
            status = summary.Error ?? $"Round complete: checked {summary.Evaluated} candidates. Opportunities are ranked by estimated net gil per plan.";
        }
        if (config.AutoScan && !request.IsRunning && DateTimeOffset.UtcNow >= nextScan) Start();
    }

    public void Suspend()
    {
        if (request.IsRunning)
        {
            request.Cancel();
            generation++;
            nextScan = DateTimeOffset.UtcNow;
            status = "Scan paused. Existing findings expire after 15 minutes.";
        }
        while (updates.TryDequeue(out _)) { }
    }

    private void Start()
    {
        if (scope == null || request.IsRunning) return;
        var activeScope = scope;
        var activeSettings = Settings;
        var activeGeneration = ++generation;
        var offset = rotation;
        var priority = book.Current(DateTimeOffset.UtcNow, activeSettings).Select(r => r.ItemId)
            .Concat(config.SelectedItems ?? []).Distinct().ToArray();
        scanned = total = 0;
        status = "Finding recently updated markets in your data center...";
        request.Start(token => Task.Run(async () =>
        {
            try
            {
                return await scanner.ScanAsync(activeScope, catalog, recipes, priority, offset, activeSettings,
                    update => updates.Enqueue((activeGeneration, update)), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                log.Error(ex, "Automatic opportunity scan failed.");
                return new ScanSummary(0, 0, offset, "Scan failed; details are in the plugin log. Retry in 10 minutes.");
            }
        }, token));
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Find ways to make gil");
        if (scope == null) { ImGui.TextWrapped(status); return; }
        ImGui.TextWrapped($"Sell on {scope.SaleWorld} (home world) | Buy on {scope.DataCenter} (current data center)");
        var auto = config.AutoScan;
        if (ImGui.Checkbox("Automatic scanning while this view is open", ref auto))
        {
            config.AutoScan = auto;
            if (!auto) Suspend();
            Save();
        }
        ImGui.SameLine();
        if (request.IsRunning)
        {
            if (ImGui.Button("Pause scan")) { config.AutoScan = false; Suspend(); Save(); }
        }
        else if (ImGui.Button("Scan now")) Start();
        if (ImGui.CollapsingHeader("Budget and opportunity filters")) DrawSettings();
        ImGui.TextWrapped(status);
        if (request.IsRunning && total > 0) ImGui.ProgressBar(scanned / (float)total, new Vector2(-1, 0), $"{scanned}/{total}");
        else if (config.AutoScan && nextScan > DateTimeOffset.UtcNow)
            ImGui.TextDisabled($"Next round in {Math.Ceiling((nextScan - DateTimeOffset.UtcNow).TotalMinutes):N0} minutes.");
        ImGui.TextWrapped($"Coverage: {checkedItems.Count:N0}/{catalog.Count:N0} catalog items checked this session. Results cover sampled markets.");
        if (ImGui.CollapsingHeader("How these opportunities are calculated"))
        {
            ImGui.TextWrapped("Each round prioritizes recently updated markets and rotates through the wider catalog. The search is not an exhaustive live-market scan.");
            ImGui.TextWrapped("Profit includes full-stack purchases and assumed 5% buying/selling taxes. Sale prices are capped below the current lowest home-world listing. Travel and crafting time are excluded.");
        }
        ImGui.Checkbox("Crafting", ref showCrafts); ImGui.SameLine();
        ImGui.Checkbox("Resale", ref showResales); ImGui.SameLine();
        ImGui.Checkbox("Include HQ", ref showHq); ImGui.SameLine();
        ImGui.SetNextItemWidth(220); ImGui.InputText("Filter items", ref search, 100);
        var rows = book.Current(DateTimeOffset.UtcNow, Settings)
            .Where(r => (showCrafts || r.Kind != OpportunityKind.Craft) && (showResales || r.Kind != OpportunityKind.Resell) &&
                (showHq || !r.HighQuality) && r.ItemName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        ImGui.TextUnformatted($"{rows.Count:N0} qualifying opportunities (showing top 100). Budget applies separately to each plan.");
        if (rows.Count == 0)
        {
            ImGui.TextWrapped(request.IsRunning ? "Looking for opportunities that meet your filters..." :
                "No qualifying opportunities in the checked markets. Try the next scan or adjust your budget, minimum profit, or ROI. Missing/stale markets and insufficient sales evidence are excluded.");
            return;
        }
        if (ImGui.BeginTable("gil-opportunities", 9, TableFlags, new Vector2(0, Math.Max(160, ImGui.GetContentRegionAvail().Y * 0.45f))))
        {
            Headers("Item / quality", "Method", "Sell qty", "Buy cost", "Net gil", "ROI", "Buy worlds", "7d sample", "Checked");
            foreach (var row in rows.Take(100))
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                if (ImGui.Selectable($"{row.ItemName} ({(row.HighQuality ? "HQ" : "NQ")})##{row.Key}", selectedKey == row.Key)) selectedKey = row.Key;
                Cell(row.Kind == OpportunityKind.Craft ? $"Craft x{row.CraftCount}" : "Buy / resell");
                Cell(row.OutputQuantity.ToString("N0")); Cell(row.Outlay.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1), $"+{row.Profit:N0}");
                Cell(row.Roi.ToString("P0")); Cell(string.Join(", ", row.Purchases.Select(p => p.World).Distinct()));
                Cell($"{row.SampleSales} sales / {row.SampleUnits:N0} units");
                Cell($"{Math.Max(0, (DateTimeOffset.UtcNow - row.CheckedAt).TotalMinutes):F0}m ago");
            }
            ImGui.EndTable();
        }
        var detail = rows.FirstOrDefault(r => r.Key == selectedKey) ?? rows[0];
        DrawPlan(detail);
    }

    private void DrawSettings()
    {
        var budget = config.GilBudget; var profit = config.MinimumProfit; var roi = config.MinimumRoiPercent;
        var sales = config.MinimumSales; var age = config.MaximumAgeHours;
        ImGui.SetNextItemWidth(150); var changed = ImGui.InputInt("Gil budget per plan", ref budget, 1000, 10000);
        ImGui.SetNextItemWidth(150); changed |= ImGui.InputInt("Minimum net gil", ref profit, 100, 1000);
        ImGui.SetNextItemWidth(150); changed |= ImGui.InputInt("Minimum ROI %", ref roi);
        ImGui.SetNextItemWidth(150); changed |= ImGui.InputInt("Minimum sampled sales in 7 days", ref sales);
        ImGui.SetNextItemWidth(150); changed |= ImGui.InputInt("Maximum data age (hours)", ref age);
        if (!changed) return;
        config.GilBudget = budget; config.MinimumProfit = profit; config.MinimumRoiPercent = roi;
        config.MinimumSales = sales; config.MaximumAgeHours = age;
        NormalizeSettings(); Suspend(); book.Clear(); nextScan = DateTimeOffset.UtcNow.AddSeconds(2); Save();
    }

    private static void DrawPlan(GilOpportunity row)
    {
        ImGui.Separator();
        ImGui.TextWrapped($"{row.ItemName}: sell {row.OutputQuantity:N0} {(row.HighQuality ? "HQ" : "NQ")} at an estimated {row.SalePrice:N0} gil each on {row.SaleWorld}.");
        if (row.Kind == OpportunityKind.Craft)
            ImGui.TextWrapped($"Craft {row.CraftCount} times using recipe {row.RecipeId} ({row.CraftJob}, level {row.CraftLevel}). Buy the stacks below; leftover materials are valued at zero in this plan. Check recipe access and gear; HQ plans require HQ outputs.");
        else ImGui.TextWrapped("Buy the listed stack below, then list it on your home world. The estimated profit assumes all units sell at the displayed price.");
        ImGui.TextWrapped($"Revenue after sale tax: {row.Revenue:N0} − full purchase cost: {row.Outlay:N0} = estimated net gil: {row.Profit:N0}. Last sampled sale: {row.LastSale.LocalDateTime:g}; oldest source: {row.OldestMarketData.LocalDateTime:g}.");
        ImGui.TextWrapped("The 7-day sample is up to 20 recent transactions, not total market volume or a sell-through forecast. Recheck listings before buying; different plans can compete for the same stock.");
        if (!ImGui.BeginTable("shopping-plan", 5, TableFlags, new Vector2(0, 150))) return;
        Headers("Buy item", "World", "Whole stack qty", "Unit price", "Cost incl. tax");
        foreach (var step in row.Purchases)
        {
            ImGui.TableNextRow(); Cell(step.ItemName); Cell(step.World); Cell(step.Quantity.ToString("N0"));
            Cell(step.UnitPrice.ToString("N0")); Cell(step.CostWithTax.ToString("N0"));
        }
        ImGui.EndTable();
    }

    private void NormalizeSettings()
    {
        config.GilBudget = Math.Clamp(config.GilBudget, 1, 999_999_999);
        config.MinimumProfit = Math.Clamp(config.MinimumProfit, 1, 999_999_999);
        config.MinimumRoiPercent = Math.Clamp(config.MinimumRoiPercent, 0, 10000);
        config.MinimumSales = Math.Clamp(config.MinimumSales, 1, 20);
        config.MaximumAgeHours = Math.Clamp(config.MaximumAgeHours, 1, 168);
    }
    private void Save() => pluginInterface.SavePluginConfig(config);
    public void Dispose() { disposed = true; Suspend(); request.Dispose(); }
    private const ImGuiTableFlags TableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
    private static void Headers(params string[] names) { foreach (var name in names) ImGui.TableSetupColumn(name); ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow(); }
    private static void Cell(string value) { ImGui.TableNextColumn(); ImGui.TextUnformatted(value); }
}
