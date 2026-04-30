using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using MarketMage.Models;

namespace MarketMage.Services;

public sealed class RecipeService
{
    private readonly IDataManager dataManager;

    public RecipeService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyDictionary<uint, CraftingRecipe> GetRecipesForItems(IEnumerable<uint> itemIds)
    {
        var wantedItemIds = itemIds.ToHashSet();
        if (wantedItemIds.Count == 0)
            return new Dictionary<uint, CraftingRecipe>();

        var recipes = new Dictionary<uint, CraftingRecipe>();
        foreach (var recipe in dataManager.GetExcelSheet<Recipe>())
        {
            var resultItemId = recipe.ItemResult.RowId;
            if (resultItemId == 0 || !wantedItemIds.Contains(resultItemId) || recipes.ContainsKey(resultItemId))
                continue;

            recipes[resultItemId] = BuildRecipe(recipe);
        }

        return recipes;
    }

    private CraftingRecipe BuildRecipe(Recipe recipe)
    {
        return new CraftingRecipe
        {
            ResultItemId = recipe.ItemResult.RowId,
            AmountResult = recipe.AmountResult <= 0 ? 1 : recipe.AmountResult,
            Ingredients = GetIngredients(recipe),
        };
    }

    private IReadOnlyList<RecipeIngredient> GetIngredients(Recipe recipe)
    {
        var ingredients = new List<RecipeIngredient>();

        for (var index = 0; index < recipe.Ingredient.Count; index++)
            AddIngredient(ingredients, recipe.Ingredient[index].RowId, recipe.AmountIngredient[index]);

        return ingredients;
    }

    private void AddIngredient(List<RecipeIngredient> ingredients, uint itemId, int quantity)
    {
        if (itemId == 0 || quantity <= 0)
            return;

        ingredients.Add(new RecipeIngredient
        {
            ItemId = itemId,
            ItemName = GetItemName(itemId),
            Quantity = quantity,
        });
    }

    private string GetItemName(uint itemId)
    {
        return dataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item)
            ? item.Name.ToString()
            : $"Item {itemId}";
    }
}
