using System;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace DualScreenBridge
{
    // Renders the game's own brick background (the "screenBg" camera) once and hands it to the second screen; nothing from the game is stored in the mod.
    internal static class Backdrop
    {
        private const int Width = 720;
        private const int Height = 1080;
        private const string AtlasName = "screen_bg_Atlas";
        private static bool sent;
        private static int tries;
        private static float nextAt = 5f;

        public static void Tick(BridgeClient client)
        {
            if (sent || tries >= 20 || client == null || !client.Connected) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextAt) return;
            nextAt = now + 3f;
            tries++;
            try
            {
                byte[] jpg = Grab(tries >= 10);
                if (jpg == null) return;
                sent = true;
                client.SetBackdrop(jpg);
            }
            catch (Exception e)
            {
                MelonLogger.Msg("backdrop failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // The camera is preferred; the plain texture is only used when the camera never shows up or renders black.
        private static byte[] Grab(bool allowTexture)
        {
            Camera cam = null;
            foreach (Camera c in Camera.allCameras)
            {
                if (c.name == "screenBg") { cam = c; break; }
            }
            Texture2D picture = null;
            string how = "";
            if (cam != null)
            {
                picture = FromCamera(cam);
                how = "camera";
                if (Brightness(picture) < 6f)
                {
                    UnityEngine.Object.Destroy(picture);
                    picture = null;
                }
            }
            Texture atlas = FindTexture(AtlasName);
            Texture2D atlasCopy = atlas != null ? Copy(atlas) : null;
            if (picture == null)
            {
                if (!allowTexture || atlasCopy == null) { if (atlasCopy != null) UnityEngine.Object.Destroy(atlasCopy); return null; }
                picture = atlasCopy;
                atlasCopy = null;
                how = "texture";
            }
            MelonLogger.Msg("backdrop grabbed from the " + how + " (" + picture.width + "x" + picture.height + ", brightness " + Brightness(picture).ToString("0") + ")");
            Save("DualScreenBridge-backdrop-" + how + ".png", picture);
            if (atlasCopy != null)
            {
                Save("DualScreenBridge-backdrop-texture.png", atlasCopy);
                UnityEngine.Object.Destroy(atlasCopy);
            }
            byte[] jpg = ImageConversion.EncodeToJPG(picture, 85);
            UnityEngine.Object.Destroy(picture);
            return jpg;
        }

        private static Texture2D FromCamera(Camera cam)
        {
            RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 16, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            RenderTexture oldTarget = cam.targetTexture;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                cam.targetTexture = rt;
                cam.Render();
                Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                return tex;
            }
            finally
            {
                cam.targetTexture = oldTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture2D Copy(Texture source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                return tex;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture FindTexture(string name)
        {
            foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static float Brightness(Texture2D tex)
        {
            if (tex == null) return 0f;
            float sum = 0f;
            int n = 0;
            for (int y = 8; y < tex.height; y += 40)
            {
                for (int x = 8; x < tex.width; x += 40)
                {
                    Color c = tex.GetPixel(x, y);
                    sum += (c.r + c.g + c.b) / 3f * 255f;
                    n++;
                }
            }
            return n > 0 ? sum / n : 0f;
        }

        // Samples for John to send back when the picture looks wrong; they stay on his machine.
        private static void Save(string file, Texture2D tex)
        {
            try
            {
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UserData");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, file), ImageConversion.EncodeToPNG(tex));
            }
            catch (Exception e)
            {
                MelonLogger.Msg("could not save " + file + ": " + e.Message);
            }
        }
    }
}
