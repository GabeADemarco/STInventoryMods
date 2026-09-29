# Supermarket Together inventory mods

Four independent BepInEx plugins for Supermarket Together:

| Mod | Version | What it does |
| --- | --- | --- |
| [Stock Planner](StockPlanner/) | 0.2.2 | Adds enough boxes to the shopping list to cover assigned shelf capacity and reserve stock. It never purchases them. |
| [By Box (Units)](ByBoxUnits/) | 0.4.2 | Adds a Product Order sort based on counted stock and units per box. |
| [Assigned Storage Only](AssignedStorageOnly/) | 0.2.0 | Limits storage workers to labeled slots and prioritizes boxes for products low on display stock. |
| [Restocker Priority](RestockerPriority/) | 0.1.0 | Prioritizes products by overall shelf fill and prefers full boxes when a partial box cannot finish a row. |

## Using Stock Planner with Assigned Storage Only

These mods are independent, but their settings work well together. For a product, label **two storage slots** and set Stock Planner's `ReserveBoxes = 1` in `BepInEx/config/gabe.supermarkettogether.stockplanner.cfg`. Once its display shelves are refilled, the slots can hold one full reserve box plus the remainder of a used box. If you want two full reserve boxes, label at least three slots to leave room for a partial box.

Stock Planner targets **units**, not storage slots. It rounds purchases up to whole boxes, so deliveries, manual purchases, or shelves still waiting to be restocked can temporarily leave more boxes than the labeled slots can hold. Assigned Storage Only will leave those extra boxes for manual handling until a matching slot becomes free.

## Build and install

Install the .NET SDK and BepInEx in your Supermarket Together game. From the directory of a mod run `dotnet build -c Release`. The projects default to the Steam game directory at `C:\Program Files (x86)\Steam\steamapps\common\Supermarket Together`. For a different installation, add `-p:GameRoot="D:\path\to\Supermarket Together"` to the build command.

Copy the resulting plugin DLL from the mod's `bin\Release\netstandard2.1\` directory to a folder under the game's `BepInEx\plugins\`. Remove previous versions of that same mod before restarting the game. Each mod's README names its DLL and explains the config settings.

| Key | Action |
| --- | --- |
| F6 / F7 | Increase / decrease By Box stock threshold |
| F8 | Toggle whether By Box counts units in boxes |
| F10 | Enable / disable Stock Planner |

If you also use Texture Memory Fix, configure its F6/F7 hotkeys to other keys (for example F1/F2) to avoid triggering both mods.

The source projects reference DLLs from your own game installation. No game DLLs or BepInEx binaries are included here.

## Test builds

Compiled DLLs supplied by the author are available in [`dist/`](dist/):

| Mod | DLL |
| --- | --- |
| Stock Planner 0.2.2 | [`STStockPlanner_v022.dll`](dist/STStockPlanner_v022.dll) |
| By Box (Units) 0.4.2 | [`STByBoxUnits_v042.dll`](dist/STByBoxUnits_v042.dll) |
| Assigned Storage Only 0.2.0 | [`STAssignedStorageOnly_v020.dll`](dist/STAssignedStorageOnly_v020.dll) |
| Restocker Priority 0.1.0 | [`STRestockerPriority_v010.dll`](dist/STRestockerPriority_v010.dll) |

Assigned Storage Only 0.2.0 and Restocker Priority 0.1.0 have been statically checked but still need gameplay testing. Install only one version of each plugin at a time. Texture Memory Fix is maintained in its separate repository. If you use it alongside By Box (Units), change its default F6/F7 hotkeys in its config.
