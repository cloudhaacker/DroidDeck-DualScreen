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

## Cause of the black top screen (found from the IL, v0.6.6 fix)
- `BackGroundUI` is the game's single menu-background object (the `m1 menuBg` camera). `GUIMapTownAndDungeon` and
  `GUIMapWorld` `SetMapDrawItem` call `BackGroundUI.DispStart` and `SetSolidColor` (mask 0, clear Color, full screen
  black over the field); `ResetMapDrawItem` calls `DispEnd` to restore it. The copy ran the first and never the second
  on the real object. v0.6.6 makes both `SetMapDrawItem`/`ResetMapDrawItem` own calls and skips those three
  `BackGroundUI` methods during own calls.
- The game's item-menu world map command (`fldcmd_item._EVM_FIELD_CMD_WORLD_MAP`) sets `GUIMap.gMapMode` directly, so a
  World tap in a town now uses mode 1 without the `CheckOpenFieldMap` check (untested; watch dungeons).
- The v0.6.5 test log was actually still v0.6.4 (same file hash), so the `StateFlowBase.Update` fix is untested alone.
- Tool: a small Mono.Cecil search (`IlFind`, types/calls modes) built with `mcs` against MelonLoader's Mono.Cecil.dll
  reads the game's Assembly-CSharp.dll.

## v0.6.6 result and v0.6.7
- v0.6.6 confirmed on device: no leak lines, `m1 menuBg` untouched, map stays on the bottom screen, area and world both
  work, game playable. Remaining: a brief fade to black with the slime loading icon when a view starts or switches.
- Cause: `GUIMapTownAndDungeon.OnUpdate` and `GUIMapWorld.OnUpdate` call `mloop.GetMainScene().FadeOut`, wait on
  `FadeCheck`, then `FadeIn` on the real main scene. v0.6.7 skips `dqCScene.FadeOut/FadeIn` during own calls and makes
  `FadeCheck` return true so the copy's state machine does not stall.
- `GUIMapWorld.OnUpdate` also calls `CCameraControl.applyCameraDistance` on the real scene camera; untouched so far.
- One fix per release, tested on device each time. Order agreed with John: 1 fade (v0.6.7), 2 auto-start the map on
  game load (set `Armed` once connected), 3 app-side default for Game map second-screen mode (`SessionActivity`,
  `SessionPrefs`), 4 map features (icons on/off, touch pan and zoom, hide unvisited places).
- Possible later cleanup: for the world copy the stray hider deactivates the game's shared `CapturedBGNGUI(Clone)`.

## v0.6.7 result and v0.6.8
- v0.6.7 on device: the fade and slime icon are gone. New problems John reported: a small camera jump behind the hero
  and a hiccup when switching between Area and World; after closing the real map opened from the item menu, the bottom
  map goes black for about a second while the copy is rebuilt; after the real world map had run once the bottom world
  map showed only the visited icons (before that it showed every icon, even unvisited places).
- Bug in v0.6.7: the copy's state machine sat in `WaitOpenFadeIn` forever (log: `world=False / WaitOpenFadeIn`) because
  `FadeCheck` always reported "finished". The game waits for finished after `FadeOut`, but after the open-time `FadeIn`
  it waits for one unfinished frame (`FadeCheck` false) and then moves on; the close path waits for finished.
  v0.6.8 records the frame of a skipped own `FadeIn` and reports unfinished for that frame and the next.
- The `FadeCheck(int)` overload fails to patch (IL compile error); the map windows only call the parameterless one, so it
  is no longer hooked.
- Ideas for the jump and hiccup (unconfirmed): the rebuild frame is large (destroy, instantiate 84 to 366 transforms,
  new RenderTexture) and the field camera follow may overshoot after a long frame; keep both copies built and only
  switch which one is active; hold the last frame instead of sending the black first frames of a new copy.
- Icons: `GUIMap.StartMap` is skipped for the copy (`SkipOwn`), so whatever it sets up for the visited-place data may not
  run; the real world map filling in the data suggests static state is set there or in `guidmap.InitGuidMap`.
- `worldview_2.resetCameraFlag` is set by `FieldGUIMenu.Open` and by the close path of `GUIMapWorld.OnUpdate`.

## v0.6.8 result and v0.6.9
- v0.6.8 on device: the world copy reaches `UpdateSmallMap`, no warnings, no leak lines. The camera jump happens only
  with World (the world copy is much heavier: 366 transforms against 84, a big map texture, a screen-capture fade) and
  is probably the long frame while it builds; no camera call was found in the world startup code. John uses Lossless
  Scaling frame generation (may amplify a long frame; test with it off).
- v0.6.9: `Armed` starts true, so the copy builds by itself once the bridge is connected and the player is in the
  field (the app's bridge listens all session, and the mod only connects when `BL_MAP_BRIDGE` is there, so users without
  the app are unaffected; the app does not yet tell the mod whether Game map mode is selected). Frames are not sent
  until `HeadlessMap.Ready` (first window re-layer plus 0.4s, or 3s), so the second screen keeps the previous picture
  instead of showing the black first frames of a rebuilt copy.
- Next: build both copies once and only switch which one renders (fixes the hiccup and jump; the copies share the
  static `GUIMap.mapMode`, which `GUIMap.UpdateStatus` reads every frame, so each copy's update needs its own mode),
  ideally while the game loads. Then the app default (needs an app build), then map features.
- Launch options go through DroidDeck; John will try `--melonloader.hideconsole` (documented for current
  MelonLoader, untested on 0.5.7). A loading message on the second screen can wait.

## v0.6.9 result and v0.6.10
- v0.6.9 on device: auto-start broke loads. Church and inn save loads hang on a black screen with the slime icon and a
  message that cannot be advanced; entering a town leaves the top screen black until the item-menu map is opened and
  closed; stutter in town; slowdown on the way to field and dungeon. Cause (partly unconfirmed): the copy was built while
  the game was loading, changing maps or showing a message window.
- v0.6.10: the copy is only built after the game's own "player free" test (`GameApi.PlayerFree`: walking loop, control
  mode 1/5/8, no message window) has been true for 4s on the same map; a map change destroys the copy. The mode values
  are inferred; the log prints "player free: ..." with the values on every change. If it never turns true, check there.
- Timing log every 15s while a copy exists: "perf ms: poll ...; tick ...; capture ..." (avg and max per piece) to find
  the stutter. Camera diagnostics now only run for the first 30s of a copy.
- Next: read those logs, then build both copies once (hiccup/jump), app default, map features.

## v0.6.10 result and v0.6.11
- v0.6.10 on device: church saves load now. The copy is built 4s after "player free: True" and nothing hangs.
- Cutscene (king, on leaving town): John sees roughly 10s pauses between text prompts. In both field logs the copy is
  gone for the whole cutscene (no perf lines; removed silently by a map change, no player, or no map, which v0.6.10 did
  not log) and the mod's own measured work stays tiny (poll max 6.7ms, tick max 5ms, capture max 61ms). The poll counter
  shows the main thread not running Update for about half of those windows: 64 polls in 119s and 37 in 86s, against
  14 or 15 per 15s in normal play. So the stalls overlap time with no copy and are not explained by the measured work.
  Unresolved; the A/B test is the same cutscene and fight with Game map mode off (mod idle).
- Battle: the copy survives the fight (perf lines and captures continue), and nothing rebuilds it. About 1.2s before the
  fight the game removes the `m1 menuBg`, `BalloonCamera` and `ConvMapPosCamera` cameras, then renames the main camera
  from `a main` to `a battle` (it is `a main` again by 51:19 in the second log). After the fight the bottom picture is
  gone while the copy is still Alive; tapping Area or World, or opening the item-menu map, rebuilds it and fixes it.
  The top-screen flash is probably the copy drawn by a freshly created camera until `Isolate` (every 1s) strips its
  layer; unconfirmed.
- v0.6.11: `GameApi.InBattle` (a camera named `a battle`) destroys the copy as soon as a fight starts; the normal 4s
  settle rebuilds it afterwards, and the second screen keeps the last picture meanwhile.
- v0.6.11 logging: `map copy removed: <reason> (age)`, `battle: true/false`, and `frame hitch: Ns, the mod used Mms of
  it; copy ...; battle ...; player free ...` for frames over 0.75s after the field was first reached (40 lines at most).
  A hitch line with the mod near 0ms points at the game or the device, not the mod.
- Next: read the hitch lines. If the top screen still flashes, strip the copy's layer from every camera in `OnPreCull`
  instead of once a second. If the copy turns out to cost frames, render its camera on demand (a few times a second,
  when a capture is due) instead of every frame.

## v0.6.11 result and v0.6.12 (trace build)
- v0.6.11 on device: the fight fix works in the log (`battle: True`, `map copy removed: battle`, `battle: False`, copy
  rebuilt 4s later). John's visual check of the bottom map after a fight is still to confirm.
- The pauses are not the mod. Hitch lines in the king cutscene: 11 frames of 9 to 12.7s (about 115 of 146s) with no copy
  and the mod at about 1ms; the fight start has four 2s frames with the copy already gone. John also saw the same
  slowdowns and freezes with no mod DLL, in official DroidDeck, and with lower resolution and graphics quality, so not
  the fork, not the mod and not gpu fill. The stall lengths cluster near 9.5s and 12.5s, which looks like a timed wait.
- With the copy up there are runs of 2s frames (town and field); that part is probably the copy's per-frame render and
  readback and is a separate, smaller cost. Idea still open: render the copy camera on demand instead of every frame.
- John reports that near the waterfall by the next dungeon the game almost stops and speeds up again toward town.
- v0.6.12 adds no behavior change except the `DualScreenBridge.nocopy` flag file (next to the game's exe), which keeps
  the copy from ever being built so a run measures the game alone. It adds measurement that also runs with no bridge
  connected (official DroidDeck, GameNative, plain Wine), once the field has been reached:
  - `FrameTrace` (Camera.onPreCull/onPreRender/onPostRender plus Update, LateUpdate and end of frame) splits each frame
    into update, late-update, render, after-render and present; every 3s it logs `trace:` with map number, main camera
    position, fps, those phases, GC counts, heap, rss and swap, pressure-stall numbers, sensors and the three costliest
    cameras. A frame over 0.75s adds its phases and GC counts to the `frame hitch:` line. When the 3s average is under
    20 fps (at most every 10s) `trace detail:` lists the main camera target size, post effects, visible and transparent
    renderers by shader, lights, particles and quality settings.
  - `StallProbe` (background thread, `ProcInfo` reads the host's /proc through Z:) watches the main thread's heartbeat.
    During a stall of a second or more it logs `stall probe:` with the main thread's state, kernel wait function and
    syscall number every 1.5s, the busiest threads by cpu time (their names show shader compile, audio or gfx threads),
    memory, pressure-stall and sensor numbers. A main thread asleep (S, futex or ppoll) with no busy thread means a wait or
    timeout; a main thread running (R) or a busy thread means real work (emulation overhead, shader compile, GC).
  - The startup `system:` line gives the gpu, graphics API, cpu, ram, vsync and quality; the `sensors:` line says whether
    temperature, cpu clock and gpu load are readable and how many threads /proc shows (0 means /proc is not readable).
- Parser and probe were tested locally on Linux with a fake stall (`ProcInfo.ParseStat` handles names with spaces and
  brackets). Exited threads drop out of /proc, so their cpu is not counted. Whether Wine can read /proc, /sys and
  the Adreno gpu files on the Thor is untested.
- Next: read the traces from the cutscene and the waterfall in DroidDeck, then in GameNative.

## Status (v0.6.6, v0.6.4 and v0.6.5 changes included)
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

## v0.6.12 trace result (John's test: voices off, 3DS options off, top 60 fps, game 30 fps, frame gen on)
- Normal play: ~25 fps, update ~10 ms, render ~3 ms, present ~21 ms, gpu busy ~23 %, GC ~0, no memory pressure, 74-83 C.
- Stalls (1.5-2.2 s frames) sit in the **present** phase. During them gpu busy drops to 1-5 % and cpu PSI drops too, so
  the game is waiting on something outside it (swapchain/driver/compositor), not computing. Max stall is always about 2 s,
  which looks like a timeout.
- Stalls happen with the copy gone (fights) and without the mod DLL, so the mod is not the cause. Temperature is the same
  during stalls as during smooth play, so not throttling.
- Waterfall: a 50 s collapse (~2 fps) at world pos about (-1514,34,-1073) to (-1489,32,-1044), repeatable by position.
- Renderer is OpenGLCore via Zink on Turnip (Vulkan). Ideas: force DX11 (DXVK) with -force-d3d11, other Turnip/Mesa, A/B frame gen.
- Per-thread probe is blind on his Wine (/proc/self/task unreadable).
- Candidates for v0.6.13: render the copy camera on demand; strip layer 31 in OnPreCull.

## v0.6.13: capture off the main thread
- John fixed the freezes with default GPU drivers, CachyOS proton, game 30 fps, screen 60 fps, no frame gen. With the map mod on he still sees micro stutters.
- Cause found in the old logs: `capture` averaged 26 ms (max 37) every 0.25 s on the main thread (ReadPixels + EncodeToJPG).
- v0.6.13: main thread only does ReadPixels + GetRawTextureData; a worker encodes with `JpegEncoder.cs` (baseline 4:4:4, checked against PIL). A busy worker drops the new frame. `perf ms:` line now shows `capture` (main), `encode (worker)`, `dropped`.
- Next if stutter remains: ReadPixels is a GPU sync, so lower the capture rate or size; Relayer() allocates every frame and could be throttled.

## v0.6.13 result and v0.6.14
- v0.6.13 log: freezes gone (one 1.1 s hitch). Main-thread capture dropped from 26 ms to about 9 ms. The worker encode took about 390 ms under Wine, so more than half the pictures were dropped (about 2.5 fps on the second screen).
- Every 3 s trace window still showed one frame of about 200 ms, even with no copy, so the trace itself (the /proc and /sys reads plus the log write) is the suspect.
- v0.6.14: encoder uses a flat-table DCT and 4:2:0 chroma (about 4x faster in a local test); healthy trace windows are logged only every 30 s, and windows with average over 45 ms or a frame over 500 ms are logged in full.

## v0.6.14 result
- John: plays very smoothly. Log: no hitch over 0.75 s after the first scene load (copy none); fights start without a stall; fps 26-29 (game capped at 30); `capture` on the main thread avg 9 ms (max 15-30) every 0.25 s; `encode (worker)` avg about 103 ms (max 172), `dropped` 0, so the bottom screen gets all 4 pictures a second.
- Only the Latest.log was created this time (no MelonLoader folder log); not a problem.
- Remaining cost is the 9 ms main-thread readback four times a second: roadmap item 2 (capture less when nothing changes).

## Roadmap (updated after v0.6.14; John's rule: get it playable first, experiment later; one fix per release, tested on the Thor)
Setup that plays well for John: default GPU drivers, CachyOS proton, game 30 fps, screen 60 fps, no frame gen. The big freezes were the emulation stack, not the mod.

### Now: make it smooth and reliable
1. Test v0.6.14 (faster worker encode, quieter trace). Check `perf ms:` shows `encode (worker)` under about 250 ms and low `dropped`.
2. Capture only when something changed (hero moved, map scrolled, icon toggled, touch) instead of 4 snapshots a second. Standing still then costs nothing. If stutter remains, also throttle `Relayer()` (allocates every frame) and look at the GPU readback.
3. Pink map at fight start: the last snapshots were taken while the game was already unloading the map background; the bottom screen keeps its last picture. Hold back the last half second of snapshots, or show a clean placeholder when a fight starts.
4. Strip layer 31 in `OnPreCull` so a new copy can never flash on the top screen (the post-fight flash).

### Next: playable features
5. Make the game map the default dual-screen option and auto-start it when a game loads.
6. Live hero marker: confirm the game moves it inside the copy; town/field auto-switch check; minimap-off check.
7. Crop the picture to just the map; hide the zoom, Icons Off and close buttons.
8. Name the views the way the game does (area map, world map); read the game's own labels.
9. Touch pan and zoom, icons on/off, hide icons for unvisited places.
10. Poll John's Discord for wishes before v1.0.

### Later: expand the mod
- Modular options: map only, item menu, party status, battle menus on the bottom screen.
- Move the mod to its own repo for Nexus and rebase this branch onto the new fork `main`; add an AI-authorship note to the fork README (upstream rejects AI-authored commits).
- Backburner: hide the MelonLoader startup console or show a splash while the game loads.
- GameNative second-screen question (not researched yet).

### Experiments (only if we need more speed or want to expand)
- A: raw pictures instead of JPEG over shared memory/files (removes the encode, keeps the GPU readback).
- B: second Unity display. Write a tiny test mod that logs `Display.displays.Length` and tries `Display.displays[1].Activate()` with a camera on `targetDisplay = 1`. If Wine reports a second display, the game could draw the map into its own second window and the DroidDeck compositor could put that window on the bottom screen: no snapshots. Unknown whether Wine reports a second monitor.
- C: share the GPU image directly (dma-buf / hardware buffer) between the game's compositor path and the second screen. Needs a read of the app's graphics code first.

## Tooling notes
Game DLLs are user supplied (not in the repo). IL was read with a small Mono.Cecil dumper (`IlDump`, `IlCallers`)
because `monodis` crashes on Assembly-CSharp. Stop-hook messages about unpushed commits refer to a different clone
(the upstream scratch copy), not this fork.
