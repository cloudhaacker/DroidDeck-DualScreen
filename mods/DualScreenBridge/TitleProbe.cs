using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DualScreenBridge
{
    // Logs what the game shows before a save is loaded (boot, title, main menu), to find its background picture and a main-menu signal.
    internal static class TitleProbe
    {
        private const int MaxLogged = 12;
        private static float nextAt = 8f;
        private static int logged;
        private static bool done;
        private static string lastSignature = "";

        public static void Tick()
        {
            if (done || logged >= MaxLogged) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextAt) return;
            nextAt = now + 6f;
            try
            {
                if (GameApi.HasPlayer())
                {
                    done = true;
                    return;
                }
                Dump();
            }
            catch (Exception e)
            {
                MelonLogger.Msg("title probe failed: " + e.Message);
                nextAt = now + 30f;
            }
        }

        private static void Dump()
        {
            List<string> lines = new List<string>();
            StringBuilder scenes = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Append(i > 0 ? ", " : "").Append(SceneManager.GetSceneAt(i).name);
            lines.Add("scene active=" + SceneManager.GetActiveScene().name + " loaded=[" + scenes + "]");

            StringBuilder roots = new StringBuilder();
            int rootCount = 0;
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!g.activeInHierarchy) continue;
                if (rootCount++ < 40) roots.Append(rootCount > 1 ? ", " : "").Append(g.name);
            }
            lines.Add("active roots " + rootCount + ": " + roots);

            foreach (Camera c in Camera.allCameras)
            {
                lines.Add(string.Format("camera {0} depth={1} mask=0x{2:x} clear={3} rect=({4:0.00},{5:0.00},{6:0.00},{7:0.00})", c.name, c.depth, c.cullingMask, c.clearFlags, c.rect.x, c.rect.y, c.rect.width, c.rect.height));
            }

            Type widget = GameApi.Find("UIWidget");
            if (widget != null)
            {
                int big = 0, labels = 0;
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(widget))
                {
                    Component c = o as Component;
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    string kind = c.GetType().Name;
                    try
                    {
                        Vector2 rel = (Vector2)GameApi.Invoke(c, null, "get_relativeSize");
                        float w = Mathf.Abs(c.transform.lossyScale.x * rel.x);
                        float h = Mathf.Abs(c.transform.lossyScale.y * rel.y);
                        string detail = "";
                        if (kind == "UILabel")
                        {
                            if (labels++ >= 30) continue;
                            detail = "text=" + GameApi.GetField(c, null, "mText");
                        }
                        else
                        {
                            if (w < 200f && h < 200f && kind != "UITexture") continue;
                            if (big++ >= 60) continue;
                            if (kind.EndsWith("Sprite")) detail = "sprite=" + GameApi.GetField(c, null, "mSpriteName") + " ";
                            Texture tex = GameApi.Invoke(c, null, "get_mainTexture") as Texture;
                            if (tex != null) detail += "texture=" + tex.name + " " + tex.width + "x" + tex.height;
                        }
                        if (detail.Length > 90) detail = detail.Substring(0, 90);
                        lines.Add(string.Format("widget {0}/{1} [{2}] size {3:0}x{4:0} depth={5} layer={6} {7}", c.transform.parent != null ? c.transform.parent.name : "", c.name, kind, w, h,
                            GameApi.Invoke(c, null, "get_depth"), c.gameObject.layer, detail));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else lines.Add("UIWidget not found");

            int renderers = 0, shown = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.gameObject.activeInHierarchy || !r.enabled) continue;
                renderers++;
                Material m = r.sharedMaterial;
                Texture tex = m != null ? m.mainTexture : null;
                if (tex == null || tex.width < 256 || shown >= 30) continue;
                shown++;
                lines.Add("renderer " + r.name + " layer=" + r.gameObject.layer + " texture=" + tex.name + " " + tex.width + "x" + tex.height + " bounds " + r.bounds.size);
            }
            lines.Add("active renderers " + renderers);

            string signature = string.Join("\n", lines.ToArray());
            if (signature == lastSignature) return;
            lastSignature = signature;
            logged++;
            MelonLogger.Msg("title probe " + logged + " (t=" + Time.realtimeSinceStartup.ToString("0") + "s):");
            foreach (string line in lines) MelonLogger.Msg("  " + line);
        }
    }
}
