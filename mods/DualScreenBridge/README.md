# DualScreenBridge

A MelonLoader mod for Dragon Quest VIII Masterpiece Reborn (Unity 2017.4, Mono) that talks to the DroidDeck Dual Screen app's bridge on `127.0.0.1:47200` (see `docs/dual-screen-map-bridge.md`).

Version 0.2 is an experiment. When the app's Area or World button is tapped, the mod builds a private copy of the game's own map window, off to the side of the playfield, draws it with its own camera into an image and sends that image to the second screen. The copy's StartMap and EndMap are skipped, so it does not hide the HUD or flag the real map as open. Status text and a short diagnostic line go to the MelonLoader log.

The in-game map must have been opened once so the game has its prefab loaded.

## Build

Needs `mcs` (Mono), the game's `DQ8MR_Data/Managed` folder and MelonLoader's `MelonLoader` folder:

    mods/DualScreenBridge/build.sh /path/to/DQ8MR_Data/Managed /path/to/MelonLoader out

Game and MelonLoader files are never committed. Copy `out/DualScreenBridge.dll` into the game's `Mods` folder.
