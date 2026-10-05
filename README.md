# MarketMage

MarketMage is a Dalamud plugin that finds potential ways to make gil in Final Fantasy XIV. Open `/marketmage` while logged in: the default **Find gil opportunities** view starts scanning automatically, without selecting items first.

## Automatic gil discovery

- Detects your **home world for selling** and **current data center for buying**. If you visit another data center, the source markets change and old results clear.
- Looks for **crafting opportunities** and **buy-on-another-world / resell-at-home opportunities**, separately for NQ and HQ.
- Prioritizes up to 200 recently updated DC item IDs, current findings, and saved watchlist items. Each round also checks the next 100 items in the marketable local catalog, so discovery broadens over time.
- Publishes results in batches of 25 and ranks qualifying plans by estimated net gil, then sampled sales activity. Coverage and progress are visible; this is not a complete live-market scan.
- Compares alternate recipes and batches of 1, 5, and 10 crafts. Shows the recipe, crafting job/level, required craft count, and output quantity.
- Builds a **whole-stack shopping plan**, including which world to visit, quantities, unit prices, and assumed purchase tax. Different ingredients may come from different worlds in your local DC.
- Filters by gil budget per plan, minimum net profit, minimum ROI, minimum sampled sales, and maximum market-data age. Defaults: 100,000 gil budget, 1,000 gil profit, 10% ROI, three sampled sales in seven days, and data no older than 24 hours.
- Automatically starts another round 10 minutes after completion. Pause scanning or use Scan now at any time. Closing the window, switching to manual comparison, logging out, changing scope, or unloading cancels outstanding discovery work.
- Removes findings when a rescan no longer supports them. Findings expire 15 minutes after evaluation or when their source data crosses the configured age limit.

Automatic scanning performs public market-data analysis only. MarketMage does not buy, sell, craft, gather, move, manage retainers, or interact automatically with game servers.

## How opportunities are estimated

**Selling price:** the lower of the quality-specific median from usable sampled sales in the last seven days and one gil below the lowest sampled home-world listing (minimum one gil). A recent home-world upload, a fresh competing listing, and sufficient sampled sales are required. Estimated revenue subtracts an assumed 5% selling tax, rounded down per unit.

**Resale:** compare each fresh sampled stack on another world in the current DC against selling all its units at home. The plan buys the entire stack and adds an assumed 5% purchase tax. A stack larger than the units observed in the recent sample is excluded. Only the best qualifying sampled stack per item/quality is displayed.

**Crafting:** compare alternate recipes and batches of 1, 5, and 10 crafts. For each ingredient, find the lowest full-stack checkout cost covering the required quantity on a single source world, among the sampled fresh NQ listings. The recipe can source different ingredients from different worlds. The cost includes all purchased stacks plus assumed 5% purchase tax; leftover ingredients are valued at zero. Output quantity cannot exceed observed sampled units. Only the highest-profit qualifying recipe/batch per item/quality is displayed.

All opportunity results require positive net profit, sufficient stock, complete cost data, and the configured budget/ROI/profit thresholds. The budget applies separately to each plan, not to a combined portfolio of plans. Profit assumes every output unit sells at the estimate. Travel, crafting time, gear, consumables, and recipe unlock costs are excluded. HQ craft results require you to produce HQ outputs; character recipe access and crafting ability are not validated.

Universalis returns up to 100 listings and 20 sales per item/quality/scope in these requests. The seven-day sales figure is the portion of that sample falling in the time window, **not total market volume or a sell-through prediction**. Requests are serialized, paced at least one second apart, batched by up to 50 IDs, and retried at most twice for throttling/server errors while respecting Retry-After. Shared ingredient responses are reused within a scan round. Large rounds can take several minutes.

The public market is crowdsourced and can change between upload, scan, and purchase. Recheck the shopping plan before buying. Separate opportunities can depend on the same stock and must not be added together as guaranteed profit. If no plans qualify, the UI reports that honestly rather than filling the list with speculative margins.

## Manual comparison

The **Manual comparison** view remains available for targeted investigation:

- Search by item name or exact ID; filter to craftable items.
- Save up to 50 watchlist items plus sale world, output quality, and comparison preferences.
- Choose public worlds grouped by region and data center.
- Compare historical output prices with local/DC NQ ingredient costs.
- Inspect source worlds, available quantities, and sale/upload/review ages.

Manual comparison uses the first matching recipe and partial-stack material values; its figures are exploratory and differ from the whole-stack opportunity plans. It does not include purchase tax, and stale data is shown with age labels rather than excluded. Changing its inputs cancels pending requests and clears old results.

## Build and checks

Prerequisites:

- .NET 10 SDK; the project uses `Dalamud.NET.Sdk/15.0.0`.
- XIVLauncher/Dalamud installed and run at least once, with compatible assemblies. Set `DALAMUD_HOME` for a custom assembly directory.

```powershell
dotnet build MarketMage.sln -c Debug -p:Platform=x64 -p:RestoreLockedMode=true
dotnet run --project MarketMage.Tests -c Release
```

The offline regression executable needs no game installation and exits nonzero on failure. It covers parsing, HQ/NQ separation, whole-stack purchases, profit arithmetic, alternate recipes, budget/demand/freshness filters, candidate rotation, local-DC boundaries, incremental scanning, cancellation, batching, caching, and expiry. CI runs these checks, builds on Windows, and uploads the DLL/manifest.

Optional read-only live API smoke check (not part of CI):

```powershell
dotnet run --project MarketMage.Tests -c Release -- --live-smoke
```

Debug DLL: `MarketMage/bin/x64/Debug/MarketMage.dll`.

## Load in game

1. Open `/xlsettings` → `Experimental`.
2. Add the full DLL path under Dev Plugin Locations.
3. Open `/xlplugins` → `Dev Tools` → `Installed Dev Plugins`.
4. Enable MarketMage and run `/marketmage` while logged in.
5. Check the detected sale world and source DC, set a budget if desired, and let the scan run.

### In-game acceptance checks

These require an actual FFXIV/Dalamud session; compilation, API smoke checks, and offline tests do not establish that they passed.

- Open with no watchlist and verify automatic discovery begins using the correct home world and current DC.
- Confirm results arrive in batches and each row opens its shopping plan; check a resale stack and a multi-yield crafting recipe manually.
- Confirm HQ plans are labeled and show crafting job/level where applicable.
- Change budget/profit/freshness filters and verify old findings clear before rescanning.
- Pause, close, switch views, log out, travel to another DC, or unload mid-scan; verify no late results appear for the previous scope.
- Leave the view open for a full round and automatic rescan; verify obsolete findings disappear and expired findings are removed.
- Simulate connectivity failure and verify partial-result/error status plus delayed retry.
- Check all columns and shopping details at different window sizes.
- Verify manual comparisons and saved watchlists/preferences still work after a plugin reload.

## Current boundaries

- Discovery covers crafting and market resale. Gathering, vendor arbitrage, desynthesis, ventures, and other acquisition methods are not modeled.
- No recursive subcraft costing, inventory-aware spending, travel optimization, character skill/recipe-unlock validation, or global search across every region.
- Whole-stack optimization uses the sampled listings, with one world per ingredient; it is not an exhaustive market or multi-world basket optimizer.
- No automatic trades or guaranteed profits, external alerts, persistent price database, or plugin repository submission setup.

## Data sources

- [Universalis API documentation](https://docs.universalis.app/) and its [v2 schema](https://docs.universalis.app/api/schema/v2): recent-market discovery, sale history, and listings.
- Dalamud/Lumina: player world/DC, marketable items, recipes, crafting job/level, and HQ capability.
