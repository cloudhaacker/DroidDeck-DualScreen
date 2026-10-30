using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DualScreenBridge.BridgeMod), "DualScreenBridge", "0.1.0", "DroidDeck Dual Screen")]

namespace DualScreenBridge
{
    public class BridgeMod : MelonMod
    {
        internal static BridgeClient Client;
        internal static volatile bool MapOpen;

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
            if (Client != null) Client.Stop();
        }

        private static void PatchMap()
        {
            try
            {
                Type map = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "Assembly-CSharp") { map = asm.GetType("GUIMap"); break; }
                }
                if (map == null) { MelonLogger.Warning("GUIMap not found, map events will not be logged"); return; }
                HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("droiddeck.dualscreen.bridge");
                Hook(harmony, map, "StartMap", "OnMapStarted");
                Hook(harmony, map, "EndMap", "OnMapEnded");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("map hooks failed: " + e.Message);
            }
        }

        private static void Hook(HarmonyLib.Harmony harmony, Type target, string method, string handler)
        {
            MethodInfo post = typeof(BridgeMod).GetMethod(handler, BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MethodInfo m in target.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != method) continue;
                harmony.Patch(m, null, new HarmonyMethod(post));
                MelonLogger.Msg("hooked GUIMap." + method);
            }
        }

        private static void OnMapStarted()
        {
            MapOpen = true;
            MelonLogger.Msg("map opened");
        }

        private static void OnMapEnded()
        {
            MapOpen = false;
            MelonLogger.Msg("map closed");
        }
    }

    public class BridgePump : MonoBehaviour
    {
        private const int Width = 480;
        private const int Height = 270;
        private float nextCapture;
        private string lastCommand;
        private string sentTitle;

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
                lastCommand = command;
            }
            string title = "DQ8MR mirror | map " + (BridgeMod.MapOpen ? "open" : "closed");
            if (lastCommand != null) title += " | cmd: " + lastCommand;
            if (title != sentTitle)
            {
                sentTitle = title;
                client.SetTitle(title);
            }
        }

        private IEnumerator CaptureLoop()
        {
            WaitForEndOfFrame end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                if (Time.realtimeSinceStartup < nextCapture) continue;
                nextCapture = Time.realtimeSinceStartup + 0.5f;
                BridgeClient client = BridgeMod.Client;
                if (client == null || !client.Connected) continue;
                try
                {
                    byte[] jpg = Grab();
                    if (jpg != null) client.SetFrame(jpg);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("capture failed: " + e.Message);
                    nextCapture = Time.realtimeSinceStartup + 5f;
                }
            }
        }

        private byte[] Grab()
        {
            Texture2D source = ScreenCapture.CaptureScreenshotAsTexture();
            if (source == null) return null;
            RenderTexture target = RenderTexture.GetTemporary(Width, Height, 0);
            RenderTexture previous = RenderTexture.active;
            Texture2D small = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                small.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                small.Apply();
                return ImageConversion.EncodeToJPG(small, 70);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(source);
                Destroy(small);
            }
        }
    }

    internal sealed class BridgeClient
    {
        public const string Host = "127.0.0.1";
        public const int Port = 47200;
        private const int TypeImage = 1;
        private const int TypeTitle = 2;
        private const int TypeCommand = 3;

        private readonly object gate = new object();
        private readonly Queue<string> commands = new Queue<string>();
        private byte[] frame;
        private string title;
        private string lastTitle;
        private volatile bool running;
        private volatile bool connected;
        private volatile bool readerDone;

        public bool Connected { get { return connected; } }

        public void Start()
        {
            running = true;
            Thread worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "dualscreen-bridge";
            worker.Start();
        }

        public void Stop()
        {
            running = false;
            lock (gate) Monitor.PulseAll(gate);
        }

        public void SetFrame(byte[] jpg)
        {
            lock (gate) { frame = jpg; Monitor.PulseAll(gate); }
        }

        public void SetTitle(string text)
        {
            lock (gate) { title = text; lastTitle = text; Monitor.PulseAll(gate); }
        }

        public bool TryTakeCommand(out string command)
        {
            lock (gate)
            {
                if (commands.Count > 0) { command = commands.Dequeue(); return true; }
            }
            command = null;
            return false;
        }

        private void Run()
        {
            while (running)
            {
                try { Session(); }
                catch (Exception e) { MelonLogger.Msg("bridge not connected: " + e.Message); }
                connected = false;
                for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
            }
        }

        private void Session()
        {
            TcpClient tcp = new TcpClient();
            try
            {
                tcp.Connect(Host, Port);
                tcp.NoDelay = true;
                NetworkStream stream = tcp.GetStream();
                readerDone = false;
                connected = true;
                MelonLogger.Msg("connected to the bridge");
                Thread reader = new Thread(delegate () { ReadLoop(stream); });
                reader.IsBackground = true;
                reader.Start();
                lock (gate) { if (title == null) title = lastTitle; }
                while (running && !readerDone)
                {
                    byte[] jpg;
                    string text;
                    lock (gate)
                    {
                        if (frame == null && title == null) Monitor.Wait(gate, 250);
                        jpg = frame; frame = null;
                        text = title; title = null;
                    }
                    if (text != null) Send(stream, TypeTitle, Encoding.UTF8.GetBytes(text));
                    if (jpg != null) Send(stream, TypeImage, jpg);
                }
            }
            finally
            {
                tcp.Close();
            }
        }

        private static void Send(NetworkStream stream, int type, byte[] payload)
        {
            int length = payload.Length + 1;
            byte[] message = new byte[5 + payload.Length];
            message[0] = (byte)(length >> 24);
            message[1] = (byte)(length >> 16);
            message[2] = (byte)(length >> 8);
            message[3] = (byte)length;
            message[4] = (byte)type;
            Buffer.BlockCopy(payload, 0, message, 5, payload.Length);
            stream.Write(message, 0, message.Length);
        }

        private void ReadLoop(NetworkStream stream)
        {
            try
            {
                byte[] head = new byte[5];
                while (running)
                {
                    ReadExact(stream, head, 5);
                    int length = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
                    if (length < 1 || length > (1 << 20)) break;
                    byte[] payload = new byte[length - 1];
                    ReadExact(stream, payload, payload.Length);
                    if (head[4] == TypeCommand)
                    {
                        lock (gate) commands.Enqueue(Encoding.UTF8.GetString(payload));
                    }
                }
            }
            catch (Exception)
            {
            }
            readerDone = true;
            lock (gate) Monitor.PulseAll(gate);
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new System.IO.EndOfStreamException();
                offset += read;
            }
        }
    }
}
