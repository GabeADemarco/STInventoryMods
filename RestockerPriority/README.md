# Restocker Priority 0.1.0

This BepInEx mod changes how **restocking employees** choose tasks and boxes. It is independent of Stock Planner, By Box (Units), and Assigned Storage Only.

- The worker's first choice is the product with the lowest **overall shelf fill percentage**, calculated across all assigned shelf rows for that product. Among rows of the same product, the emptier row comes first. The game still provides the eligible tasks, so a product without stock in storage is not added as a new task.
- If the box the game picked is partial and cannot finish the chosen shelf row, the mod prefers a **full box of that product** that can finish the row. If none is available, it retains the original partial box.
- Different workers receive different ranked shelf-row tasks when the game offers enough tasks. Placement speed, pathing, merging, and recycling remain the game's own behavior.

## Build and install

Open Command Prompt in this directory and run `dotnet build -c Release`. The project expects the game at `C:\Program Files (x86)\Steam\steamapps\common\Supermarket Together`. If yours is elsewhere, add `-p:GameRoot="D:\path\to\Supermarket Together"`.

Copy `bin\Release\netstandard2.1\STRestockerPriority_v010.dll` to `Supermarket Together\BepInEx\plugins\RestockerPriority\` and restart the game. Keep only one version of this mod installed.

The generated `BepInEx\config\gabe.supermarkettogether.restockerpriority.cfg` has two independent toggles: `ProductWidePriority` and `PreferFullBoxWhenNeeded`. Both default to `true`. `LogTaskChanges` defaults to `false` and can be enabled for troubleshooting.

## First test

Try a small setup with two products, one with a single empty row but otherwise full shelves and another with several nearly empty rows. Supply storage boxes for both, then check which product the worker visits first. For the box preference, provide one partial and one full box of the same product while a row needs more than the partial box contains. Employee work may already be in progress, so allow a new task-selection cycle.

This source has been statically checked against the supplied game assembly. It has **not** been compiled or tested in game here. Send `BepInEx\LogOutput.log` if the behavior differs.
