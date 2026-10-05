# MarketMage

MarketMage is a Dalamud dev plugin for Final Fantasy XIV that compares market-board sale history with crafting ingredient listing costs. Open it with `/marketmage`.

## Features

- Search market items by name or exact item ID; filter to craftable items.
- Save a watchlist of up to 50 items, the sale world, output quality, and filter/comparison preferences in Dalamud configuration.
- Choose public worlds grouped by region and data center from local Lumina data.
- Separate HQ and NQ output sale estimates; ingredients always use NQ listings.
- Refresh manually, with batched requests, bounded retries for throttling/server errors, and cancellation.
- Compare local ingredient cost with the cheapest sampled world in the selected data center for each ingredient.
- Show estimated revenue, local/DC profit, local ROI, sampled transaction count, and sale/upload age.
- Inspect ingredient quantity, sampled stock, source world, and listing review age.
- Changing the world, quality, comparison setting, or watchlist cancels outstanding work and clears previous results. Late responses cannot replace the new selection.

## How estimates work

**Sale price** is the median unit price of up to 20 recent transactions of the selected quality. The sample count is neither sales per day nor units sold. No usable history means no revenue or profit estimate. Revenue assumes a 5% sale tax, rounded down.

**Ingredient cost** consumes the cheapest sampled NQ listing quantities until one craft's requirements are covered. A world with insufficient sampled stock is excluded. The API request retrieves up to 100 listings per ingredient and scope; this is not an exhaustive market scan. Per-output material cost divides recipe cost by its yield and rounds up. Missing ingredients withhold the corresponding profit estimate.

**DC cost** chooses the cheapest sufficient sampled world separately for each ingredient, also considering the local-world listings fetched during the refresh. The basket may require multiple world visits; it is not the cheapest single-world basket. Sale revenue always comes from the selected sale world.

Costs are partial-stack material values. You may need to buy whole stacks, so checkout outlay can be higher. Purchase taxes and travel costs are excluded. Sale/upload and listing-review ages are shown; ages over 24 hours are marked stale, and missing timestamps remain unknown. Stale data is not automatically excluded. Universalis is crowdsourced, so estimates are not guaranteed executable prices.

## Build and checks

Prerequisites:

- .NET 10 SDK (the project uses `Dalamud.NET.Sdk/15.0.0`).
- XIVLauncher/Dalamud installed and run at least once, with compatible Dalamud assemblies. For a custom installation, set `DALAMUD_HOME` to the assembly directory.

```powershell
dotnet build MarketMage.sln -c Debug -p:Platform=x64 -p:RestoreLockedMode=true
dotnet run --project MarketMage.Tests -c Release
```

The offline regression executable exits nonzero on failure and needs no game installation. It covers API parsing, quality separation, quantity-aware costs, arithmetic, cancellation, request batching, and error handling. GitHub Actions runs these checks, builds on Windows, and uploads the DLL/manifest.

Debug DLL: `MarketMage/bin/x64/Debug/MarketMage.dll`.

## Load in game

1. Open `/xlsettings` and go to `Experimental`.
2. Add the full DLL path under Dev Plugin Locations.
3. Open `/xlplugins` → `Dev Tools` → `Installed Dev Plugins`.
4. Enable MarketMage and run `/marketmage`.

### In-game acceptance checks

These require an actual FFXIV/Dalamud session; compilation and offline tests do not establish that they passed.

- Open/close the window, choose worlds under different data centers, and verify the selected sale world in results.
- Select a craft with multiple ingredients and a multi-item yield; compare displayed arithmetic with its recipe and sampled listings.
- Switch HQ/NQ and confirm old results clear before a new refresh.
- Change worlds, cancel, clear, or unload while refreshing; verify no late results or ImGui errors appear.
- Disable DC comparison and confirm local estimates still work.
- Reload the plugin and verify watchlist/world/preferences persist.
- Check missing/insufficient stock, no-sale history, stale timestamps, and connectivity errors.
- Resize the window and inspect all table columns and the scrollable ingredient panel.

## Limitations and later work

- Advisory only: no buying, selling, undercutting, crafting, gathering, retainer, movement, or game-server automation.
- Uses the first matching recipe for an output; alternate-recipe selection is not implemented.
- No recursive subcraft costing, vendor pricing, inventory awareness, gathering effort, desynthesis, or alerts.
- No automatic background price polling or persistent market cache.
- Catalog displays the first 250 matches and explicitly reports this limit; narrow the search to find additional items.
- No plugin repository submission or distribution setup yet.

## Data sources

- [Universalis API](https://docs.universalis.app/) for sale history and listings.
- Dalamud/Lumina game data for items, public worlds, data centers, and recipes.
