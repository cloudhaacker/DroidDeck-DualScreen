using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DualScreenBridge.BridgeMod), "DualScreenBridge", "0.6.21", "DroidDeck Dual Screen")]

namespace DualScreenBridge
{
    public class BridgeMod : MelonMod
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        internal static BridgeClient Client;
        internal static readonly HeadlessMap Map = new HeadlessMap();
        internal static volatile bool RealMapOpen;
        internal static float ResumeAt;
        // -1 follows the game's own default, 0 world map, 1 area map.
        internal static int DesiredView = -1;
        internal static string Status = "starting";
        // The copy starts by itself once the bridge is connected and the player is in the field, and gives up if the
        // game keeps closing it; an Area or World tap arms it again.
        internal static bool Armed = true;
        // A file named DualScreenBridge.nocopy next to the game's exe turns the map copy off and leaves only the trace.
        internal static bool NoCopy;
        // A file named DualScreenBridge.keepminimap leaves the game's minimap visible on the main screen while the bridge is connected.
        internal static bool KeepMiniMap;
        internal static int QuickDeaths;
        internal static float RetryAt;

        public override void OnLateInitializeMelon()
        {
            Client = new BridgeClient();
            Client.Start();
            try
            {
                string folder = System.IO.Directory.GetParent(Application.dataPath).FullName;
                NoCopy = System.IO.File.Exists(System.IO.Path.Combine(folder, "DualScreenBridge.nocopy"));
                KeepMiniMap = System.IO.File.Exists(System.IO.Path.Combine(folder, "DualScreenBridge.keepminimap"));
            }
            catch (Exception)
            {
            }
            if (NoCopy) MelonLogger.Msg("DualScreenBridge.nocopy found: trace only, no map copy");
            PatchMap();
            GameObject host = new GameObject("DualScreenBridge");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<BridgePump>();
            StallProbe.Log = delegate (string line) { MelonLogger.Msg(line); };
            StallProbe.Start();
            FrameTrace.Start();
            MiniMapHider.Start();
            MelonLogger.Msg("started, bridge at " + BridgeClient.Host + ":" + BridgeClient.Port);
        }

        public override void OnDeinitializeMelon()
        {
            Map.Destroy();
            if (Client != null) Client.Stop();
        }

        private static void PatchMap()
        {
            try
            {
                Type guiMap = GameApi.Find("GUIMap");
                Type world = GameApi.Find("GUIMapWorld");
                if (guiMap == null || world == null) { MelonLogger.Warning("map classes not found, map copy unavailable"); return; }
                HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("droiddeck.dualscreen.bridge");
                Hook(harmony, guiMap, "StartMap", "SkipOwn", "AfterMapStart");
                Hook(harmony, guiMap, "EndMap", "SkipOwn", "AfterMapEnd");
                Hook(harmony, world, "OnInit", null, "AfterWorldInit");
                // The copy must not switch off the field camera or take over the game's controller input.
                Hook(harmony, guiMap, "FieldOff", "SkipOwn", null);
                Hook(harmony, guiMap, "FieldOn", "SkipOwn", null);
                Hook(harmony, guiMap, "ProcessRetCmd", "EnterOwn", "LeaveOwn");
                // The per-frame state machine runs the copy's window tweens, which lock the game's input.
                Type flow = GameApi.Find("StateFlowBase");
                if (flow != null) Hook(harmony, flow, "Update", "EnterOwn", "LeaveOwn");
                else MelonLogger.Warning("StateFlowBase not found, the copy's per-frame update is not isolated");
                // The world map locks input and grabs the game's menu camera for a fade; none of that may reach the real game.
                Hook(harmony, world, "OnExchangeWorldLocal", "EnterOwn", "LeaveOwn");
                Hook(harmony, world, "AlphaFadeUpdate", "EnterOwn", "LeaveOwn");
                Hook(harmony, world, "ResetMapDrawItem", "EnterOwn", "LeaveOwn");
                Hook(harmony, world, "SetMapDrawItem", "EnterOwn", "LeaveOwn");
                // Every map window paints the game's one shared menu background solid black and restores it on close.
                Type town = GameApi.Find("GUIMapTownAndDungeon");
                if (town != null)
                {
                    Hook(harmony, town, "SetMapDrawItem", "EnterOwn", "LeaveOwn");
                    Hook(harmony, town, "ResetMapDrawItem", "EnterOwn", "LeaveOwn");
                }
                Type background = GameApi.Find("BackGroundUI");
                if (background != null)
                {
                    Hook(harmony, background, "DispStart", "SkipInOwnCall", null);
                    Hook(harmony, background, "DispEnd", "SkipInOwnCall", null);
                    Hook(harmony, background, "SetSolidColor", "SkipInOwnCall", null);
                }
                else MelonLogger.Warning("BackGroundUI not found, the copy will blank the main screen");
                // Both map windows fade the game's main scene out and in while they open; the copy must not, and must not wait for it.
                Type scene = GameApi.Find("dqCScene");
                if (scene != null)
                {
                    Hook(harmony, scene, "FadeOut", "SkipInOwnCall", null);
                    Hook(harmony, scene, "FadeIn", "OwnFadeIn", null);
                    Hook(harmony, scene, "FadeCheck", "FadeDone", null);
                }
                else MelonLogger.Warning("dqCScene not found, the copy will fade the main screen");
                Type captured = GameApi.Find("CapturedBgNGUI");
                if (captured != null)
                {
                    Hook(harmony, captured, "Enable", "SkipInOwnCall", null);
                    Hook(harmony, captured, "Disable", "SkipInOwnCall", null);
                    Hook(harmony, captured, "set_RenderCamera", "SkipInOwnCall", null);
                    Hook(harmony, captured, "SetLocalPosition", "SkipInOwnCall", null);
                }
                Type camera = GameApi.Find("UICamera");
                if (camera != null)
                {
                    Hook(harmony, camera, "PushInterface", "SkipOwnArg", null);
                    Hook(harmony, camera, "PopInterface", "SkipOwnArg", null);
                    Hook(harmony, camera, "LockInput", "SkipInOwnCall", null);
                    Hook(harmony, camera, "UnlockInput", "SkipInOwnCall", null);
                }
                // The game's own mod toolkit takes over every map window and draws a full-screen map on the main screen.
                Type mapUi = GameApi.Find("DQ8MR.Mods.Toolkit.MapUi");
                if (mapUi != null)
                {
                    Hook(harmony, mapUi, "Attach", "SkipOwnArg", null);
                    Hook(harmony, mapUi, "Release", "SkipOwnArg", null);
                }
                else MelonLogger.Msg("the game's MapUi toolkit was not found");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("map hooks failed: " + e.Message);
            }
        }

        private static void Hook(HarmonyLib.Harmony harmony, Type target, string method, string prefix, string postfix)
        {
            HarmonyMethod pre = prefix == null ? null : new HarmonyMethod(typeof(BridgeMod).GetMethod(prefix, Statics));
            HarmonyMethod post = postfix == null ? null : new HarmonyMethod(typeof(BridgeMod).GetMethod(postfix, Statics));
            foreach (MethodInfo m in target.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != method) continue;
                // Hooks that read the first argument cannot patch a method without one.
                if (prefix == "SkipOwnArg" && m.GetParameters().Length == 0) continue;
                // The map windows only use the parameterless FadeCheck, and the other overload does not compile as a patch target.
                if (prefix == "FadeDone" && m.GetParameters().Length != 0) continue;
                try
                {
                    harmony.Patch(m, pre, post);
                    MelonLogger.Msg("hooked " + target.Name + "." + method);
                    if (prefix == "SkipInOwnCall") AuditPatch(harmony, m, "Audit");
                    else if (prefix == "SkipOwnArg") AuditPatch(harmony, m, "AuditArg");
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("could not hook " + target.Name + "." + method + ": " + e.Message);
                }
            }
        }

        private static int auditLogged;

        private static void AuditPatch(HarmonyLib.Harmony harmony, MethodInfo m, string audit)
        {
            try
            {
                HarmonyMethod pre = new HarmonyMethod(typeof(BridgeMod).GetMethod(audit, Statics));
                pre.priority = HarmonyLib.Priority.First;
                harmony.Patch(m, pre, null);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("could not audit " + m.Name + ": " + e.Message);
            }
        }

        // Logs calls that reach the real game from the copy's own code while the copy exists.
        private static void Audit(MethodBase __originalMethod)
        {
            if (OwnDepth == 0) ReportLeak(__originalMethod);
        }

        private static void AuditArg(MethodBase __originalMethod, object __0)
        {
            if (OwnDepth == 0 && !Map.Owns(__0)) ReportLeak(__originalMethod);
        }

        private static void ReportLeak(MethodBase method)
        {
            try
            {
                if (!Map.Active || auditLogged >= 40) return;
                string stack = new System.Diagnostics.StackTrace(2, false).ToString();
                if (stack.IndexOf("GUIMap", StringComparison.Ordinal) < 0) return;
                auditLogged++;
                MelonLogger.Msg("reached the real game from the map copy: " + method.DeclaringType.Name + "." + method.Name + "\n" + stack);
            }
            catch (Exception)
            {
            }
        }

        // The copy must not hide the HUD or flag the real map as open.
        private static bool SkipOwn(object __instance)
        {
            return !Map.Owns(__instance);
        }

        // Greater than zero only while one of the copy's own methods runs, so its input locks and camera grabs are ignored.
        internal static int OwnDepth;

        private static bool SkipOwnArg(object __0)
        {
            return !Map.Owns(__0);
        }

        private static bool EnterOwn(object __instance)
        {
            if (Map.Owns(__instance)) OwnDepth++;
            return true;
        }

        private static void LeaveOwn(object __instance)
        {
            if (OwnDepth > 0 && Map.Owns(__instance)) OwnDepth--;
        }

        private static bool SkipInOwnCall()
        {
            return OwnDepth == 0;
        }

        private static int ownFadeInFrame = -10;

        // After FadeOut the copy waits for FadeCheck to report a finished fade; after FadeIn it first waits for one
        // unfinished frame and then a finished one, so a skipped FadeIn has to look like a fade that ran for a frame.
        private static bool OwnFadeIn()
        {
            if (OwnDepth == 0) return true;
            ownFadeInFrame = Time.frameCount;
            return false;
        }

        private static bool FadeDone(ref bool __result)
        {
            if (OwnDepth == 0) return true;
            __result = Time.frameCount - ownFadeInFrame > 1;
            return false;
        }

        // The real map and the copy share static state, so the copy steps aside while the real one is up.
        private static void AfterMapStart(object __instance)
        {
            if (Map.Owns(__instance)) return;
            RealMapOpen = true;
            Map.Destroy("in-game map opened");
            MelonLogger.Msg("in-game map opened");
        }

        private static void AfterMapEnd(object __instance)
        {
            if (Map.Owns(__instance)) return;
            RealMapOpen = false;
            ResumeAt = Time.realtimeSinceStartup + 1.5f;
            MelonLogger.Msg("in-game map closed");
        }

        private static void AfterWorldInit(object __instance)
        {
            if (DesiredView < 0 || !Map.Owns(__instance)) return;
            try
            {
                GameApi.SetField(__instance, __instance.GetType(), "isLocalMap", DesiredView == 1);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("could not pick the map view: " + e.Message);
            }
        }
    }

    // Milliseconds the mod itself spends per piece of work, logged every 15 seconds while a copy exists.
    internal sealed class Stat
    {
        private int count;
        private double total;
        private double max;

        // Everything the mod measured since the last frame started, to tell a game stall from a mod stall.
        public static double FrameMs;

        public void Add(long startTicks)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            FrameMs += ms;
            count++;
            total += ms;
            if (ms > max) max = ms;
        }

        // For worker threads: not part of the main thread's frame time.
        public void AddQuiet(long startTicks)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lock (this)
            {
                count++;
                total += ms;
                if (ms > max) max = ms;
            }
        }

        public string Take()
        {
            lock (this) return TakeLocked();
        }

        private string TakeLocked()
        {
            string text = count + "x avg " + (count > 0 ? total / count : 0).ToString("0.0") + " max " + max.ToString("0.0");
            count = 0;
            total = 0;
            max = 0;
            return text;
        }
    }

    public class BridgePump : MonoBehaviour
    {
        private static readonly Stat PollStat = new Stat();
        private static readonly Stat TickStat = new Stat();
        private static readonly Stat ReadStat = new Stat();
        private static readonly Stat EncodeStat = new Stat();
        private static readonly JpegEncoder Encoder = new JpegEncoder();
        private static int encoding;
        private static int dropped;
        private static int pinkSkipped;
        private static int pinkLogged;
        private static string lastMean = "-";

        private const float SettleSeconds = 2.5f;
        private static float freeSince = -1f;
        private static int lastMapNo = int.MinValue;
        private static bool lastFree;
        private static bool lastBattle;
        private static bool playerSeen;
        private static int hitchesLogged;
        private static float hudWatchUntil;
        private static string hudWatchLast = "";
        private static int hudWatchLogged;
        private static bool hudWatchBattle;
        private float battleCheckAt;
        private float hudWatchAt;
        private float seenCheckAt;
        private float nextPoll;
        private float nextFrame;
        private float perfAt;
        private string sentTitle;
        private bool wasConnected;

        private void Start()
        {
            StartCoroutine(CaptureLoop());
        }

        private void Update()
        {
            BridgeMod.OwnDepth = 0;
            StallProbe.Beat();
            float frame = Time.unscaledDeltaTime;
            string phases = FrameTrace.Frame(frame, playerSeen);
            // The trace also runs without the bridge, so the field is detected here and not only in Poll.
            if (!playerSeen && Time.realtimeSinceStartup >= seenCheckAt)
            {
                seenCheckAt = Time.realtimeSinceStartup + 1f;
                if (GameApi.HasPlayer())
                {
                    playerSeen = true;
                    StallProbe.Enabled = true;
                }
            }
            if (playerSeen && frame > 0.75f && hitchesLogged < 60)
            {
                hitchesLogged++;
                MelonLogger.Msg(string.Format("frame hitch: {0:0.0}s, the mod used {1:0}ms of it; copy {2}; battle {3}; player free {4}; {5}",
                    frame, Stat.FrameMs, BridgeMod.Map.Active ? "age " + BridgeMod.Map.Age.ToString("0.0") + "s" : "none", lastBattle, lastFree, phases));
            }
            Stat.FrameMs = 0;
            WatchHud();
            BridgeClient client = BridgeMod.Client;
            if (client == null) return;
            string command;
            while (client.TryTakeCommand(out command))
            {
                MelonLogger.Msg("command: " + command);
                if (command == "view:area" || command == "view:world")
                {
                    BridgeMod.DesiredView = command == "view:area" ? 1 : 0;
                    BridgeMod.Armed = true;
                    BridgeMod.QuickDeaths = 0;
                    BridgeMod.RetryAt = 0f;
                    nextPoll = 0f;
                }
            }
            if (Time.realtimeSinceStartup >= nextPoll)
            {
                nextPoll = Time.realtimeSinceStartup + 1f;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { Poll(client); }
                catch (Exception e)
                {
                    BridgeMod.Status = "error: " + e.Message;
                    MelonLogger.Warning(e.ToString());
                    nextPoll = Time.realtimeSinceStartup + 5f;
                }
                PollStat.Add(started);
            }
            if (Time.realtimeSinceStartup >= perfAt)
            {
                perfAt = Time.realtimeSinceStartup + 15f;
                if (BridgeMod.Map.Active) MelonLogger.Msg("perf ms: poll " + PollStat.Take() + "; tick " + TickStat.Take() + "; capture " + ReadStat.Take() + "; encode (worker) " + EncodeStat.Take() + "; dropped " + dropped + "; pink skipped " + pinkSkipped);
            }

            bool connected = client.Connected;
            if (connected && !wasConnected && !BridgeMod.Map.Active) client.SetFrame(Placeholder());
            wasConnected = connected;

            string title = "DQ8MR | " + BridgeMod.Status;
            if (title != sentTitle)
            {
                sentTitle = title;
                client.SetTitle(title);
            }
        }

        // For 15 s after a fight ends, a map change or a new copy, logs every change of the game's HUD-hiding inputs.
        private void WatchHud()
        {
            float now = Time.realtimeSinceStartup;
            if (playerSeen && now >= battleCheckAt)
            {
                battleCheckAt = now + 0.25f;
                bool inBattle = GameApi.InBattle();
                if (inBattle != hudWatchBattle)
                {
                    hudWatchBattle = inBattle;
                    StartHudWatch(inBattle ? "battle start" : "battle end");
                }
            }
            if (now > hudWatchUntil || now < hudWatchAt || hudWatchLogged >= 200) return;
            hudWatchAt = now + 0.1f;
            string hidden = "?";
            Type hud = GameApi.Find("DQ8MR.Mods.Toolkit.FieldHud");
            try
            {
                System.Collections.ICollection list = GameApi.GetField(null, hud, "Hidden") as System.Collections.ICollection;
                if (list != null) hidden = list.Count.ToString();
            }
            catch (Exception) { }
            string state = "hidden=" + hidden + " " + GameApi.HudInputs();
            if (state == hudWatchLast) return;
            hudWatchLast = state;
            hudWatchLogged++;
            MelonLogger.Msg("hud watch: " + state);
        }

        private static void StartHudWatch(string why)
        {
            hudWatchUntil = Time.realtimeSinceStartup + 15f;
            hudWatchLast = "";
            MelonLogger.Msg("hud watch start: " + why);
        }

        private static bool RealMapExists()
        {
            Type guiMap = GameApi.Find("GUIMap");
            if (guiMap == null) return false;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(guiMap))
            {
                if (!BridgeMod.Map.Owns(o)) return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            FrameTrace.Late();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            BridgeMod.Map.Tick();
            TickStat.Add(started);
        }

        private static void Poll(BridgeClient client)
        {
            HeadlessMap map = BridgeMod.Map;
            if (!client.Connected) return;
            if (BridgeMod.NoCopy)
            {
                map.Destroy("no-copy flag");
                BridgeMod.Status = "trace only";
                return;
            }
            if (BridgeMod.RealMapOpen)
            {
                if (RealMapExists()) { BridgeMod.Status = "in-game map is open"; return; }
                BridgeMod.RealMapOpen = false;
                BridgeMod.ResumeAt = Time.realtimeSinceStartup + 1.5f;
                MelonLogger.Msg("no in-game map window found any more, continuing");
                return;
            }
            if (Time.realtimeSinceStartup < BridgeMod.ResumeAt) return;
            if (!BridgeMod.Armed)
            {
                map.Destroy("not armed");
                if (BridgeMod.QuickDeaths < 4) BridgeMod.Status = "tap Area or World to start";
                return;
            }
            if (Time.realtimeSinceStartup < BridgeMod.RetryAt) return;
            if (map.Active && !map.Alive)
            {
                float life = map.Age;
                map.Destroy();
                BridgeMod.QuickDeaths = life < 6f ? BridgeMod.QuickDeaths + 1 : 0;
                MelonLogger.Msg(string.Format("the game closed the map copy after {0:0.0}s (quick closes: {1})", life, BridgeMod.QuickDeaths));
                if (BridgeMod.QuickDeaths >= 4)
                {
                    BridgeMod.Armed = false;
                    BridgeMod.Status = "map copy keeps closing, tap Area or World to retry";
                    return;
                }
                BridgeMod.RetryAt = Time.realtimeSinceStartup + 3f * BridgeMod.QuickDeaths + 1f;
                return;
            }
            // The game tears down and rebuilds its field UI around a fight, which leaves a copy built before it blank.
            bool battle = GameApi.InBattle();
            if (battle != lastBattle)
            {
                lastBattle = battle;
                MelonLogger.Msg("battle: " + battle + "; " + GameApi.MiniMapState());
            }
            if (battle)
            {
                map.Destroy("battle");
                freeSince = -1f;
                BridgeMod.Status = "battle";
                return;
            }
            if (!GameApi.HasPlayer())
            {
                map.Destroy("no player");
                freeSince = -1f;
                lastMapNo = int.MinValue;
                BridgeMod.Status = "waiting for the field";
                return;
            }
            playerSeen = true;
            StallProbe.Enabled = true;
            // Building the copy while the game loads a save, changes maps or shows a message window leaves the real screen black.
            string detail;
            bool free = GameApi.PlayerFree(out detail);
            float now = Time.realtimeSinceStartup;
            if (free != lastFree)
            {
                lastFree = free;
                MelonLogger.Msg("player free: " + free + " (" + detail + "); last map picture rgb " + lastMean);
            }
            if (!free) freeSince = -1f;
            else if (freeSince < 0f) freeSince = now;
            int mapNo = GameApi.MapNo();
            if (mapNo != lastMapNo)
            {
                lastMapNo = mapNo;
                StartHudWatch("map " + mapNo);
                freeSince = free ? now : -1f;
                map.Destroy("map changed");
            }
            int mode = GameApi.AutoMode();
            if (mode == 0)
            {
                map.Destroy("no map for this area");
                BridgeMod.Status = "no map for this area";
                return;
            }
            // The game's item-menu world map command sets the mode directly, so it works from inside a town too.
            if (mode == 2 && BridgeMod.DesiredView == 0) mode = 1;
            string key = GameApi.MapNo() + "/" + mode + "/" + BridgeMod.DesiredView;
            if (map.Alive && map.Key == key) return;
            if (freeSince < 0f || now - freeSince < SettleSeconds)
            {
                BridgeMod.Status = "waiting for the game to settle";
                return;
            }
            string error = map.Create(mode, key);
            if (error != null)
            {
                BridgeMod.Status = "map: " + error;
                MelonLogger.Msg(BridgeMod.Status);
                return;
            }
            BridgeMod.Status = mode == 2 ? "town or dungeon map" : (BridgeMod.DesiredView == 1 ? "area map" : (BridgeMod.DesiredView == 0 ? "world map" : "field map"));
            MelonLogger.Msg("showing: " + BridgeMod.Status + "; " + GameApi.MiniMapState());
        }

        private IEnumerator CaptureLoop()
        {
            WaitForEndOfFrame end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                FrameTrace.EndOfFrame();
                if (Time.realtimeSinceStartup < nextFrame) continue;
                nextFrame = Time.realtimeSinceStartup + 0.25f;
                BridgeClient client = BridgeMod.Client;
                // A new copy draws black until its windows exist, so the previous picture stays on the second screen meanwhile.
                if (client == null || !client.Connected || !BridgeMod.Map.Active || !BridgeMod.Map.Ready) continue;
                try
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    int w, h;
                    byte[] raw = BridgeMod.Map.ReadRaw(out w, out h);
                    ReadStat.Add(started);
                    if (raw != null) EncodeAsync(raw, w, h, client);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("map capture failed: " + e.Message);
                    nextFrame = Time.realtimeSinceStartup + 5f;
                }
            }
        }

        // The previous picture stays on the second screen while the worker is still busy with an older one.
        private static void EncodeAsync(byte[] raw, int w, int h, BridgeClient client)
        {
            if (System.Threading.Interlocked.CompareExchange(ref encoding, 1, 0) != 0)
            {
                dropped++;
                return;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    // The game unloads the map's background as a fight starts, which draws pink; keep the last good picture instead.
                    bool pink;
                    string mean = Sample(raw, w, h, out pink);
                    lastMean = mean;
                    if (pink)
                    {
                        pinkSkipped++;
                        if (pinkLogged < 12)
                        {
                            pinkLogged++;
                            MelonLogger.Msg("skipped a pink map picture (mean rgb " + mean + ")");
                        }
                        return;
                    }
                    byte[] jpg = Encoder.Encode(raw, w, h);
                    EncodeStat.AddQuiet(started);
                    client.SetFrame(jpg);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("map encode failed: " + e.Message);
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref encoding, 0);
                }
            });
        }

        // Samples a grid of pixels of Unity's bottom-up RGB24 data: mean colour, and whether a large share is magenta.
        private static string Sample(byte[] raw, int w, int h, out bool pink)
        {
            long r = 0, g = 0, b = 0;
            int n = 0, magenta = 0;
            for (int y = 4; y < h; y += 30)
            {
                for (int x = 4; x < w; x += 30)
                {
                    int p = (y * w + x) * 3;
                    int pr = raw[p], pg = raw[p + 1], pb = raw[p + 2];
                    r += pr;
                    g += pg;
                    b += pb;
                    n++;
                    if (pr > 180 && pb > 150 && pr - pg > 90 && pb - pg > 60) magenta++;
                }
            }
            pink = n > 0 && magenta * 100 / n >= 20;
            return n > 0 ? (r / n) + "," + (g / n) + "," + (b / n) + " magenta " + (magenta * 100 / n) + "%" : "-";
        }

        private static byte[] Placeholder()
        {
            Texture2D tex = new Texture2D(64, 36, TextureFormat.RGB24, false);
            Color[] fill = new Color[64 * 36];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color(0.08f, 0.09f, 0.11f);
            tex.SetPixels(fill);
            tex.Apply();
            byte[] jpg = ImageConversion.EncodeToJPG(tex, 90);
            Destroy(tex);
            return jpg;
        }
    }
}
