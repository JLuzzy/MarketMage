namespace MarketMage.Models;

public sealed class IngredientCostEstimate
{
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int UnitPrice { get; init; }
    public int TotalPrice => Quantity * UnitPrice;
    public bool HasPrice { get; init; }
}
