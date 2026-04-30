# MarketMage

MarketMage is a Dalamud dev plugin for Final Fantasy XIV that helps compare market-board sale prices against simple crafting material costs.

Current prototype features:

- Opens with `/marketmage`.
- Loads searchable item names from Lumina.
- Uses a curated public-world dropdown for NA, EU, Oceania, and Japan worlds.
- Fetches recent sale history from Universalis only when the user clicks Refresh.
- Finds local craft recipes through Lumina.
- Estimates non-recursive crafting material cost from market-board ingredient prices.
- Shows sale price, post-tax revenue, material cost, profit, ROI, recent sales, and recipe ingredient details.

## Limitations

- No buying, selling, undercutting, crafting, gathering, retainer, movement, or game-server automation.
- No recursive subcraft costing yet.
- No vendor pricing, inventory awareness, gathering effort, alerts, packaging, or plugin repository submission files yet.
- Ingredient cost currently uses median recent Universalis sale price.

## Build

Prerequisites:

- XIVLauncher/Dalamud installed and run at least once.
- .NET SDK compatible with the Dalamud SDK used by the project.

Build Debug x64:

```powershell
dotnet build -c Debug -p:Platform=x64
```

Output DLL:

```text
MarketMage/bin/x64/Debug/MarketMage.dll
```

## Load In Game

1. Open `/xlsettings`.
2. Go to `Experimental`.
3. Add the full path to `MarketMage.dll` under Dev Plugin Locations.
4. Open `/xlplugins`.
5. Go to `Dev Tools` -> `Installed Dev Plugins`.
6. Enable MarketMage.
7. Run `/marketmage`.

## Data Sources

- Universalis API for market-board sale history.
- Dalamud/Lumina game data for item names, worlds, and recipes.
