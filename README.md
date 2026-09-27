# Supermarket Together inventory mods

Two independent BepInEx plugins for Supermarket Together:

| Mod | Version | What it does |
| --- | --- | --- |
| [Stock Planner](StockPlanner/) | 0.2.2 | Adds enough boxes to the shopping list to cover assigned shelf capacity and reserve stock. It never purchases them. |
| [By Box (Units)](ByBoxUnits/) | 0.4.2 | Adds a Product Order sort based on counted stock and units per box. |

## Build and install

Install the .NET SDK and BepInEx in your Supermarket Together game. From the directory of either mod run `dotnet build -c Release`. The projects default to the Steam game directory at `C:\Program Files (x86)\Steam\steamapps\common\Supermarket Together`. For a different installation, add `-p:GameRoot="D:\path\to\Supermarket Together"` to the build command.

Copy the resulting plugin DLL from the mod's `bin\Release\netstandard2.1\` directory to a folder under the game's `BepInEx\plugins\`. Remove previous versions of that same mod before restarting the game. Each mod's README names its DLL and explains the config settings.

| Key | Action |
| --- | --- |
| F6 / F7 | Increase / decrease By Box stock threshold |
| F8 | Toggle whether By Box counts units in boxes |
| F10 | Enable / disable Stock Planner |

If you also use Texture Memory Fix, configure its F6/F7 hotkeys to other keys (for example F1/F2) to avoid triggering both mods.

The source projects reference DLLs from your own game installation. No game DLLs, BepInEx binaries or compiled releases are included here.
