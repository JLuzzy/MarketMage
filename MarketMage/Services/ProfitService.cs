using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using MarketMage.Models;

namespace MarketMage.Services;

public sealed class ProfitService
{
    private readonly IDataManager dataManager;

    public ProfitService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyList<ItemCatalogEntry> GetItemCatalog()
    {
        return dataManager.GetExcelSheet<Item>()
            .Where(item => item.RowId > 0)
            .Where(item => item.ItemSearchCategory.RowId > 0)
            .Select(item => new ItemCatalogEntry
            {
                ItemId = item.RowId,
                Name = item.Name.ToString(),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.ItemId)
            .ToList();
    }

    public IReadOnlyList<ProfitEstimate> BuildEstimates(
        IEnumerable<MarketPriceSnapshot> snapshots,
        IReadOnlyDictionary<uint, CraftingRecipe> recipes,
        IReadOnlyDictionary<uint, MarketPriceSnapshot> ingredientPrices)
    {
        return snapshots
            .Select(snapshot => BuildEstimate(snapshot, recipes, ingredientPrices))
            .OrderByDescending(estimate => estimate.Profit ?? int.MinValue)
            .ToList();
    }

    private ProfitEstimate BuildEstimate(
        MarketPriceSnapshot snapshot,
        IReadOnlyDictionary<uint, CraftingRecipe> recipes,
        IReadOnlyDictionary<uint, MarketPriceSnapshot> ingredientPrices)
    {
        if (!recipes.TryGetValue(snapshot.ItemId, out var recipe))
        {
            return new ProfitEstimate
            {
                ItemId = snapshot.ItemId,
                ItemName = GetItemName(snapshot.ItemId),
                EstimatedSalePrice = snapshot.MedianRecentSalePrice,
                IsCraftable = false,
                HasCompleteCost = false,
                RecentSalesCount = snapshot.RecentSalesCount,
                LastSaleTime = snapshot.LastSaleTime,
            };
        }

        var ingredientCosts = recipe.Ingredients
            .Select(ingredient => BuildIngredientCost(ingredient, ingredientPrices))
            .ToList();
        var missingIngredientNames = ingredientCosts
            .Where(ingredient => !ingredient.HasPrice)
            .Select(ingredient => ingredient.ItemName)
            .ToList();
        var hasCompleteCost = missingIngredientNames.Count == 0;
        var totalMaterialCost = ingredientCosts.Sum(ingredient => ingredient.TotalPrice);
        int? costPerOutput = hasCompleteCost
            ? (int)Math.Ceiling(totalMaterialCost / (decimal)Math.Max(1, recipe.AmountResult))
            : null;

        return new ProfitEstimate
        {
            ItemId = snapshot.ItemId,
            ItemName = GetItemName(snapshot.ItemId),
            EstimatedSalePrice = snapshot.MedianRecentSalePrice,
            IsCraftable = true,
            HasCompleteCost = hasCompleteCost,
            EstimatedMaterialCost = costPerOutput,
            RecentSalesCount = snapshot.RecentSalesCount,
            LastSaleTime = snapshot.LastSaleTime,
            RecipeYield = recipe.AmountResult,
            MissingIngredientNames = missingIngredientNames,
            IngredientCosts = ingredientCosts,
        };
    }

    private static IngredientCostEstimate BuildIngredientCost(
        RecipeIngredient ingredient,
        IReadOnlyDictionary<uint, MarketPriceSnapshot> ingredientPrices)
    {
        var hasPrice = ingredientPrices.TryGetValue(ingredient.ItemId, out var priceSnapshot) &&
                       priceSnapshot.MedianRecentSalePrice > 0 &&
                       priceSnapshot.RecentSalesCount > 0;

        return new IngredientCostEstimate
        {
            ItemId = ingredient.ItemId,
            ItemName = ingredient.ItemName,
            Quantity = ingredient.Quantity,
            UnitPrice = hasPrice ? priceSnapshot!.MedianRecentSalePrice : 0,
            HasPrice = hasPrice,
        };
    }

    private string GetItemName(uint itemId)
    {
        return dataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item)
            ? item.Name.ToString()
            : $"Item {itemId}";
    }

}
