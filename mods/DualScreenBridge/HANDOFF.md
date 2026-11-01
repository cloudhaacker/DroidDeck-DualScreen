# DualScreenBridge: project state and handoff

Read this first when picking the project up in a new session.

## Goal
A persistent, live area or world map on the Ayn Thor's bottom screen for Dragon Quest VIII Masterpiece Reborn
(DQ8MR, Unity 2017.4.8 Mono, MelonLoader 0.5.7 under Proton), through a "Dual screen" mode in the
DroidDeck Dual Screen fork. John (the owner) is not a coder: Claude writes the code, John tests on the device and
sends `MelonLoader/Latest.log` plus screenshots.

## Pieces
- App (this repo): `MapBridge.kt`, `SecondScreenPresentation.kt`, docs in `docs/dual-screen-map-bridge.md`.
  Bridge protocol on 127.0.0.1:47200 (u32 length, u8 type, payload; 1 image, 2 title, 3 command).
- Mod (this folder): `BridgeClient.cs` (socket), `GameApi.cs` (reflection helpers, auto map mode),
  `HeadlessMap.cs` (a private copy of the game's map window drawn by its own camera into a RenderTexture),
  `DualScreenBridge.cs` (Harmony hooks, pump, back-off). Build: `build.sh MANAGED_DIR MELONLOADER_DIR OUT_DIR`.
- The game's own toolkit class `DQ8MR.Mods.Toolkit.MapUi` takes over every `GUIMap`; the copy must keep clear of it.

## What the copy must never do to the real game (each one was a real bug)
1. Run `MapUi.Attach` / `MapUi.Release` (black full-width map with controller hints on the top screen; Release also
   runs from `GUIMap.OnDestroyInner` after the copy is gone, so the copy's map window is remembered by reference).
2. Run `GUIMap.StartMap/EndMap/FieldOff/FieldOn` (HUD hiding, field camera shutoff).
3. Push or pop `UICamera` interfaces, or lock/unlock input (controller stops responding). Locks also happen in
   `GUIMapWorld.OnExchangeWorldLocal/AlphaFadeUpdate`, so those run inside an "own call" depth counter.
4. Use `CapturedBgNGUI` (captures the menu camera and ORs a layer into its culling mask): its Enable/Disable/
   set_RenderCamera/SetLocalPosition are no-ops during own calls, the menu camera mask is held to its original value
   while the copy exists, and the stray captured-background object is hidden.
5. Leave any part on the game's menu layer 8: all parts are moved back to layer 31 just before each camera culls.
6. Rebuild itself in a loop: it starts only after an Area or World tap and backs off after 4 quick closes.

## Status (v0.6.3)
- v0.6.1 log was clean but John still lost the top screen and controller after opening the map below; v0.6.3 adds
  items 3 and 4 above for the world-map code paths. Not yet confirmed on device.
- World tap inside a town uses the world map (mode 1) when `CheckOpenFieldMap` allows it (v0.6.2).
- Icons now appear on the bottom map.

## Next
- Confirm v0.6.3 leaves the top screen and controller alone.
- Crop the bottom-screen picture to just the map and hide the zoom, Icons Off and close buttons.
- Live player marker, auto town/field switching check, minimap-off check.
- Move the mod out into its own repo for Nexus; keep this fork for the app. Rebase the branch onto the new fork `main`.
- Add an AI-authorship note to the fork README (upstream rejects AI-authored commits).

## Tooling notes
Game DLLs are user supplied (not in the repo). IL was read with a small Mono.Cecil dumper (`IlDump`, `IlCallers`)
because `monodis` crashes on Assembly-CSharp. Stop-hook messages about unpushed commits refer to a different clone
(the upstream scratch copy), not this fork.
