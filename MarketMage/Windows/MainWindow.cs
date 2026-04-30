using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using MarketMage.Models;
using MarketMage.Services;

namespace MarketMage.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private const int MaxRefreshItemCount = 50;
    private static readonly string[] LodestoneWorldNames =
    [
        "Adamantoise",
        "Cactuar",
        "Faerie",
        "Gilgamesh",
        "Jenova",
        "Midgardsormr",
        "Sargatanas",
        "Siren",
        "Balmung",
        "Brynhildr",
        "Coeurl",
        "Diabolos",
        "Goblin",
        "Malboro",
        "Mateus",
        "Zalera",
        "Cuchulainn",
        "Golem",
        "Halicarnassus",
        "Kraken",
        "Maduin",
        "Marilith",
        "Rafflesia",
        "Seraph",
        "Behemoth",
        "Excalibur",
        "Exodus",
        "Famfrit",
        "Hyperion",
        "Lamia",
        "Leviathan",
        "Ultros",
        "Cerberus",
        "Louisoix",
        "Moogle",
        "Omega",
        "Phantom",
        "Ragnarok",
        "Sagittarius",
        "Spriggan",
        "Alpha",
        "Lich",
        "Odin",
        "Phoenix",
        "Raiden",
        "Shiva",
        "Twintania",
        "Zodiark",
        "Bismarck",
        "Ravana",
        "Sephirot",
        "Sophia",
        "Zurvan",
        "Aegis",
        "Atomos",
        "Carbuncle",
        "Garuda",
        "Gungnir",
        "Kujata",
        "Tonberry",
        "Typhon",
        "Alexander",
        "Bahamut",
        "Durandal",
        "Fenrir",
        "Ifrit",
        "Ridill",
        "Tiamat",
        "Ultima",
        "Anima",
        "Asura",
        "Chocobo",
        "Hades",
        "Ixion",
        "Masamune",
        "Pandaemonium",
        "Titan",
        "Belias",
        "Mandragora",
        "Ramuh",
        "Shinryu",
        "Unicorn",
        "Valefor",
        "Yojimbo",
        "Zeromus",
    ];

    private readonly UniversalisService universalisService;
    private readonly ProfitService profitService;
    private readonly RecipeService recipeService;
    private readonly IPluginLog log;
    private readonly IReadOnlyList<ItemCatalogEntry> itemCatalog;
    private readonly IReadOnlyList<string> worldNames;
    private readonly HashSet<uint> selectedItemIds = [];

    private string selectedWorld = "Cactuar";
    private string searchText = string.Empty;
    private string statusText = "Ready.";
    private bool isLoading;
    private bool sortCatalogById;
    private uint? selectedEstimateItemId;
    private IReadOnlyList<ProfitEstimate> estimates = [];

    public MainWindow(IDataManager dataManager, IPluginLog log)
        : base("MarketMage")
    {
        this.log = log;
        universalisService = new UniversalisService(log);
        profitService = new ProfitService(dataManager);
        recipeService = new RecipeService(dataManager);
        itemCatalog = profitService.GetItemCatalog();
        worldNames = GetWorldNames(dataManager);
        statusText = $"Loaded {itemCatalog.Count:N0} items from Lumina.";

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(980, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
        universalisService.Dispose();
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("World");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        DrawWorldCombo();

        ImGui.SameLine();
        ImGui.TextUnformatted("Search");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(360);
        ImGui.InputText("##itemSearch", ref searchText, 128);

        ImGui.SameLine();
        ImGui.Checkbox("Sort by ID", ref sortCatalogById);

        ImGui.SameLine();
        if (isLoading)
            ImGui.BeginDisabled();

        if (ImGui.Button("Refresh"))
            _ = RefreshAsync();

        if (isLoading)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            selectedItemIds.Clear();
            estimates = [];
        }

        ImGui.Spacing();
        ImGui.TextUnformatted($"{statusText} Selected: {selectedItemIds.Count:N0}.");
        ImGui.Spacing();

        var contentHeight = ImGui.GetContentRegionAvail().Y;
        var catalogHeight = Math.Max(170, contentHeight * 0.42f);
        DrawCatalogTable(catalogHeight);

        ImGui.Spacing();
        DrawResultsTable(Math.Max(170, ImGui.GetContentRegionAvail().Y * 0.58f));
        ImGui.Spacing();
        DrawEstimateDetails();
    }

    private async Task RefreshAsync()
    {
        if (isLoading)
            return;

        isLoading = true;
        statusText = $"Refreshing {selectedWorld}...";

        try
        {
            var itemIds = selectedItemIds.OrderBy(itemId => itemId).ToList();
            if (itemIds.Count == 0)
                throw new InvalidOperationException("Select at least one item first.");

            if (itemIds.Count > MaxRefreshItemCount)
                throw new InvalidOperationException($"Select {MaxRefreshItemCount} or fewer items for one refresh.");

            var saleSnapshots = await universalisService.GetRecentSaleSnapshotsAsync(selectedWorld, itemIds).ConfigureAwait(false);
            var recipes = recipeService.GetRecipesForItems(itemIds);
            var ingredientIds = recipes.Values
                .SelectMany(recipe => recipe.Ingredients)
                .Select(ingredient => ingredient.ItemId)
                .Distinct()
                .ToList();
            var ingredientSnapshots = ingredientIds.Count == 0
                ? []
                : await universalisService.GetRecentSaleSnapshotsAsync(selectedWorld, ingredientIds).ConfigureAwait(false);
            var ingredientPrices = ingredientSnapshots.ToDictionary(snapshot => snapshot.ItemId);

            estimates = profitService.BuildEstimates(saleSnapshots, recipes, ingredientPrices);
            selectedEstimateItemId ??= estimates.FirstOrDefault()?.ItemId;

            var incompleteCount = estimates.Count(estimate => estimate.IsCraftable && !estimate.HasCompleteCost);
            statusText = incompleteCount == 0
                ? $"Loaded {estimates.Count} items and {ingredientPrices.Count} ingredient prices from Universalis."
                : $"Loaded {estimates.Count} items. {incompleteCount} craft costs are incomplete.";
        }
        catch (Exception ex)
        {
            statusText = $"Refresh failed: {ex.Message}";
            log.Error(ex, "MarketMage refresh failed.");
        }
        finally
        {
            isLoading = false;
        }
    }

    private void DrawCatalogTable(float height)
    {
        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##marketmage-catalog", 3, tableFlags, new Vector2(0, height)))
            return;

        ImGui.TableSetupColumn("Use");
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("ID");
        ImGui.TableHeadersRow();

        foreach (var item in GetFilteredCatalog().Take(250))
        {
            var selected = selectedItemIds.Contains(item.ItemId);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Checkbox($"##select-{item.ItemId}", ref selected))
            {
                if (selected)
                    selectedItemIds.Add(item.ItemId);
                else
                    selectedItemIds.Remove(item.ItemId);
            }

            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{item.Name}##item-{item.ItemId}", selected, ImGuiSelectableFlags.SpanAllColumns))
            {
                if (!selectedItemIds.Remove(item.ItemId))
                    selectedItemIds.Add(item.ItemId);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.ItemId.ToString(CultureInfo.InvariantCulture));
        }

        ImGui.EndTable();
    }

    private void DrawWorldCombo()
    {
        if (!ImGui.BeginCombo("##world", selectedWorld))
            return;

        foreach (var worldName in worldNames)
        {
            var isSelected = selectedWorld == worldName;
            if (ImGui.Selectable(worldName, isSelected))
                selectedWorld = worldName;

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawResultsTable(float height)
    {
        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##marketmage-results", 8, tableFlags, new Vector2(0, height)))
            return;

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Sale");
        ImGui.TableSetupColumn("Cost");
        ImGui.TableSetupColumn("Revenue");
        ImGui.TableSetupColumn("Profit");
        ImGui.TableSetupColumn("ROI");
        ImGui.TableSetupColumn("Sales");
        ImGui.TableSetupColumn("Last Sale");
        ImGui.TableHeadersRow();

        foreach (var estimate in estimates)
        {
            var selected = selectedEstimateItemId == estimate.ItemId;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{estimate.ItemName}##result-{estimate.ItemId}", selected, ImGuiSelectableFlags.SpanAllColumns))
                selectedEstimateItemId = estimate.ItemId;

            DrawCell(estimate.EstimatedSalePrice.ToString("N0"));
            DrawCell(GetCostText(estimate));
            if (estimate.IsCraftable && !estimate.HasCompleteCost && estimate.MissingIngredientNames.Count > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Missing prices: {string.Join(", ", estimate.MissingIngredientNames)}");

            DrawCell(estimate.AdjustedRevenue?.ToString("N0") ?? "N/A");
            DrawCell(estimate.Profit?.ToString("N0") ?? "N/A");
            DrawCell(estimate.Roi?.ToString("P0") ?? "N/A");
            DrawCell(estimate.RecentSalesCount.ToString("N0"));
            DrawCell(estimate.LastSaleTime?.LocalDateTime.ToString("g") ?? "None");
        }

        ImGui.EndTable();
    }

    private static void DrawCell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }

    private void DrawEstimateDetails()
    {
        var estimate = estimates.FirstOrDefault(estimate => estimate.ItemId == selectedEstimateItemId) ?? estimates.FirstOrDefault();
        if (estimate is null)
            return;

        ImGui.Separator();
        ImGui.TextUnformatted("Recipe");
        ImGui.TextUnformatted($"{estimate.ItemName} ({estimate.ItemId})");

        if (!estimate.IsCraftable)
        {
            ImGui.TextUnformatted("Not craftable.");
            return;
        }

        ImGui.TextUnformatted($"Yield: {estimate.RecipeYield:N0}");

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##marketmage-ingredients", 5, tableFlags))
            return;

        ImGui.TableSetupColumn("Ingredient");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Unit");
        ImGui.TableSetupColumn("Total");
        ImGui.TableSetupColumn("Status");
        ImGui.TableHeadersRow();

        foreach (var ingredient in estimate.IngredientCosts)
        {
            ImGui.TableNextRow();
            DrawCell(ingredient.ItemName);
            DrawCell(ingredient.Quantity.ToString("N0"));
            DrawCell(ingredient.HasPrice ? ingredient.UnitPrice.ToString("N0") : "N/A");
            DrawCell(ingredient.HasPrice ? ingredient.TotalPrice.ToString("N0") : "N/A");
            DrawCell(ingredient.HasPrice ? "OK" : "Missing");
        }

        ImGui.EndTable();
    }

    private static string GetCostText(ProfitEstimate estimate)
    {
        if (!estimate.IsCraftable)
            return "N/A";

        if (!estimate.HasCompleteCost)
            return "Incomplete";

        return estimate.EstimatedMaterialCost?.ToString("N0") ?? "N/A";
    }

    private IEnumerable<ItemCatalogEntry> GetFilteredCatalog()
    {
        var query = searchText.Trim();
        var filteredItems = string.IsNullOrWhiteSpace(query)
            ? itemCatalog
            : itemCatalog.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        return sortCatalogById
            ? filteredItems.OrderBy(item => item.ItemId)
            : filteredItems.OrderBy(item => item.Name).ThenBy(item => item.ItemId);
    }

    private static IReadOnlyList<string> GetWorldNames(IDataManager dataManager)
    {
        var availableWorldNames = dataManager.GetExcelSheet<World>()
            .Select(world => world.Name.ToString())
            .Where(worldName => !string.IsNullOrWhiteSpace(worldName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var worldNames = LodestoneWorldNames
            .Where(availableWorldNames.Contains)
            .ToList();

        return worldNames.Count == 0 ? ["Cactuar"] : worldNames;
    }
}
