# Assigned Storage Only 0.2.0

This BepInEx plugin changes **storage employees only**. They may place a box only in an empty storage slot explicitly labeled for that box's product. If all matching slots are occupied or there is no matching label, the worker treats the box as having no free storage slot and leaves it alone. Empty unlabeled slots remain available for manual placement.

The plugin filters the game's storage-worker slot search; it does not relabel shelves or change the player's placement controls. It is independent of Stock Planner and By Box (Units).

Storage workers also prioritize ground boxes belonging to products with the **lowest overall shelf fill percentage** across that product's assigned shelf rows. Only boxes with a free matching labeled storage slot are eligible. Workers spread across different eligible boxes when possible. A product with no assigned display shelf is treated as fully stocked for ranking purposes. This prioritization is enabled by default and can be disabled with `PrioritizeLowShelfStock = false` in the generated config file.

## Build and install on Windows

Copy this directory to your PC, open Command Prompt in it, and run:

```bat
dotnet build -c Release
```

The project defaults to the Steam game directory at `C:\Program Files (x86)\Steam\steamapps\common\Supermarket Together`. For another installation, use `dotnet build -c Release -p:GameRoot="D:\path\to\Supermarket Together"`.

Copy only `bin\Release\netstandard2.1\STAssignedStorageOnly_v020.dll` to `Supermarket Together\BepInEx\plugins\AssignedStorageOnly\` and restart the game. **Remove `STAssignedStorageOnly_v010.dll` first**; do not install multiple versions of this plugin.

## First check

Label one storage slot for a product and leave an unlabeled slot empty. A storage employee should fill the labeled slot, then leave further boxes of that product alone when it is full. You should still be able to place a box manually in the unlabeled slot. If the behavior differs, send `BepInEx\LogOutput.log`.

For the new priority, place ground boxes for two products that both have free labeled storage slots. Make one product low on display stock while the other is nearly full; a storage worker should collect the low-stock product's box first on a new task selection.

The source has been checked against the game assembly used for the existing mods. Version 0.2.0 has not yet been compiled or tested in game; please test with a small number of boxes first.
