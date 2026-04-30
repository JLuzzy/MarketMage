namespace MarketMage.Models;

public sealed class RecipeIngredient
{
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; init; }
}
