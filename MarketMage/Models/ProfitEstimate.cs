using System;
using System.Collections.Generic;

namespace MarketMage.Models;

public sealed class ProfitEstimate
{
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int EstimatedSalePrice { get; init; }
    public bool IsCraftable { get; init; }
    public bool HasCompleteCost { get; init; }
    public int? EstimatedMaterialCost { get; init; }
    public int RecentSalesCount { get; init; }
    public DateTimeOffset? LastSaleTime { get; init; }
    public int RecipeYield { get; init; } = 1;
    public IReadOnlyList<string> MissingIngredientNames { get; init; } = [];
    public IReadOnlyList<IngredientCostEstimate> IngredientCosts { get; init; } = [];

    public int? AdjustedRevenue => EstimatedSalePrice <= 0
        ? null
        : (int)Math.Floor(EstimatedSalePrice * 0.95m);

    public int? Profit => HasCompleteCost && AdjustedRevenue.HasValue && EstimatedMaterialCost.HasValue
        ? AdjustedRevenue.Value - EstimatedMaterialCost.Value
        : null;

    public double? Roi => Profit.HasValue && EstimatedMaterialCost is > 0
        ? (double)Profit.Value / EstimatedMaterialCost.Value
        : null;

    public decimal Confidence => RecentSalesCount switch
    {
        >= 10 => 1.0m,
        >= 5 => 0.8m,
        >= 2 => 0.5m,
        _ => 0.2m,
    };
}
