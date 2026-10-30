using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DualScreenBridge.BridgeMod), "DualScreenBridge", "0.2.0", "DroidDeck Dual Screen")]

namespace DualScreenBridge
{
    public class BridgeMod : MelonMod
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        internal static BridgeClient Client;
        internal static readonly HeadlessMap Map = new HeadlessMap();
        internal static bool DesiredLocal;
        internal static string Status = "waiting for a command";

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
            foreach (MethodInfo m in target.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != method) continue;
                harmony.Patch(m, pre, post);
                MelonLogger.Msg("hooked " + target.Name + "." + method);
            }
        }

        // The copy must not hide the HUD or flag the real map as open.
        private static bool SkipOwn(object __instance)
        {
            return !Map.Owns(__instance);
        }

        private static void AfterMapStart(object __instance)
        {
            if (!Map.Owns(__instance)) MelonLogger.Msg("in-game map opened");
        }

        private static void AfterMapEnd(object __instance)
        {
            if (!Map.Owns(__instance)) MelonLogger.Msg("in-game map closed");
        }

        private static void AfterWorldInit(object __instance)
        {
            if (!Map.Owns(__instance)) return;
            try
            {
                Type t = __instance.GetType();
                GameApi.SetField(__instance, t, "isLocalMap", DesiredLocal);
                GameApi.Invoke(__instance, t, "ChangeSwitchButtonDisp");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("could not pick the map view: " + e.Message);
            }
        }
    }

    public class BridgePump : MonoBehaviour
    {
        private float nextFrame;
        private string sentTitle;
        private bool wasConnected;

        private void Start()
        {
            StartCoroutine(CaptureLoop());
        }

        private void Update()
        {
            BridgeClient client = BridgeMod.Client;
            if (client == null) return;
            string command;
            while (client.TryTakeCommand(out command))
            {
                MelonLogger.Msg("command: " + command);
                Handle(command);
            }
            BridgeMod.Map.Tick();

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

        private static void Handle(string command)
        {
            bool local;
            if (command == "view:area") local = true;
            else if (command == "view:world") local = false;
            else return;
            string error;
            try
            {
                error = BridgeMod.Map.Create(local);
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                MelonLogger.Warning(e.ToString());
            }
            BridgeMod.Status = error != null ? "map: " + error : "showing the " + (local ? "area" : "world") + " map";
            MelonLogger.Msg(BridgeMod.Status);
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
