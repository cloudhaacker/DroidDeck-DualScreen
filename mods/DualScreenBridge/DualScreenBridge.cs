using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DualScreenBridge.BridgeMod), "DualScreenBridge", "0.6.0", "DroidDeck Dual Screen")]

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
        // The copy only starts after the first Area or World tap, and gives up if the game keeps closing it.
        internal static bool Armed;
        internal static int QuickDeaths;
        internal static float RetryAt;

        public override void OnLateInitializeMelon()
        {
            Client = new BridgeClient();
            Client.Start();
            PatchMap();
            GameObject host = new GameObject("DualScreenBridge");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<BridgePump>();
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
                try
                {
                    harmony.Patch(m, pre, post);
                    MelonLogger.Msg("hooked " + target.Name + "." + method);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("could not hook " + target.Name + "." + method + ": " + e.Message);
                }
            }
        }

        // The copy must not hide the HUD or flag the real map as open.
        private static bool SkipOwn(object __instance)
        {
            return !Map.Owns(__instance);
        }

        // True only while the copy's own command handler runs, so its input locks are ignored.
        internal static bool InOwnCall;

        private static bool SkipOwnArg(object __0)
        {
            return !Map.Owns(__0);
        }

        private static bool EnterOwn(object __instance)
        {
            InOwnCall = Map.Owns(__instance);
            return true;
        }

        private static void LeaveOwn()
        {
            InOwnCall = false;
        }

        private static bool SkipInOwnCall()
        {
            return !InOwnCall;
        }

        // The real map and the copy share static state, so the copy steps aside while the real one is up.
        private static void AfterMapStart(object __instance)
        {
            if (Map.Owns(__instance)) return;
            RealMapOpen = true;
            Map.Destroy();
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

    public class BridgePump : MonoBehaviour
    {
        private float nextPoll;
        private float nextFrame;
        private string sentTitle;
        private bool wasConnected;

        private void Start()
        {
            StartCoroutine(CaptureLoop());
        }

        private void Update()
        {
            BridgeMod.InOwnCall = false;
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
                try { Poll(client); }
                catch (Exception e)
                {
                    BridgeMod.Status = "error: " + e.Message;
                    MelonLogger.Warning(e.ToString());
                    nextPoll = Time.realtimeSinceStartup + 5f;
                }
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

        private void LateUpdate()
        {
            BridgeMod.Map.Tick();
        }

        private static void Poll(BridgeClient client)
        {
            HeadlessMap map = BridgeMod.Map;
            if (!client.Connected) return;
            if (BridgeMod.RealMapOpen) { BridgeMod.Status = "in-game map is open"; return; }
            if (Time.realtimeSinceStartup < BridgeMod.ResumeAt) return;
            if (!BridgeMod.Armed)
            {
                map.Destroy();
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
            if (!GameApi.HasPlayer())
            {
                map.Destroy();
                BridgeMod.Status = "waiting for the field";
                return;
            }
            int mode = GameApi.AutoMode();
            if (mode == 0)
            {
                map.Destroy();
                BridgeMod.Status = "no map for this area";
                return;
            }
            string key = GameApi.MapNo() + "/" + mode + "/" + BridgeMod.DesiredView;
            if (map.Alive && map.Key == key) return;
            string error = map.Create(mode, key);
            if (error != null)
            {
                BridgeMod.Status = "map: " + error;
                MelonLogger.Msg(BridgeMod.Status);
                return;
            }
            BridgeMod.Status = mode == 2 ? "town or dungeon map" : (BridgeMod.DesiredView == 1 ? "area map" : (BridgeMod.DesiredView == 0 ? "world map" : "field map"));
            MelonLogger.Msg("showing: " + BridgeMod.Status);
        }

        private IEnumerator CaptureLoop()
        {
            WaitForEndOfFrame end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                if (Time.realtimeSinceStartup < nextFrame) continue;
                nextFrame = Time.realtimeSinceStartup + 0.25f;
                BridgeClient client = BridgeMod.Client;
                if (client == null || !client.Connected || !BridgeMod.Map.Active) continue;
                try
                {
                    byte[] jpg = BridgeMod.Map.Read();
                    if (jpg != null) client.SetFrame(jpg);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("map capture failed: " + e.Message);
                    nextFrame = Time.realtimeSinceStartup + 5f;
                }
            }
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
