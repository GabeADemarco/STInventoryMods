# By Box (Units) 0.4.2

Adds **By box (units)** to the Product Order sorting menu. It puts products with counted stock at or below `MinimumStockBoxes × units per box` first, and sorts each group by ascending counted units. Stock in storage and, by default, in boxes is counted.

Build with `dotnet build -c Release` in this directory, using `-p:GameRoot="D:\path\to\Supermarket Together"` if necessary. Install `bin\Release\netstandard2.1\STByBoxUnits_v042.dll` in `BepInEx\plugins\ByBoxUnits\` and restart.

In `BepInEx\config\gabe.supermarkettogether.byboxunits.cfg`, `MinimumStockBoxes` defaults to 1 and `IncludeUnitsInBoxes` to true. F6 increases the threshold (up to 5), F7 decreases it (down to 1), and F8 toggles counting units in boxes. These keys can be changed in the config.

Version 0.4.2 limits its UI changes to the Product Order screen so that employee assignment buttons remain usable.
