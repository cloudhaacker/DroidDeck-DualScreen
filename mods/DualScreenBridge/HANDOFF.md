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

## v0.6.4 log findings (first log with leak and camera logging)
- Leak found: `StateFlowBase.Update > GUIMap.UpdateStatus > GUIMapCommon.OnUpdate > GUIMapTownAndDungeon.OnUpdate >
  GUIDelayWindow.MoveWindow > UITweener.Play > TweenPosition.ChangeEnabled > UICamera.LockInput` ran for real, because
  the copy's per-frame update was not inside an own call. v0.6.5 wraps `StateFlowBase.Update` as an own call.
- About 1s after the copy was created the game's `m1 menuBg` camera (full screen, depth 30) went from mask 0x80000 /
  clear Depth to mask 0x0 / clear Color. A full-screen camera clearing to a colour over the field camera would give a
  black top screen; the call that changed it is not attributed yet. Check whether it still happens in v0.6.5.
- Later in that run `CapturedTexture` came on, then `a main` and `m2 menu` went off; unknown whether that was the game
  reacting to something John pressed.
- The game's own `CapturedBGNGUI(Clone)` (parent AppSystem) exists before the copy; v0.6.3 deactivated it as a "stray".

## Status (v0.6.5, v0.6.4 changes included)
- v0.6.1 and v0.6.3 logs were clean but John still lost the top screen and controller after opening the map below.
  The v0.6.3 log shows no blocked or leaked calls at all, so it cannot say which call reaches the real game.
- v0.6.4 (source only until built): the prefab is instantiated under the copy's root inside an own call, so Awake and
  OnEnable are owned (before, ownership only existed after Instantiate returned); the copy is removed with
  DestroyImmediate inside an own call, so its OnDestroy cleanup (input unlock, captured background) no longer runs for
  real at end of frame; the camera is disabled before its RenderTexture is released; the stray captured-background hider
  skips objects that existed before the copy.
- v0.6.4 logging to read first: "reached the real game from the map copy" (hooked calls with the stack),
  "before/after/changed camera" dumps (enabled state, mask, depth, clear flags), "controller focus" (NGUI selected object).
- World tap inside a town uses the world map (mode 1) when `CheckOpenFieldMap` allows it (v0.6.2).
- Icons now appear on the bottom map.

## Next
- Build v0.6.4 (needs the game's Managed folder and MelonLoader folder), test, and read the leak and camera lines in the log.
- Crop the bottom-screen picture to just the map and hide the zoom, Icons Off and close buttons.
- Live player marker, auto town/field switching check, minimap-off check.
- Move the mod out into its own repo for Nexus; keep this fork for the app. Rebase the branch onto the new fork `main`.
- Add an AI-authorship note to the fork README (upstream rejects AI-authored commits).

## Tooling notes
Game DLLs are user supplied (not in the repo). IL was read with a small Mono.Cecil dumper (`IlDump`, `IlCallers`)
because `monodis` crashes on Assembly-CSharp. Stop-hook messages about unpushed commits refer to a different clone
(the upstream scratch copy), not this fork.
