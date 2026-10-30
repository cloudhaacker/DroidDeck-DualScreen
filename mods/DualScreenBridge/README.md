# DualScreenBridge

A MelonLoader mod for Dragon Quest VIII Masterpiece Reborn (Unity 2017.4, Mono) that talks to the DroidDeck Dual Screen app's bridge on `127.0.0.1:47200` (see `docs/dual-screen-map-bridge.md`).

Version 0.1 is a connection test: it sends a small live picture of the game screen and a title line, shows the last command received from the app's Area and World buttons, and logs when the game's map opens and closes.

## Build

Needs `mcs` (Mono), the game's `DQ8MR_Data/Managed` folder and MelonLoader's `MelonLoader` folder:

    mods/DualScreenBridge/build.sh /path/to/DQ8MR_Data/Managed /path/to/MelonLoader out

Game and MelonLoader files are never committed. Copy `out/DualScreenBridge.dll` into the game's `Mods` folder.
