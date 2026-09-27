# Stock Planner 0.2.2

Checks products assigned to shelf spaces and adds whole boxes to the shopping list until owned stock plus queued boxes reaches the combined assigned shelf capacity and configured reserve. Counts stock on shelves, in storage and in boxes. Ignores products without assigned shelves. **It never presses Buy.**

Build with `dotnet build -c Release` in this directory, using `-p:GameRoot="D:\path\to\Supermarket Together"` if necessary. Install `bin\Release\netstandard2.1\STStockPlanner_v022.dll` in `BepInEx\plugins\StockPlanner\` and restart.

The plugin starts off. F10 toggles checks; the first check runs immediately and repeats every minute by default. Set these entries in `BepInEx\config\gabe.supermarkettogether.stockplanner.cfg` while the game is closed:

| Entry | Default | Meaning |
| --- | --- | --- |
| `ReserveBoxes` | 2 | Full boxes beyond assigned shelf capacity. Set to 1 for one reserve box. |
| `IncludeFullShelves` | true | Include capacity across every assigned shelf space. |
| `CheckIntervalMinutes` | 1 | Interval in minutes, 1 to 1440; config only. |
| `EnabledOnStart` | false | Start checking when the game loads. |
| `DiagnosticLogging` | false | Write detailed per-shelf calculations to the BepInEx log. |

Version 0.2.2 counts multiple spaces assigned to one product within the same furniture unit.
