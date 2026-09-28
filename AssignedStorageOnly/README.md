# Assigned Storage Only 0.1.0

This BepInEx plugin changes **storage employees only**. They may place a box only in an empty storage slot explicitly labeled for that box's product. If all matching slots are occupied or there is no matching label, the worker treats the box as having no free storage slot and leaves it alone. Empty unlabeled slots remain available for manual placement.

The plugin filters the game's storage-worker slot search; it does not relabel shelves or change the player's placement controls. It is independent of Stock Planner and By Box (Units).

## Build and install on Windows

Copy this directory to your PC, open Command Prompt in it, and run:

```bat
dotnet build -c Release
```

The project defaults to the Steam game directory at `C:\Program Files (x86)\Steam\steamapps\common\Supermarket Together`. For another installation, use `dotnet build -c Release -p:GameRoot="D:\path\to\Supermarket Together"`.

Copy only `bin\Release\netstandard2.1\STAssignedStorageOnly_v010.dll` to `Supermarket Together\BepInEx\plugins\AssignedStorageOnly\` and restart the game. Do not install multiple versions of this plugin.

## First check

Label one storage slot for a product and leave an unlabeled slot empty. A storage employee should fill the labeled slot, then leave further boxes of that product alone when it is full. You should still be able to place a box manually in the unlabeled slot. If the behavior differs, send `BepInEx\LogOutput.log`.

The source has been checked against the game assembly used for the existing mods. Version 0.1.0 has been compiled and statically inspected. Initial in-game testing with labeled slots has worked; the case of a new product with no storage label still needs testing.
