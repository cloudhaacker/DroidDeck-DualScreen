using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace DualScreenBridge
{
    // Splits every frame into phases and logs a summary every few seconds together with the map and camera position,
    // so slow places and slow phases can be found. It never touches the game.
    internal static class FrameTrace
    {
        private sealed class CamStat
        {
            public string Name;
            public long CullStart;
            public long RenderStart;
            public double CullMs;
            public double RenderMs;
        }

        private const float WindowSeconds = 3f;
        private static readonly Dictionary<int, CamStat> cams = new Dictionary<int, CamStat>();
        private static long tUpdate;
        private static long tLate;
        private static long tCull;
        private static long tPost;
        private static long tEnd;
        private static bool culled;
        private static int lastG0;
        private static int lastG1;
        private static int lastG2;
        private static int windowG0;
        private static int windowG1;
        private static int windowG2;
        private static int frames;
        private static double sumFrame;
        private static double maxFrame;
        private static double sumUpdate;
        private static double sumLate;
        private static double sumRender;
        private static double sumTail;
        private static double sumPresent;
        private static float windowStart;
        private static float detailAt;

        public static void Start()
        {
            Camera.onPreCull += OnPreCull;
            Camera.onPreRender += OnPreRender;
            Camera.onPostRender += OnPostRender;
            ProcInfo.FindSensors();
            LogSystem();
        }

        private static double Ms(long from, long to)
        {
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }

        // Marks the start of the frame's late-update phase.
        public static void Late()
        {
            tLate = Stopwatch.GetTimestamp();
        }

        public static void EndOfFrame()
        {
            tEnd = Stopwatch.GetTimestamp();
        }

        // Called first thing every frame. Returns the previous frame's phases when it was longer than a second.
        public static string Frame(float frameSeconds, bool report)
        {
            long now = Stopwatch.GetTimestamp();
            string note = null;
            if (tUpdate != 0)
            {
                long b = tLate > tUpdate ? tLate : tUpdate;
                long c = culled && tCull > b ? tCull : b;
                long d = tPost > c ? tPost : c;
                long e = tEnd > d ? tEnd : d;
                double update = Ms(tUpdate, b);
                double late = Ms(b, c);
                double render = Ms(c, d);
                double tail = Ms(d, e);
                double present = Ms(e, now);
                double total = update + late + render + tail + present;
                int g0 = GC.CollectionCount(0);
                int g1 = GC.CollectionCount(1);
                int g2 = GC.CollectionCount(2);
                if (report && frameSeconds > 0.75f)
                {
                    note = string.Format("phases ms: update {0:0} late-update {1:0} render {2:0} after-render {3:0} present {4:0}; gc +{5}/{6}/{7}",
                        update, late, render, tail, present, g0 - lastG0, g1 - lastG1, g2 - lastG2);
                }
                lastG0 = g0;
                lastG1 = g1;
                lastG2 = g2;
                frames++;
                sumFrame += total;
                if (total > maxFrame) maxFrame = total;
                sumUpdate += update;
                sumLate += late;
                sumRender += render;
                sumTail += tail;
                sumPresent += present;
            }
            tUpdate = now;
            tLate = 0;
            tCull = 0;
            tPost = 0;
            tEnd = 0;
            culled = false;
            float time = Time.realtimeSinceStartup;
            if (windowStart <= 0f)
            {
                windowStart = time;
                windowG0 = GC.CollectionCount(0);
                windowG1 = GC.CollectionCount(1);
                windowG2 = GC.CollectionCount(2);
            }
            else if (time - windowStart >= WindowSeconds)
            {
                Window(time, report);
            }
            return note;
        }

        private static void OnPreCull(Camera cam)
        {
            long now = Stopwatch.GetTimestamp();
            if (!culled)
            {
                culled = true;
                tCull = now;
            }
            Stat(cam).CullStart = now;
        }

        private static void OnPreRender(Camera cam)
        {
            long now = Stopwatch.GetTimestamp();
            CamStat s = Stat(cam);
            if (s.CullStart != 0) s.CullMs += Ms(s.CullStart, now);
            s.RenderStart = now;
        }

        private static void OnPostRender(Camera cam)
        {
            long now = Stopwatch.GetTimestamp();
            CamStat s = Stat(cam);
            if (s.RenderStart != 0) s.RenderMs += Ms(s.RenderStart, now);
            s.RenderStart = 0;
            tPost = now;
        }

        private static CamStat Stat(Camera cam)
        {
            int id = cam.GetInstanceID();
            CamStat s;
            if (!cams.TryGetValue(id, out s))
            {
                s = new CamStat();
                s.Name = cam.name;
                cams[id] = s;
            }
            return s;
        }

        private static void Window(float time, bool report)
        {
            try
            {
                if (report && frames > 0)
                {
                    double n = frames;
                    double average = sumFrame / n;
                    StringBuilder sb = new StringBuilder();
                    sb.Append("trace: ").Append(Where());
                    sb.Append(string.Format(" | fps {0:0.0} frames {1} avg {2:0.0}ms max {3:0}ms | update {4:0.0} late-update {5:0.0} render {6:0.0} after-render {7:0.0} present {8:0.0}",
                        1000.0 / average, frames, average, maxFrame, sumUpdate / n, sumLate / n, sumRender / n, sumTail / n, sumPresent / n));
                    sb.Append(string.Format(" | gc +{0}/{1}/{2} heap {3}MB", GC.CollectionCount(0) - windowG0, GC.CollectionCount(1) - windowG1, GC.CollectionCount(2) - windowG2, GC.GetTotalMemory(false) / (1024 * 1024)));
                    sb.Append(" | ").Append(ProcInfo.Memory());
                    sb.Append(" | psi cpu/mem/io ").Append(ProcInfo.Pressure("cpu")).Append('/').Append(ProcInfo.Pressure("memory")).Append('/').Append(ProcInfo.Pressure("io"));
                    string sensors = ProcInfo.SensorLine();
                    if (sensors.Length > 0) sb.Append(" | ").Append(sensors);
                    sb.Append(" | cameras ").Append(TopCameras(n));
                    MelonLogger.Msg(sb.ToString());
                    if (average > 50.0 && time >= detailAt)
                    {
                        detailAt = time + 10f;
                        Detail();
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("trace window failed: " + e.Message);
            }
            foreach (CamStat s in cams.Values)
            {
                s.CullMs = 0;
                s.RenderMs = 0;
            }
            frames = 0;
            sumFrame = 0;
            maxFrame = 0;
            sumUpdate = 0;
            sumLate = 0;
            sumRender = 0;
            sumTail = 0;
            sumPresent = 0;
            windowStart = time;
            windowG0 = GC.CollectionCount(0);
            windowG1 = GC.CollectionCount(1);
            windowG2 = GC.CollectionCount(2);
        }

        private static string Where()
        {
            string map = "-";
            try
            {
                map = GameApi.MapNo().ToString();
            }
            catch (Exception)
            {
            }
            string pos = "-";
            foreach (Camera c in Camera.allCameras)
            {
                if (c.name != "a main" && c.name != "a battle") continue;
                Vector3 p = c.transform.position;
                pos = string.Format("({0:0},{1:0},{2:0})", p.x, p.y, p.z);
                break;
            }
            return "map " + map + " pos " + pos;
        }

        private static string TopCameras(double frameCount)
        {
            foreach (Camera c in Camera.allCameras)
            {
                CamStat known;
                if (cams.TryGetValue(c.GetInstanceID(), out known)) known.Name = c.name;
            }
            List<CamStat> list = new List<CamStat>(cams.Values);
            list.Sort(delegate (CamStat x, CamStat y) { return (y.CullMs + y.RenderMs).CompareTo(x.CullMs + x.RenderMs); });
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < 3; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i].Name).Append(' ').Append(((list[i].CullMs + list[i].RenderMs) / frameCount).ToString("0.0")).Append("ms");
            }
            return sb.ToString();
        }

        // What the scene holds while the game is slow: draw load, transparent surfaces, post effects, particles, lights.
        private static void Detail()
        {
            try
            {
                StringBuilder sb = new StringBuilder("trace detail: ");
                foreach (Camera c in Camera.allCameras)
                {
                    if (c.name != "a main" && c.name != "a battle") continue;
                    RenderTexture rt = c.targetTexture;
                    sb.Append(string.Format("{0} target {1} hdr {2} path {3} depth {4} effects [{5}]; ", c.name, rt != null ? rt.width + "x" + rt.height : "screen", c.allowHDR, c.actualRenderingPath, c.depthTextureMode, ImageEffects(c)));
                    break;
                }
                Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                int visible = 0;
                int transparent = 0;
                Dictionary<string, int> shaders = new Dictionary<string, int>();
                foreach (Renderer r in renderers)
                {
                    if (!r.isVisible) continue;
                    visible++;
                    Material m = r.sharedMaterial;
                    if (m == null) continue;
                    if (m.renderQueue >= 3000) transparent++;
                    string name = m.shader != null ? m.shader.name : "-";
                    int count;
                    shaders.TryGetValue(name, out count);
                    shaders[name] = count + 1;
                }
                sb.Append(string.Format("renderers {0} visible of {1}, {2} transparent; shaders {3}; ", visible, renderers.Length, transparent, TopShaders(shaders)));
                Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
                int lit = 0;
                int shadowed = 0;
                foreach (Light l in lights)
                {
                    if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
                    lit++;
                    if (l.shadows != LightShadows.None) shadowed++;
                }
                sb.Append(string.Format("lights {0} on, {1} with shadows; particles {2}; quality shadows {3} pixel lights {4} shadow distance {5:0}", lit, shadowed, Particles(), QualitySettings.shadows, QualitySettings.pixelLightCount, QualitySettings.shadowDistance));
                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("trace detail failed: " + e.Message);
            }
        }

        private static string TopShaders(Dictionary<string, int> shaders)
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(shaders);
            list.Sort(delegate (KeyValuePair<string, int> x, KeyValuePair<string, int> y) { return y.Value.CompareTo(x.Value); });
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < 6; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i].Key).Append(" x").Append(list[i].Value);
            }
            return sb.ToString();
        }

        private static string ImageEffects(Camera cam)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MonoBehaviour m in cam.GetComponents<MonoBehaviour>())
            {
                if (m == null) continue;
                Type t = m.GetType();
                if (t.GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t.Name).Append(m.enabled ? "" : " (off)");
            }
            return sb.ToString();
        }

        // The particle module is not referenced at build time, so it is read by name.
        private static string Particles()
        {
            Type type = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule");
            if (type == null) return "unavailable";
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(type);
            int playing = 0;
            int count = 0;
            foreach (UnityEngine.Object o in all)
            {
                if ((bool)GameApi.Invoke(o, type, "get_isPlaying")) playing++;
                count += Convert.ToInt32(GameApi.Invoke(o, type, "get_particleCount"));
            }
            return playing + " playing of " + all.Length + ", " + count + " particles";
        }

        private static void LogSystem()
        {
            try
            {
                MelonLogger.Msg(string.Format("system: gpu {0} ({1}, {2}) {3}MB; cpu {4} x{5} {6}MHz; ram {7}MB; screen {8}x{9}; vsync {10}; target fps {11}; quality {12}; gfx threads {13}",
                    SystemInfo.graphicsDeviceName, SystemInfo.graphicsDeviceType, SystemInfo.graphicsDeviceVersion, SystemInfo.graphicsMemorySize,
                    SystemInfo.processorType, SystemInfo.processorCount, SystemInfo.processorFrequency, SystemInfo.systemMemorySize,
                    Screen.width, Screen.height, QualitySettings.vSyncCount, Application.targetFrameRate,
                    QualitySettings.names[QualitySettings.GetQualityLevel()], SystemInfo.graphicsMultiThreaded));
                string sensors = ProcInfo.SensorLine();
                MelonLogger.Msg("sensors: " + (sensors.Length > 0 ? sensors : "none readable") + "; main thread " + ProcInfo.MainThreadId() + "; " + ProcInfo.Threads().Count + " threads");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("system info failed: " + e.Message);
            }
        }
    }
}
