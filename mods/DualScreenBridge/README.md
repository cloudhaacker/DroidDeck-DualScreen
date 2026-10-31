# DualScreenBridge

A MelonLoader mod for Dragon Quest VIII Masterpiece Reborn (Unity 2017.4, Mono) that talks to the DroidDeck Dual Screen app's bridge on `127.0.0.1:47200` (see `docs/dual-screen-map-bridge.md`).

Version 0.3 keeps the second screen showing the live map by itself. Once a second it asks the game the same question its own map button asks (town or dungeon map, world map, or no map here), builds a private copy of the game's map window off to the side of the playfield, draws it with its own camera into an image and sends that image to the app. It rebuilds the copy when the area changes. The copy's StartMap and EndMap are skipped, so it does not hide the HUD or flag the real map as open, and it steps aside while the real in-game map is open. The app's Area and World buttons choose between those two views in the field.

The mod asks the game to load the map assets itself, and removes its private layer from every other camera so the game's own UI camera never draws the copy. Diagnostics go to the MelonLoader log.

## Build

Needs `mcs` (Mono), the game's `DQ8MR_Data/Managed` folder and MelonLoader's `MelonLoader` folder:

    mods/DualScreenBridge/build.sh /path/to/DQ8MR_Data/Managed /path/to/MelonLoader out

Game and MelonLoader files are never committed. Copy `out/DualScreenBridge.dll` into the game's `Mods` folder.
