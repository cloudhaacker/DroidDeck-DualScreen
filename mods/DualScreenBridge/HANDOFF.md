# DualScreenBridge: project state and handoff

Read this first when picking the project up in a new session.

## Current state (2026-11-08, mod v0.6.40; read this block first)
- Repo `cloudhaacker/DroidDeck-DualScreen`, branch `dual-screen-map`. CI "Build APK" is the only compile check for the app; the mod builds with `build.sh` (needs the game's Managed folder and MelonLoader folder).
- Working and tested by John: auto-open of the game map when the mod connects, start page setting, Quit tile, crop of the map picture, Area/World icons, touch pan/zoom, hero marker following in towns and dungeons (v0.6.28), faster pictures (parallel JPEG encoders, change-driven capture, minimap d-pad toggle blocked).
- v0.6.32 tested by John: field map, world map, towns and dungeons all follow the hero; log clean (no warnings; re-layout about every 0.44 s while walking; encode about 82 ms, no dropped frames).
- v0.6.33/34 tested by John (2026-11-07): the idle screen and boot credit show on the Thor; place names work ('Farebury: Church', 'Farebury', '(Field)', 'Waterfall Cave: Level 1').
- v0.6.35 tested by John (2026-11-08): brick backdrop shows right away at boot and looks good; place names show over it (Farebury: Church, Woods during the new-game cutscene, then Farebury). Problems found: (1) buildings other than the church and pub, and the well, freeze the map (fixed in v0.6.36); (2) 'Made with MelonLoader' flashes for about a second after Continue from the title (queued for v0.6.37); (3) going back to the main menu leaves the last map picture on the bottom screen instead of the brick (queued for v0.6.37).
- v0.6.36 tested by John: building freezes are fixed (map rebuilds), but entering and leaving buildings takes many seconds to settle.
- v0.6.37 tested by John: much snappier. Log: the copy is rebuilt about 2.7 s after the player became free (was about 6 s); pink rebuild fires after 1.0 to 1.2 s. Remaining oddity: the bottom map pops to the middle for a moment on the buildings that go pink. John's church save has a king text cutscene right after leaving Farebury toward the waterfall cave: use it to test any shorter settle.
- v0.6.38 tested by John: the bottom map still pops to the middle on ENTERING buildings and the well (never on leaving), then brick with text, then the map with the star. The v0.6.38 view-hold did not help.
- v0.6.39 tested by John: still snaps to the middle on entering. Log: `hero marker changed` fires 46 ms AFTER `player free: True`, so the swap is too late to help; the rebuild-on-swap works (rebuild about 0.2 s after the free flip).
- Built but NOT yet tested: v0.6.40 (DLL only): no pictures are captured or sent while `PlayerFree` is false.
- Hard rule: `SettleSeconds` stays 2.5 s. At 1.0 s the copy appeared just as a scripted scene started (loading a save inside a church) and the game never received input; the map was black. Idea if John wants faster: use the long wait only for the first copy after a save loads and about 1.5 s after later map changes, tested with the church and doors.
- The item-menu world map looks different because the game's toolkit (`MapUi`) replaces it with its own full-screen map. The copy deliberately uses the stock map, so they will differ.
- Next on the roadmap, in order: v0.6.41 idle-screen fixes (credit flash after Continue; brick when returning to the main menu); then maybe a shorter settle for map changes while the player stays in the game (see the v0.6.37 section); per-game bottom-screen choice in the app (remember the panel per game via `SessionState.gameId`); icons on/off and hiding icons for unvisited places; in-game mod options menu (idle behaviour: return to the minimap-style view after a while, or keep the user's zoom); later modular options (item menu, party status, battle menus); poll the Masterpiece Reborn Discord before v1.0.
- Working agreements: one fix per release, John tests on the Ayn Thor and sends `MelonLoader/Latest.log` plus screenshots, Claude replies in plain language, sends the DLL named `DualScreenBridge-verX.Y.Z.dll`, commits and pushes, and updates this file every release. `AGENTS.md`: comments rare and factual.

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
5. Leave any part on the game's menu layer 8: all parts are moved back to the copy's own layer (30 or lower since v0.6.18; 31 belongs to the toolkit minimap camera) just before each camera culls.
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

## v0.6.14 second result and v0.6.15
- John: still smooth. Pink map at every fight in the waterfall dungeon (town/dungeon map, mode 2), not on the world/field map; settles after the fight.
- Log timeline (every fight): `player free: False (walking=False mode=1)` then `camera state changed` about 1 s before `battle: True`; the copy is removed at `battle: True`. So the copy keeps drawing for up to a second while the game starts the encounter, and the bottom screen shows the last (pink) picture.
- v0.6.15: the worker samples a grid of pixels; if 20 % or more is magenta the picture is not sent (the last good one stays) and `skipped a pink map picture (mean rgb ...)` is logged (12 at most). `perf ms:` shows `pink skipped`. Each `player free:` line shows the mean colour of the last picture, to tune the test if pink slips through.
- John also finds the 4 s settle delay long now that the game is faster; candidate for the next release (try 2.5 s).

## v0.6.15 result (two saves: waterfall cave "a", first town to waterfall "b")
- John: both run well, no pink map seen. Log: `skipped a pink map picture (mean rgb 144,10,142 magenta 52%)` fires 2-8 times at each dungeon (town/dungeon map) fight, starting when the camera state changes, before `player free: False`; field-map fights show no pink. Filter works. `perf ms:`: capture avg 9 ms main thread, encode (worker) 100-142 ms, dropped 0.
- Only first scene loads hitch (0.8-1.6 s, no copy): the game loading.
- New small bug: after a fight the game's own minimap comes back on the top screen until the copy is rebuilt (4 s settle), then it disappears. The mod has no minimap code, so something the game does when the copy exists hides it.
- IL notes (Assembly-CSharp): in-game minimap is `ButtonPanelWindow.miniMap` (`MiniMap`, tweened, static `guidmap.isMiniMapOpen`); the DQ8MR toolkit class `DQ8MR.Mods.Toolkit.FieldHud` (Tick/LateTick/ToggleMiniMap/HitMinimap, `Hidden` list, `Visible`) also manages it. Not yet found what hides it while the copy exists.
- Plan, one release at a time: (1) v0.6.16 settle delay 4 s -> about 2.5 s plus a log line with `guidmap.isMiniMapOpen` and `FieldHud.Visible` at copy create, copy remove and battle end, to find the minimap trigger; (2) fix the minimap flash; (3) adaptive capture rate (roadmap item 2); (4) strip layer 31 in OnPreCull; then the playable features list below.
- IL tools: `tools/IlDump.cs` (dump a method), `tools/IlFind.cs` (find types/references); build with mcs against MelonLoader's Mono.Cecil.dll.

## v0.6.16: shorter settle and minimap logging (built, untested on device)
- `SettleSeconds` 4 -> 2.5. No other behavior change.
- `GameApi.MiniMapState()` is appended to three log lines: `showing: ...` (copy created), `map copy removed: ...`, and `battle: True/False`. It prints `guidmap.isMiniMapOpen` and the toolkit `FieldHud` statics `Visible`, `_xHidden`, `_noMapHidden`, `MinimapSettling` and `Hidden.Count`. Each value falls back to `?(ExceptionName)` if reflection fails.
- `FieldHud` also has `HitMinimap`, `ToggleMiniMap`, `MinimapCorner`; `_noMapHidden` and `MinimapSettling` are the first suspects for the post-fight flash.
- To read: compare the line at `battle: False` (flash visible) with the one at `showing:` (flash gone). The field that flips there is the trigger. Also check `player free` and `showing` timing with 2.5 s: any hang on save load or in the king cutscene means the delay is too short.
- Test saves: waterfall cave and first town to waterfall, plus a church save.
- Next: fix the minimap flash from that log, then adaptive capture rate, then strip layer 31 in `OnPreCull`.

## v0.6.16 result and v0.6.17 (HUD watch build)
- v0.6.16 on device (church save with king cutscene and two fights, waterfall cave and a fight, then a cave save): 2.5 s settle works, nothing hangs, only one hitch (1.4 s, first scene load, no copy). Copy is rebuilt about 3 s after `battle: False`. Pink filter fine (7 and 11 skipped at the dungeon fights).
- Minimap flash is not caused by the copy: `isMiniMapOpen=True`, `Visible=False`, `_xHidden=True`, `_noMapHidden=True`, `MinimapSettling=False` are identical with and without the copy. Only `FieldHud.Hidden.Count` changes: 15 when the HUD is hidden (already 15 in the same frame the copy is created, so the toolkit hid it first), 0 at `battle: True` (FieldGUIManager gone, list cleared), 8 at `battle: False` (flash), 15 again by the time the copy exists.
- IL (`FieldHud.Tick`): the toolkit hides the HUD under `panelList[2]` (`HideUnder`) only while `Visible` is false and `Replace.On && FieldGUIManager.IsControl && MainLoopMode==1 && MainViewMode==0 && !OverlayGate.CursorBlocked/DialogBlocked && !GameBridge.MenuOpen && !MessageWindowManager.IsOpened && !LoadingSlime.IsRender && !UICamera.IsBlockInput && !FreeCamera.Active && !ScriptFunc.IsPartyTalkMode`; otherwise it restores (`Restore`) when `Hidden.Count > 0`. So after a fight the game's HUD, minimap included, is shown until all of those hold again. Likely the same without the mod; A/B test: `DualScreenBridge.nocopy` flag file, fight, look at the top screen.
- v0.6.17 logs `hud watch start: battle start|battle end|map N` and then every change of `hidden=N` plus each of those inputs (`GameApi.HudInputs`), polled every 0.1 s for 15 s (200 lines at most). Battle edge is detected every 0.25 s in `BridgePump.WatchHud`. No behavior change.
- Next: read which input is false while `hidden` is 8 after a fight. If it is a game or toolkit state we cannot or should not touch, hide nothing and accept it (or hold the settle delay shorter); only act on the minimap if the mod itself causes it.

## v0.6.17 result and v0.6.18 (minimap fix)
- Correction: the v0.6.16 conclusion that the copy does not cause the minimap flash was wrong. John ran v0.6.17 with and without the `DualScreenBridge.nocopy` flag: with no copy the game's minimap on the top screen is never removed. `FieldHud.Hidden` counts (15) and every `HudInputs` value are the same in both runs, so they say nothing about the minimap.
- Cause: the toolkit draws the minimap with its own camera `DQ8MR minimap camera` (`FieldHud.EnsureOwnCamera`, depth 35.99, culling mask `1 << _ownLayer`). `FieldHud.FindFreeLayer` and `HeadlessMap.FreeLayer` both took the highest unnamed layer, 31. `HeadlessMap.Isolate` then cleared bit 31 from every other camera, so the minimap camera's mask became 0x0 (visible in the `before camera` dump). After a fight the game rebuilds its HUD and the toolkit builds a fresh camera with a correct mask, which shows the minimap until the next copy is created and `Isolate` strips it again (about 3 s). Same after a map change.
- v0.6.18: `FreeLayer` starts at 30 and skips layers any camera explicitly draws (mask not -1), mirroring the toolkit's rule. Layer 31 is never touched. Expected in the log: `map copy created: ... layer=30`, and `DQ8MR minimap camera` keeps `mask=0x80000000` in the camera dumps.
- Not checked: whether the toolkit's minimap object (parked on its layer) was also being drawn into the bottom picture; look at the bottom screen for a stray minimap graphic.
- HUD watch logging from v0.6.17 is still in (200 lines at most); drop it once the flash is confirmed gone.
- Next: adaptive capture rate (capture only when something changed), then the roadmap features.

## v0.6.18 result and v0.6.19
- v0.6.18 on device: broke the mod. Every create attempt logged `map: no free layer` and nothing showed on the second screen. Layers 8 to 30 are all named or used by the game; 31 is the only unnamed layer, which is why both the toolkit and the copy took it. Assuming 30 and below were free was wrong and was not checked against the game's layer names.
- v0.6.19: `HeadlessMap.FreeLayer` scans once per copy (retry at most every 15 s): counts scene `GameObject`s per layer and cameras that explicitly draw each layer (mask not -1), and picks the highest layer from 30 down with no objects and no explicit camera. Layer 31 is never used by the copy. Logs `layer scan: picked N 'name'`, or `layer scan: none free (layer:name:objects:cameras ...)` with a table for 8 to 31.
- Risk: the game may put objects on the picked layer later; `Isolate` would then hide them from cameras that draw it while the copy exists. Battle destroys the copy anyway. Check the bottom picture for stray objects.
- If the scan finds nothing, the table in the log shows what uses each layer; the next option is to share layer 31 and make `Isolate` skip `DQ8MR minimap camera`, which needs another way to keep the toolkit camera from drawing the copy on the top screen.
- Expected on device: `layer scan: picked ...`, `map copy created: ... layer=<N>` with N below 31, the toolkit minimap camera keeps `mask=0x80000000`, and no minimap flash after fights.

## v0.6.19 result and v0.6.20 (minimap closed by default)
- v0.6.19 on device: same failure as v0.6.18, `layer scan: none free`. The scan counted every camera whose mask is not -1 as drawing a layer, but several cameras (for example `CloneCamera`, 0x7FFFFFFF) include nearly all layers, so no layer ever passed. Layers 9, 11-18, 20, 21, 23, 25-27 and 30 had 0 scene objects at the time; layer 31 held 227 to 314 objects (the toolkit's minimap parts) and one camera. A smarter filter (ignore cameras with 28 or more bits set, since `Isolate` strips them anyway) is possible but was shelved.
- John's call: minimap on the back burner. v0.6.20 reverts `HeadlessMap` to the v0.6.17 layer code (highest unnamed layer, 31; identical to the v0.6.17 file) and closes the game's minimap once instead.
- The toolkit's d-pad right toggle (`FieldHud.ToggleMiniMap`, `InputBindings` action 17) reflects `ButtonPanelWindow.PressMap` and `PressCloseDirectMap`: if `guidmap.isMiniMapOpen` it invokes `PressCloseDirectMap` on the HUD's button panel, else `PressMap`; it only acts when `dispObjPatturn & 8194 != 0`, and plays system SE 41.
- `GameApi.CloseMiniMap` does the close branch (no sound). `Poll` calls it once per load, after `player free` has held for `SettleSeconds`, and retries every poll while the result is a `no ...` one (HUD not ready, or no minimap in this view). Log: `minimap close: closed | already closed | no field gui | no controller | no button panel | no minimap in this view (display pattern N) | failed: ...`. `miniMapDone` resets when the player disappears (title screen, new load).
- Opt-out: a file named `DualScreenBridge.keepminimap` next to the game's exe leaves the minimap alone.
- Unknown until tested: whether the game reopens the minimap after a fight or map change (`isMiniMapOpen` is static and `ButtonPanelWindow.PostCreate` reads it, so it should stay closed). The existing `battle: False` log lines print `minimap: open=...`.
- With the minimap closed the toolkit minimap camera has nothing to draw, so the layer clash with `Isolate` no longer shows. If John reopens it with d-pad right while the copy exists it stays invisible; that is the old behavior.

## v0.6.20 result and v0.6.21 (hide the minimap camera, keep the minimap open)
- v0.6.20 on device: `minimap close: closed` once after the load, `isMiniMapOpen` stayed False through map changes and three fights, no minimap on the top screen, layer 31 as in v0.6.17. Regression: entering the waterfall cave (map 5, key 5/2/-1) the copy showed Farebury's label and icons on a black background (`last map picture rgb 11,11,11`; v0.6.17 had 96,77,53 for the same map). Farebury, the field map and the world map were fine.
- Cause: `MiniMap.StatusChangeMapInit` calls `guidmap.InitGuidMapEach`, which loads the per-area map data the copy reads (`GUIMap.EndMap` does the same for the real map). With the minimap closed that init does not run on an area change, so the copy read the previous town's data. Closing the minimap logically is therefore wrong while the copy is in use.
- v0.6.21: removed `GameApi.CloseMiniMap` and its poll block. New `MiniMapHider` sets the culling mask of `DQ8MR minimap camera` to 0 in `Camera.onPreCull` (looked up once per camera instance), only while the bridge is connected and neither `nocopy` nor `keepminimap` files exist. The minimap stays open in the game, so its init keeps loading map data, and the toolkit's camera rebuilds after fights or map changes cannot flash it because the mask is cleared before every cull. The mask-0 camera still clears depth, as the mask-0 camera did in v0.6.17.
- `DualScreenBridge.keepminimap` now means: leave the minimap camera alone (minimap visible on the main screen, but the copy still strips layer 31 from it while the copy exists, as before).
- Expected on device: no minimap on the top screen at any time (including right after fights and in the cave), the cave map shows the cave, layer=31 in `map copy created`, `DQ8MR minimap camera` mask 0x0 in camera dumps.
- Open: the game's d-pad right toggle still changes `isMiniMapOpen`; if John closes it by hand the cave-data problem returns. HUD watch logging is still in.

## v0.6.21 result and v0.6.22 (cleanup)
- v0.6.21 on device: minimap never seen on the top screen, correct maps in Farebury, the field and the waterfall cave. `DQ8MR minimap camera` kept mask 0x0, `isMiniMapOpen` stayed True. Flash fixed.
- v0.6.22: removed the HUD watch (`WatchHud`, `StartHudWatch`, `GameApi.HudInputs` and their fields). `GameApi.MiniMapState` stays; it is still appended to the `showing:`, `map copy removed:` and `battle:` lines. No behavior change.
- Next, in order: capture only when the picture changed (stutters), default the Game map option on and auto-start it, then icons on/off, touch pan and zoom, hide icons for unvisited places.

## v0.6.23 (skip unchanged pictures; built, untested on device)
- Why: v0.6.21 logs show about 4 captures per second, each JPEG encode on the worker averaging 140 ms (about half a core all the time) and each capture a 9 ms main-thread read (max 15 to 44 ms), even when the map was not changing. Likely the remaining micro stutters.
- `Fingerprint` hashes every 6th pixel of the raw picture in the worker. If it equals the last sent picture's hash the encode and send are skipped (`unchanged skipped N` in the `perf ms:` line). After 3 unchanged results in a row the capture interval goes from 0.25 s to 0.5 s; any changed picture or app command (`unchangedRuns = 0`) goes back to 0.25 s. `lastSentHash` is reset when the app reconnects so a new connection gets a picture.
- Limits: an animated map cursor or icons keep the picture changing, so the saving may be small while one is on screen; the first change after a still period can take up to 0.5 s to show. Pink pictures are still dropped before the hash check.
- To read in the log: `capture` count per window (expect about half while standing still), `encode (worker)` count, `unchanged skipped`, and the trace lines' `fps` and `max` frame time while standing still and while walking.
- If the saving is small: next options are a lower capture resolution, a cheaper encoder setting, or encoding only the changed rows.

## Roadmap (updated after v0.6.14; John's rule: get it playable first, experiment later; one fix per release, tested on the Thor)
Setup that plays well for John: default GPU drivers, CachyOS proton, game 30 fps, screen 60 fps, no frame gen. The big freezes were the emulation stack, not the mod.

### Now: make it smooth and reliable
1. Test v0.6.14 (faster worker encode, quieter trace). Check `perf ms:` shows `encode (worker)` under about 250 ms and low `dropped`.
2. Capture only when something changed (hero moved, map scrolled, icon toggled, touch) instead of 4 snapshots a second. Standing still then costs nothing. If stutter remains, also throttle `Relayer()` (allocates every frame) and look at the GPU readback.
3. Pink map at fight start: the last snapshots were taken while the game was already unloading the map background; the bottom screen keeps its last picture. Hold back the last half second of snapshots, or show a clean placeholder when a fight starts.
4. Strip layer 31 in `OnPreCull` so a new copy can never flash on the top screen (the post-fight flash).

### Next: playable features
5. DONE (app, tested 2026-11-07): the game map opens on the bottom screen as soon as the mod connects (`MapFeed.onConnect`, `SessionActivity.gameMapConnected`); picking another panel by hand turns it off for that session.
6. Live hero marker: confirm the game moves it inside the copy; town/field auto-switch check; minimap-off check.
7. Crop the picture to just the map; hide the zoom, Icons Off and close buttons.
8. Name the views the way the game does (area map, world map); read the game's own labels.
9. Touch pan and zoom, icons on/off, hide icons for unvisited places.
10. Poll John's Discord for wishes before v1.0.

### App features John asked for (fork only, no mod change)
- A. Start page setting: choose which tab DroidDeck opens on (default Steam; John wants Library, because opening Steam makes the game start behind it and slows launch). The tab is `selected` in `FrontEndScreen.kt` (starts as "steam", or "games" for the shortcut picker); add a stored preference and a Setup row. Library tab key is "games".
- B. Quit DroidDeck: a Quit button in settings (and maybe back at the root screen). Must not kill a running game session silently; confirm first, then `finishAffinity()` plus stop the session service.
- Both are app-only; ship them in one APK.

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

## Upstream sync and setup lessons (2026-11-06/07)
- The fork was synced to upstream 0.6.9 on branch `sync-upstream-2026-11-06` (not yet merged into `dual-screen-map`). CI "Build APK" passed; John installed it and the Game map mode works with the new bottom bar.
- After the update the bottom screen waited on 127.0.0.1:47200 and `Latest.log` stopped changing: MelonLoader no longer loaded because the `WINEDLLOVERRIDES=version=n,b` setting was gone. Put it in Game environment, profile **All Proton games** (shared values are merged with the Windows-component overrides). A per-game `WINEDLLOVERRIDES` REPLACES the merged value, which also drops the .NET and Media Foundation overrides.
- Videos failed and the new-game intro hung: CachyOS Proton's GStreamer libav plugin could not load (`libvpx.so.9`, `libspeex.so.1`, `libjpeg.so.62` missing), so there was no H.264 decoder. Nothing to do with the mod. John reset his profiles to defaults and the videos play again; the stutters went away at the same time (cause not isolated, likely the Proton/profile state).
- v0.6.23 tested after the reset: connects, map copies in town, field and the cave map, minimap never shown, no big hitches, world map ~28 fps, unchanged-picture skipping works (`unchanged skipped` counts rise).
- Next: merge the sync branch into `dual-screen-map`, then roadmap item 5 (Game map default and auto-start).

### More requests from John (2026-11-07)
- Start page and Quit tile shipped and tested (app only, `dual-screen-map`, build 42399178800).
- C. Per-game bottom-screen choice (app): remember the bottom-screen panel per game (game map, keyboard/trackpad, none) instead of one global choice, so DQ8 defaults to the game map and other games do not. `SessionState.gameId` identifies the game; the global `secondScreenPanel` pref is what is used today. The map auto-open (`gameMapConnected`) already only fires for games whose mod connects, so other games are safe now.
- D. Idle picture on the bottom screen (mod + app): show something while there is no map (boot, new game before Farebury, settling, areas without a map). Ideas: MelonLoader credit on boot; then the current location name from the game. DQ8 has no named chapters. Do not ship the Square Enix logo or other game art in the mod; use text, or what the game itself shows.
- Order: crop and polish first, then D, then C, then touch pan and zoom and icons on/off.

## v0.6.24 (crop and polish, step 1: inventory)
- Diagnostic only. `HeadlessMap.DumpWidgets()` logs every visible NGUI widget of the copy once per copy (first four copies per session) as `widget: parent/name [UISprite|UILabel|UITexture] x0,y0 - x1,y1 depth=N sprite=... / text=...` in pixels of the 720-wide bottom picture, from the top left. Header line `widgets: N in the copy, picture WxH`.
- Plan: from that list pick the crop rectangle (map only) and the widgets to hide (close button, zoom, Icons Off, arrows). John to send the log plus bottom-screen screenshots of the area map, the world map and the field map.

## v0.6.24 result and v0.6.25 (crop)
- v0.6.24 log: `widgets: N in the copy` printed but no `widget:` lines. This NGUI is old (UIWidget has `relativeSize`, `pivotOffset`, no `worldCorners`), so every widget threw and was skipped silently. v0.6.25 rewrites `DumpWidgets` for it (centre from the transform, size = lossyScale x relativeSize) and counts failures.
- Screenshots (area map Waterfall Cave and Farebury, field map) fit the picture 720x1080 at scale 0.861. Map paper spans about y 226-764 and x 9-711 of the picture; zoom buttons about y 795-865; the Icons Off / World Map / close bar y 970-1080.
- v0.6.25: `ReadRaw` keeps rows 0.19 to 0.727 of the picture height (full width, about 720x580) so the buttons are cropped out without touching the widgets. `DualScreenBridge.nocrop` next to the exe turns the crop off.
- App: Area and World buttons use icons (Map, Public) because the upstream bottom bar draws icon-only buttons (the text-only ones showed as empty circles).
- Next: John's wish for a default zoom close to the minimap (follows the hero, icons uncovered as you go), user zoom, and later an in-game options menu (return to minimap zoom after idle, or keep the zoom). That belongs to touch pan and zoom.

## v0.6.25 result and v0.6.26 (zoom and pan)
- v0.6.25 on device: crop good, Area/World icons good, no clipping on the 3 known maps. Widget inventory worked (see log): area map paper is `T&D_Map(Clone)/map/Sprite_map` (0,210 size 720x574 in picture px), name label `map/Label_name` (the "Farebury" / "Waterfall Cave: Level 1" text is part of the picture and stays), buttons `Map_Top(Clone)/btn_02` (Icons Off, `_buttonPillowEnboss`), `btn_Plus`/`btn_Minus` (zoom), bottom bar `_bar02`, close `CloseButton/Close_Btn(Clone)`, hero marker `icon/icon_cursor/map_cursor` (pivot Bottom; parked at x=1148 or y=-22000 when not shown), visited-place icons `map/icon/<name>(Clone)`.
- App's own title text ("DQ8MR | field map") overlapped the map; removed in the app.
- v0.6.26: the copy's camera zooms and pans (`HeadlessMap.ApplyView`). Start view zoom 2 following the hero (`map_cursor`), clamped inside the sent rows; zoom 1..4. Commands from the app: `zoom:<factor>`, `pan:<dx>,<dy>` (fractions of the picture), `view:reset`. Gesture commands are not logged. `DualScreenBridge.zoom1` next to the exe starts at zoom 1.
- App: `SessionMapView` handles drag (pan), pinch (zoom), double tap (reset); sends at most about every 70 ms.
- Unknowns for the test: whether the picture really follows the hero in towns and on the field map, how laggy it feels (pictures arrive about 4 per second), and whether the hero marker is found in every view.
- Later: idle behaviour options (return to the start view after a while, or keep the user's zoom), in-game options menu, hide icons for unvisited places.


## v0.6.26 result and v0.6.27 (follow the hero, faster touch, change-driven pictures)
- v0.6.26 result: touch pan/zoom works but the view stayed centred (the copy never placed `map_cursor`, parked off-picture) and touch felt slow (0.25 s capture, encodes dropped).
- v0.6.27: `HeadlessMap.PlaceCursor` runs every 0.2 s: `GetPlayerInfo`, store `playerPos/playerRot` (static for GUIMapTownAndDungeon, instance for GUIMapWorld), then `SetCursorDungeon`/`SetCursorTown` (guidmap mode 1/2) or `SetWorldCursor`, inside OwnDepth. Logs "hero marker: ..." up to 6 times per copy. Untested: check that the position lands inside the picture.
- Pictures are change-driven: `HeadlessMap.dirty` is set on creation, gestures, view changes and hero marker movement; a dirty flag restarts a 0.12 s capture burst until 3 identical reads, then one keep-alive read every 1.5 s.
- `FieldHud.ToggleMiniMap` is blocked while the bridge is connected (prefix `KeepMiniMapOpen`).

## v0.6.27 result and v0.6.28 (hero marker offset)
- v0.6.27 log: PlaceCursor works (marker moves with the hero, e.g. 62,177 -> 37,181) but the game sets world positions as if the map sat at the origin, so the marker landed near (0,0), nowhere near the copy at (20000,20000). v0.6.28 shifts the active marker's parent (icon_cursor, or icon_star when interior) by the root position, picks map_star for interiors, and uses the world path when the copy mode is 1 (v0.6.27 wrongly ran the town path on the field map). Logs "hero at picture x,y" up to 8 times.

## v0.6.28 result and v0.6.29
- v0.6.28 log: town and dungeon follow works. The field map is the game's "local map" (isLocalMap=True, status UpdateLocalMap), so the marker needs `SetLocalCursor` on `bigMapIcon`, not `SetWorldCursor` on `smallMapIcon`; the marker was stuck at the centre.
- v0.6.29: local map uses SetLocalCursor and scans the marker under bigMapIcon; SettleSeconds 2.5 -> 1.0; two JPEG encoders run in parallel (encode averaged 82 ms, which capped the rate near 12 fps and dropped frames), with a sequence guard so older frames never replace newer ones.

## v0.6.29 result and v0.6.30
- v0.6.29 with SettleSeconds 1.0 broke loading a save inside a church: the copy was created 1.25 s after the player first counted as free, the priest's dialogue started about 1 s later (player free False, mode=2) and input never came back. The settle wait is a real guard against building the copy just before a scripted scene; v0.6.30 restores 2.5 s. Do not shorten it without a better signal (for example, wait until the game has been free and no scene has started for N seconds, or until a save-load flag clears).

## v0.6.30 result and v0.6.31 (field map follows the hero)
- v0.6.30 log: on the field map the game's `SetLocalCursor` always puts the marker at (0,0), the centre of the local map; the map tiles are laid out around the hero once, when the window opens (OnUpdate init: GetPlayerInfo, SetSpecificPlayerPos, GetLocalMapSize, GetLocalMapInfo, SetMapDrawItem). So the marker only turned and never moved.
- v0.6.31: RefreshLocalMap repeats that init sequence on the copy when the hero has moved 60+ units (at most every 0.4 s). Untested; watch for flicker, wrong icons, or a log warning "could not place the hero marker". If SetMapDrawItem is too heavy or resets things, fall back to rebuilding the copy after the hero moves a long way, or scale the marker by the hero's offset.

## v0.6.31 result and v0.6.32 (run the game's layout code with the copy at the origin)
- v0.6.31 log: the re-layout ran (positions changed as the hero walked) but the picture did not scroll. `GUIMapWorld.SetLocalMapPos` sets tile and icon positions with `Transform.set_position` (world space) assuming the menu sits at the origin; the copy sits at (20000,20000), so tiles land about 20000 away from the copy camera (the widget dump shows tiles at about -44000 px). `MapUi.Stitch` returns false for the copy, so the stock path runs.
- v0.6.32: `PlaceCursor` moves the copy's root to the origin while the game's layout and cursor calls run, then moves it back, so every world position the game computes ends up right. The old per-marker shift remains as a fallback (it no longer triggers). Untested.

## v0.6.32 result and v0.6.33 (idle picture)
- v0.6.32 on the Thor: the field map scrolls with the hero, town and dungeon maps still follow. Log: `local map re-laid out` lines appear as the hero walks, no warnings.
- v0.6.33 protocol: new message type 4 (mod to app), payload is the place name in UTF-8 and may be empty; it means "nothing to show". The app draws a text picture itself (place name, or "DualScreenBridge" when the name is empty, and "Made with MelonLoader" at the bottom; no logos or game art). Any image message (type 1) turns it off. Older apps ignore type 4. The old dark placeholder picture sent on connect is gone.
- When the mod sends it (resent every poll, about 1 s, so a picture still in flight cannot undo it): no player yet and no map ever shown (boot); `AutoMode` 0 (no map for this area); settling after a map change, but only when the map number differs from the last map shown (after a fight the old picture stays). Battle, the copy itself and the in-game map window do not send it.
- Place name comes from `dq_mapinfo.GetMapTitle(GetMainMapNo())` (the string the game lists for the Lura spell destinations), control characters removed, 60 characters at most. Unverified: it may be empty for some maps or not match the dungeon floor label ("Waterfall Cave: Level 1"). The log prints `place name for map N: '...'` on every map change and `idle picture: place '...'` when the idle text changes. If names are empty or odd, the alternative is `guidmap.DngTitleInfo.DisplayName` (what the map window label uses), which only holds a value after the map window has run for that map.
- App files: `MapBridge.kt` (`MapFeed.idle`, `MapFeed.place`), `SessionMapView.kt` (`drawIdle`), strings `second_idle_mod`, `second_idle_credit`.

## v0.6.34 (title probe and boot-only credit; built, untested on device)
- John's wishes: "Made with MelonLoader" only at boot until the main menu is up; after that an idle picture, ideally the game's own menu background (the brick background or the map behind the main menu), taken live from the installed game and never shipped in the mod or app (nothing from the game in the repo).
- App: the credit ("Made with MelonLoader", with "DualScreenBridge" under it) is only drawn while the place name is empty; with a place name the screen shows just the name.
- Mod: `TitleProbe` (new file) logs, from 8 s after start every 6 s while no player exists, up to 12 changed dumps: active scene and loaded scenes, active root objects, cameras, NGUI widgets of 200+ units or any UITexture (sprite name, texture name and size), up to 30 label texts (to spot the main menu), and renderers with textures of 256+ px. Lines start with `title probe N`.
- To read: which widget or renderer is the brick or the map behind the menu, which texture it uses, and which label texts or scene name appear only on the main menu (the signal for "main menu is up"). Next release: show that picture (probably copied once with Graphics.Blit to a RenderTexture and sent as a background image, or drawn as an original brick pattern in the app if it cannot be reached) under the place name.

## v0.6.34 result and v0.6.35 (brick backdrop; built, untested on device)
- v0.6.34 title probe findings (removed again in v0.6.35): scenes go `Download` (boot, about 18 s) then `DQTitle`, and `TitleNGUI` loads on top about 24 s in; the title screen's map is `BGPanel/TitleBG_wmapA/B` (UISprite `wmap_title`, atlas `title_wmapAtlas` 1536x512, layer 19); the brick background is the `screenBg` camera (depth -100, culling mask 0x20000000 = layer 29) drawing a `_UIDrawCall` with `screen_bg_Atlas` 512x512 (bounds 2.8 x 2.8); that camera still exists during gameplay (seen in the camera logs). The video player draws over it, which is why only the brick sides show during the intro.
- v0.6.35: `Backdrop` renders the `screenBg` camera once into a 720x1080 picture (cleared first; the camera's own target is restored), sends it as message type 5 (JPEG), and the client resends it to every new connection. If the camera is missing for about 30 s or renders black, it falls back to copying the `screen_bg_Atlas` texture. PNG samples are written to `<game folder>/UserData/DualScreenBridge-backdrop-camera.png` (or `-texture.png`) and `-texture.png`; John sends them back if the picture looks wrong. Log lines: `backdrop grabbed from the camera|texture (WxH, brightness N)`.
- Idle message payloads (type 4): `boot` while the game is still booting (title scene not loaded yet), then `place:` plus the place name (empty on the title screen and main menu). The app draws the credit only for `boot`; otherwise the backdrop dimmed with the place name over it (a shadowed white text); no backdrop yet means a plain dark screen.
- `GameApi.LocationName` strips a name wrapped in parentheses: the field map's '(Field)' shows as 'Field'.
- Open: no signal yet for "main menu is open" (the title is treated as up as soon as `TitleNGUI` is loaded); fine for now because the title and the menu both show the backdrop without text.

## v0.6.36 (map freezes inside some buildings; DLL only, built, untested)
- Log evidence (v0.6.35, Farebury): entering houses that stay on map number 1 (and the well) made the copy's picture magenta (87-97%) and it stayed pink until the map number changed (church, pub), so the pink pictures were skipped and the bottom screen kept the last good (frozen) map. `pink skipped` in the perf lines kept climbing meanwhile (32 to 329, then 329 to 671, then to 1121).
- Fix: `pinkSinceTick` marks the first pink picture of a run (worker thread; cleared by any fine picture and when a copy is created). If the copy has been pink for 3 s and 20 s have passed since the last such rebuild, `Poll` destroys it ('pink picture'), restarts the settle wait and clears `shownMapNo`, so the idle screen (brick and place name) shows until the rebuilt map arrives. Log: `the map copy has drawn pink for N s; building it again`.
- Not verified: whether a copy built inside a house stays fine (the church and pub rebuilds did). If it comes out pink again the 20 s cooldown keeps it from rebuilding in a loop; then look at what the game unloads on entering those buildings.

## v0.6.37 (faster rebuild inside and outside buildings; DLL only, built, untested)
- v0.6.36 log timeline per building trip: `player free: False` for about 3 s (the game's own transition), pink copy already; then 3 to 11 s before the rebuild (the 20 s cooldown after the previous rebuild stretched the pink wait: 'drawn pink for 5 s / 11 s / 9 s'); then a 3.1 s settle restart; then create. The copy also goes pink on leaving a building, so every enter and every exit costs a rebuild. Poll ran once a second, so every wait was rounded up to a whole second.
- Changes: Poll every 0.25 s (`PollSeconds`; poll costs about 0.5 ms); pink rule needs the player free and 1 s of continuous pink (fights draw pink for up to about 1 s before `battle: True`, and the player is not free then); the settle timer is no longer restarted by a pink rebuild (the free flip already started it), so the new copy is created 2.5 s after the player became free; the 20 s cooldown is replaced by a loop guard (3 pink copies in a row, each within 8 s of being built, stops rebuilding for 30 s: `rebuilt copies keep drawing pink; not rebuilding for 30 s`).
- Expected: after the game's own transition, the copy is created about 2.5 s later instead of about 6 s; then the usual 0.4 s ready delay and first picture.
- `SettleSeconds` stays 2.5 s (a shorter one broke input when loading a save inside a church). Idea if more speed is wanted: keep 2.5 s after boot, save loads and battles, but about 1.2 s for map changes and pink rebuilds while the player has been in the game all along; risk is a cutscene or message window starting right after arriving. Needs its own release and an input test (arrive in a house where a conversation starts by itself).

## v0.6.38 (map no longer pops to the middle during scene changes; DLL only, built, untested)
- Cause: `HeadlessMap.ApplyView` set the view to the middle of the map whenever `HeroPixel` found no marker (the game parks or swaps `map_cursor`/`map_star` while it changes scenes, and the marker swap clears `cursor`), and those frames were still sent because they are not pink yet.
- Fix: `heroSeen` (reset when a copy is created); with no marker the view keeps its last position, and only a copy that has never seen the hero starts in the middle.
- To check: enter and leave the buildings that used to freeze the map; the picture should hold the old view until the brick screen shows.

## v0.6.39 (hold the picture when the hero marker swaps; DLL only, built, untested)
- v0.6.38 log: pink pictures only begin when the player is free again (`player free: True` then 9 `skipped a pink map picture` lines), but entering takes about 3 s with the player not free, and during that time the old copy keeps being sent. The game flips `GUIMapTownAndDungeon`'s `Interior` flag at that moment, so `PlaceCursor` swaps `map_cursor` to `map_star`; the star on a copy built outside sits at the wrong spot (near the middle) and the view follows it. v0.6.38's view-hold only covered a missing marker, not a misplaced one.
- Fix: `HeadlessMap.MarkerSwapped` becomes true when the wanted marker differs from the first one chosen after the copy was created (log: `hero marker changed from X to Y; holding the picture ...`). The capture loop sends nothing while it is true, so the bottom screen keeps the last good picture; `Poll` rebuilds the copy as soon as the player is free (no 1 s pink wait), same loop guard (3 copies out of date within 8 s of creation stops rebuilding for 30 s). Log: `the map copy is out of date (its hero marker changed | it drew pink for N s); building it again`.
- Kept from v0.6.38: the view stays put when the marker is missing (`heroSeen`).
- To check: enter the buildings and the well: the old picture should stay (no middle), then the brick, then the map with the star. If the middle still shows, the jump happens before `Interior` flips: then freeze on `player free: False` for town maps instead.

## v0.6.40 (hold the picture while the player is not free; DLL only, built, untested)
- Entering a building: `player free: False (walking=True mode=2 message=0)` for about 3 s, then `True (walking=True mode=1 ...)`; the marker swap and the pink pictures only start at the free flip, so the picture sent during the 3 s window (the wrong view near the middle) is not covered by the pink or marker rules.
- Fix: `CaptureLoop` calls `GameApi.PlayerFree` before each capture (about 8 per second, a few reflection reads) and skips the capture while it is false; the previous picture stays on the second screen. Side effect: the map does not follow the hero during scripted walks and message windows (nothing to follow in most of them); it resumes at once when the player is free.
- Kept: v0.6.39 marker-swap rebuild and v0.6.38 view-hold.
- If the middle still shows: the picture is wrong while the player is still free (before `mode=2`), or the jump is not in the picture; then log the first hero positions around the door (the `hero at picture` log shows only the first 8).

## v0.6.41 (loop guard no longer blocks real trips; DLL only, built, untested)
- v0.6.40 log: after exiting the well at 18:18:51 the log said `rebuilt copies are out of date again right away; not rebuilding for 30 s`, so the map stayed stale until 18:19:21 (matches the report). John was making quick back-to-back trips (building, well, building), each rebuilt copy was replaced within 8 s, and three of those tripped the guard.
- Fix: `leftFreeSinceCreate` is set whenever the player is not free and cleared when a copy is created. A rebuild only counts toward the loop guard when no not-free period happened since the copy was built (a real runaway loop has none); a walk through a door resets the streak.
- Known and left alone (own release): the new copy shows centered for a split second before the star; idea is to hold its first frames until the hero marker is placed.
- Next queued: idle-screen fixes (credit flash after Continue, brick when returning to the main menu), shorter settle experiment.

## v0.6.41 result
- John: tested rapid in/out of the well and buildings without letting the map populate; no hang, map caught up afterwards. Log: 9 rebuilds, no loop-guard block, no errors.

## v0.6.42 (idle screen: credit only at first boot, brick on return to the menu; DLL only, built, untested)
- v0.6.41 log: `idle picture: 'boot'` appeared again at 18:37:16, after the title was up (18:36:55 `place:`), because Continue unloads TitleNGUI and `TitleUp()` went false: that was the credit flash after Continue.
- Fix 1: `titleSeen` latches once `TitleUp()` has been true; after that the no-player idle is always `place:` (brick), never `boot`.
- Fix 2: in the no-player branch, if a copy was active or a map number was known, `shownMapNo` is reset so the idle picture is sent again; returning to the main menu now shows the brick instead of the last map picture.
- Bundled two small changes in the same branch; if either misbehaves, they are separate lines in Poll.
- Next queued: hold the first frames of a new copy until the hero marker is placed (centered flash), shorter settle experiment.

## v0.6.42 result and app fix (APK only; DLL stays 0.6.42)
- John: no credit flash after Continue; brick shows after returning to the main menu. Quitting from the main menu flashed the last map for a second before closing.
- Log: the mod sent `idle picture: 'place:'` correctly at 18:47:31 and nothing after, so the flash is app side: on disconnect `MapFeed.connected` goes false while `idle` stays true, and `SessionMapView.onDraw` only draws the idle picture when `idle && connected`, so it fell through to the stale `bitmap`.
- Fix in `MapBridge.kt` (disconnect `finally`): clear `bitmap` and `idle` so the waiting screen shows. Needs a new APK (`gh workflow run "Build APK" --ref dual-screen-map`).
- Next queued: centered flash on new copy (hold first frames until the star is placed), shorter settle experiment.

## v0.6.43 (two changes, John agreed to bundle: no centered flash; shorter settle for map changes; DLL only, built, untested)
- Centered flash: `HeadlessMap.Ready` now also needs `heroSeen` (or `!follow`), so no picture goes out before the star/cursor has been found; the 3 s `Age` fallback still applies. v0.6.42 log: create at 18:47:23.66, first `hero at picture` 24.52, so pictures were sendable 0.4 s after create, before the marker was found.
- Shorter settle: `QuickSettleSeconds` = 1.2 s is used when `settledOnce` (a copy was built since the last boot, save load or fight); `SettleSeconds` 2.5 s stays for boot, save loads (no player) and after battles (`settledOnce` reset in those branches).
- Test: king cutscene when leaving Farebury toward the waterfall cave; any conversation that starts on arriving in a house. If input breaks or the screen goes black, set `QuickSettleSeconds` back to 2.5 (the two changes are independent).
- Next queued: per-game bottom-screen choice in the app, icons on/off / hide unvisited, in-game options menu.

## v0.6.43 result and per-game bottom screen choice (APK only; DLL stays 0.6.43)
- John: tiny centered flash still occasionally, accepted as too minor; king cutscene fine (screen showed field until the map arrived). Log clean (9 copies, no warnings).
- App: new per-game setting "Bottom screen at start" in the in-session drawer, Games page, "Game settings" group (below "Start without Steam"). Choices: Use Default, Dual screen (game map), Keyboard + trackpad, Terminal, Dashboard. Stored in SessionPrefs as `bottomStart:<appId>` (`gameBottomStart` / `setGameBottomStart`), keyed by the selected game profile (same id as the other per-game rows).
- `SessionActivity.openingSecondScreenMode()` now checks the running game's choice (`SessionState.gameId`) first; a non-map choice sets `gameMapDeclined` so the map does not take over when the mod connects; with no choice it falls through to `generalOpeningSecondScreenMode()` (the old behaviour). Dashboard falls back to the general rule when no dashboard pages exist.
- Only affects the screen the session opens with; switching during play works as before. Existing global `secondScreenPanel` memory is unchanged.
- Not compiled locally (no Android SDK here); CI build is the check.
- Next queued: icons on/off + hide icons for unvisited places, in-game options menu.

## Roadmap note (from John, after the per-game bottom screen test)
- Per-game "Bottom screen at start" works. Open item for the next APK change: the general default still opens on Dual screen (game map) whenever a mod connects, which is how this fork behaved before. Most games do not need the second screen, so restore whatever upstream DroidDeck opens with by default (find it in `SecondScreenMode.opening` / `generalOpeningSecondScreenMode` and `gameMapConnected`); the game map should then be opt-in per game via the new row. Not urgent: do it with the next APK-touching feature. Check upstream's behaviour first.
- Next mod feature: icons on/off and hide icons for places not visited yet; then the in-game options menu.

## v0.6.44 (Icons button on the bottom screen; DLL and new APK; built, untested)
- John's choices: a button on the Thor's bottom screen; "hide icons the game itself hides" means copy what the game shows (nothing extra to build; verify in a town with an unvisited shop whether the game's own copy already omits it).
- App: new "Icons" button in the game-map bar (`bottom-map-icons`, active = icons shown) next to Area/World. State `MapFeed.iconsHidden` (Compose state), stored in `SessionPrefs.mapIconsHidden`, sent as `icons:0` (hide) / `icons:1` (show) on click and again on every mod connect (`SessionActivity` onConnect). Needs a new APK.
- Mod: command `icons:0|1` goes through `HeadlessMap.Gesture` and sets static `IconsHidden`. `ApplyIcons` (every 0.5 s in `Tick`) disables UIWidget sprites named map_Well, map_Tool, map_Protector, map_INN, map_G, map_Chrch, map_Bar, map_Arms in the copy and remembers which it disabled to re-enable them. The hero marker (map_cursor/map_star) is not in the list. The static flag survives copy rebuilds.
- To check: tap Icons on a town map: icons vanish within about a second and the button dims; tap again, they return; the choice should survive entering buildings and restarts. World map and dungeons use other sprite names or none, so nothing hides there (expected).
- v0.6.44 APK build 42547409190 failed: `MapBridge.kt` used `by mutableStateOf` without importing `getValue`/`setValue`. Fixed by adding the imports (no behaviour change). Lesson: no Android SDK here, so the CI build is the only compile check for app changes; read the log with `gh run view <id> --log-failed | grep "e: "`.

## v0.6.44 result and v0.6.45 (zoom kept across rebuilds; world cursor in towns; DLL only, built, untested)
- v0.6.44 log (1478 lines, read in full): Icons button works. John's four findings: (1) zoom resets to default when the copy is rebuilt (building, fight); (2) world map opened in a town puts the hero cursor in the middle (log: `hero at picture -20223,22313 (map_cursor)`, marker pos (19997.6, 20061.8, -7.5) in every town world copy, correct in the field); (3) first world map opened after loading in town shows all icons; after a trip to the field it shows only revealed ones; (4) the field "Area" map is only the surroundings and cannot be scrolled or zoomed out; the world map has the whole map.
- Fix 1: `SavedZoom` (static, keyed by `mode/DesiredView` e.g. 2/1 town, 1/1 area, 1/0 world) remembers the zoom per view kind; double-tap reset clears the slot. Pan is not kept (the view follows the hero after a rebuild).
- Fix 2: `PlaceCursor` world branch: `GetPlayerInfo` in a town returns small town coordinates (trace: `pos (-2,2,5)`), in the field about 32000. If max(|x|,|z|) > 2000 the position and rotation are cached in `lastFieldPos/Rot`; otherwise the cached field position is used. No cache exists if the game was loaded straight into a town and the field was never visited this session (cursor stays centered then).
- Still open: (3) world icons showing everything until a field visit (idea: snapshot the active icon widgets of a field world copy and hide the rest in a town world copy, or call the game's `SetMapDrawItem` reset); (4) area map in the field: option to make "Area" show the world map zoomed in on the hero (different look; the local map is a live render around the player).

## v0.6.45 result and v0.6.46 (world view zooms in closer; DLL only, built, untested)
- John: v0.6.45 (zoom kept, world cursor in towns) works as intended. His screenshots of the item-menu map: one fully zoomed in (very close, pixelated texture, star centred), one fully zoomed out (the whole world map). So the game's item-menu map IS the world map texture with a large zoom range, not the local terrain tile map; our World view was capped at 4x (`MaxZoom`).
- Fix: `WorldMaxZoom` = 10 for the world slot (`zoomSlot == "1/0"`); town and area keep 4x. Pinch and drag already work in World, and the zoom is remembered per view.
- The small spot in the black area top right of his zoomed-out screenshot is almost certainly the ship icon (`GUIMapWorld.SetShipPosition`), parked off the map when the ship is not available; not part of what we send.
- Open: option to make field "Area" open the World view zoomed in on the hero (replacing the local tile map, which only shows the surroundings and cannot scroll); world icons showing everything until a field visit.

## v0.6.46 result and v0.6.47 (full scrollable terrain map for the field Area view; DLL only, built, untested)
- John: World zoom-in fine. Correction from him: the item-menu map is NOT the world (parchment) view; it is the same terrain-tile map as our field Area view, but the whole map, scrollable and zoomable. He wants the full area map in the field (and wonders about large towns later).
- Found by disassembling the game (Mono.Cecil from MelonLoader folder; see /tmp/il tools, rebuild with mcs): the game contains a Toolkit mod `DQ8MR.Mods.Toolkit.MapUi` (hint bar "Stick/D-pad: Move, R1: Icons, R2/L2: Zoom, B: Back") that replaces the item-menu map. `GUIMapWorld.SetLocalMapPos` first calls `MapUi.Stitch(GUIMapWorld)`, which for the Toolkit's own window lays out ALL tiles: `Sprite (a00)`..`Sprite (f06)` under `bigMapAll` (letter = row a-f, number = column 00-06), position x = TileSize.x*(col - MapTexPutIndex[0]) + MapTexPutPos[0], y = -TileSize.y*(row - MapTexPutIndex[1]) + MapTexPutPos[1], scale = localMapSize (x,y,1), `Sprite (sea)` hidden, `bigMapIcon` placed at calc_offset with scale 1.5; panel clipping set to None; bounds from `NGUIMath.CalculateAbsoluteWidgetBounds`. It returns false for any other window (ours), so our copy only ever got the vanilla 3x3 block layout.
- Fix: `HeadlessMap.StitchTiles()` repeats that layout in our copy (root moved to the origin while placing, as PlaceCursor does), clears `UIPanel.clipping` once, measures the bounds into picture pixels (`stitchMin/Max X/Y`) and logs `full area map: N tiles missing, bounds ...`. Called after each `RefreshLocalMap` and every 0.5 s in `Tick` (the game's own SetMapDrawItem would put the 3x3 layout back). Only for slot `1/1` (field Area). `ApplyView` then clamps scrolling to those bounds (centred if the view is larger); `ZoomFloor()` lets the zoom go down to fit the whole map; `AreaMaxZoom` 8.
- Towns and dungeons are not affected: those are single full-size artwork (`Sprite_map`), so the whole map is already reachable by pan and zoom there.
- Open risks to check in the log: tile pivot (bounds may be half a tile off), whether all 42 tiles exist (`tiles missing` count), resolution when fully zoomed out (720 px wide picture), clipping removal side effects.
- Still open: world icons showing everything until a field visit; in-game options menu.

## v0.6.47 result and v0.6.48 (full area map placement fixed; DLL only, built, untested)
- v0.6.47 test: at first the field Area map would not zoom out; after a town trip it did, looked right for a moment, then as soon as the hero moved it zoomed in enormously and was off course; the Map_Top buttons (Icons Off / World Map / close, normally outside the crop) showed up in the picture. Log: `full area map: 0 tiles missing, bounds x -27018..-20970 y 22263..26799` (about 6000 x 4500 px and centred about 24000 px from the picture: far too big and too far).
- Causes: (1) the tile positions were set with world `position` (the Toolkit's own screen has an unscaled window; our copy sits under the scaled UI root, so world units came out hundreds of times too large). Now `localPosition` for the tiles and for `bigMapIcon`, no root move. (2) the game's default field view is `DesiredView = -1` (area map) and the slot was `1/-1`, so the full-map logic (slot `1/1`) only started after an explicit `view:area` (hence the zoom out only worked after going to town and back). The slot now maps -1 to 1.
- To check in the next log: the `full area map: ... bounds` line should show a width of a few thousand px centred near the picture (x around 360, y around 540), not tens of thousands.

## v0.6.48 result and v0.6.49 (bounds measured from the tiles; DLL only, built, untested)
- v0.6.48 test: the full area map did not show the hero's location and kept zooming in as he ran, ending pixelated. Log: `full area map: 0 tiles missing, bounds x -26263..-20215 y 22249..26785` (6048 x 4536 = exactly 7 x 864 by 6 x 756, so the tile grid itself was right) but centred about -23200 / +24500 px from the picture: the bounds were in the origin frame while `basePos` is in the parked frame (offset 20000 world units = about 22500 px at unit 0.889). The camera view was clamped into that far-away box.
- Cause: `NGUIMath.CalculateAbsoluteWidgetBounds` returns stale (origin-frame) widget corners right after the root is moved back. v0.6.48's local-position placement did not help (root scale is 1 here: the menu camera is orthographic with size 480, so world units are picture pixels).
- Fix: tile placement goes back to the game's own way (world `position` with the root at the origin, as in v0.6.47) and the bounds are measured from the tile transforms (`position` +/- `lossyScale`/2, assumes centre pivot) after the root is restored. The log line now also prints the tile size and `MapTexPutIndex`.
- Expected next log: bounds roughly x -2000..4000 and y -1500..3000 around the picture, hero near the picture centre (vanilla layout puts the hero's block at the origin).
- Known limit: while scrolled away from the hero (follow off) a re-layout (every 60+ units walked) re-centres the grid on the hero, so the scrolled view can jump. Double-tap returns to follow.

## v0.6.49 result and v0.6.50 (zoom creep while moving fixed; window buttons hidden; DLL only, built, untested)
- v0.6.49 test: the map starts in the right place and looks right, zoom out and double-tap fine, but it zooms in more and more as he moves. Zoomed out, the window's own buttons (Icons Off / World Map / close) show up in the middle of the map.
- Cause of the creep: `RefreshLocalMap` sets `localMapSize` from `GUIMapWorld.GetLocalMapSize()`, which reads tile a00's current scale times 1.5. The full layout rescales a00 to `localMapSize` every time, so each re-layout (every 60+ units walked) made the tiles 1.5x bigger. The vanilla nine-tile layout only touches a00 when the hero is in a corner block. Fix: the size is read once per copy (`baseTileSize`).
- Second bug: `StitchTiles` measures bounds from the tiles, but `RefreshLocalMap` calls it while the root is moved to the origin for the game's calls, so those bounds were in the wrong frame (log still showed x -27450..-21402 although tile size 768x672 and index 5,1 were right). Bounds are now taken relative to the root's parked place (`shiftX/shiftY`).
- `HideWindowChrome` disables the widgets under `Map_Top*` and `CloseButton*` (buttons, bars, the 1440x2556 black backdrop) that are not part of the tiles or icons.
- Check next log: the `full area map:` bounds should be near the picture (x about -2500..3500) and the tile size should stay 768x672 over a walk.
