using System.Collections.Generic;

namespace MarketMage.Models;

public sealed class CraftingRecipe
{
    public uint ResultItemId { get; init; }
    public int AmountResult { get; init; } = 1;
    public IReadOnlyList<RecipeIngredient> Ingredients { get; init; } = [];
}
