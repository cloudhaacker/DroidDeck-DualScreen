using System;
using MelonLoader;
using UnityEngine;

namespace DualScreenBridge
{
    // A private copy of the game's map window, drawn by its own camera far from the playfield.
    internal sealed class HeadlessMap
    {
        private const int FirstLayer = 8;
        private const float FarAway = 20000f;
        private GameObject root;
        private GameObject cameraObject;
        private RenderTexture target;
        private Texture2D readback;
        private float diagnoseAt;
        private int layer;

        public bool Active { get { return root != null; } }

        public bool Owns(object component)
        {
            Component c = component as Component;
            return c != null && root != null && c.transform.IsChildOf(root.transform);
        }

        public string Create(bool local)
        {
            Destroy();
            BridgeMod.DesiredLocal = local;
            if (!GameApi.HasPlayer()) return "no player yet";
            Type menus = GameApi.Find("SubMenuPrefabs");
            Type resource = GameApi.Find("NGUIResource");
            Type guiMap = GameApi.Find("GUIMap");
            Type cameraControl = GameApi.Find("NGUICamera2DControl");
            if (menus == null || resource == null || guiMap == null || cameraControl == null) return "game classes not found";

            object menu = GameApi.Invoke(null, menus, "get_Instance");
            object res = GameApi.Invoke(null, resource, "get_Instance");
            if (menu == null || res == null) return "game not ready";
            GameObject anchor = GameApi.GetField(menu, menus, "guiMapRoot") as GameObject;
            string path = GameApi.GetField(menu, menus, "guiMapPrefabPath") as string;
            if (path == null) return "no map prefab path";
            path = (string)GameApi.Invoke(null, resource, "ReplacePath", path);
            if (!(bool)GameApi.Invoke(res, resource, "IsLoaded", path)) return "open the in-game map once first";
            GameObject prefab = GameApi.Invoke(res, resource, "GetPrefabResource", path) as GameObject;
            if (prefab == null) return "map prefab missing";
            Camera main = GameApi.Invoke(null, cameraControl, "GetCamera") as Camera;
            if (main == null) return "no UI camera";

            layer = FreeLayer();
            if (layer < 0) return "no free layer";

            float aspect = main.aspect > 0.1f ? main.aspect : 16f / 9f;
            int width = 960;
            int height = Mathf.Clamp(Mathf.RoundToInt(width / aspect), 240, 1080);
            target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);

            root = new GameObject("DualScreenMapRoot");
            root.layer = layer;
            root.transform.position = new Vector3(FarAway, FarAway, 0f);
            if (anchor != null) root.transform.localScale = anchor.transform.lossyScale;

            float depth = anchor != null ? main.transform.position.z - anchor.transform.position.z : main.transform.position.z;
            cameraObject = new GameObject("DualScreenMapCamera");
            cameraObject.layer = layer;
            cameraObject.transform.position = new Vector3(FarAway, FarAway, depth);
            Camera cam = cameraObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = main.orthographicSize;
            cam.aspect = aspect;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << layer;
            cam.depth = -100f;
            cam.targetTexture = target;

            object modeValue = GameApi.GetField(null, guiMap, "gMapMode");
            int mode = Convert.ToInt32(modeValue);
            if (mode == 0) mode = 1;
            GameApi.SetField(null, guiMap, "mapMode", Enum.ToObject(modeValue.GetType(), mode));

            GameObject instance = (GameObject)UnityEngine.Object.Instantiate(prefab);
            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            diagnoseAt = Time.realtimeSinceStartup + 2f;
            MelonLogger.Msg(string.Format("map copy created: mode={0} local={1} layer={2} target={3}x{4} scale={5}", mode, local, layer, width, height, root.transform.localScale));
            return null;
        }

        public void Tick()
        {
            if (diagnoseAt <= 0f || Time.realtimeSinceStartup < diagnoseAt) return;
            diagnoseAt = 0f;
            if (root == null) return;
            try
            {
                Type panel = GameApi.Find("UIPanel");
                Type world = GameApi.Find("GUIMapWorld");
                int panels = panel != null ? root.GetComponentsInChildren(panel, true).Length : -1;
                int transforms = root.GetComponentsInChildren<Transform>(true).Length;
                Component w = world != null ? root.GetComponentInChildren(world, true) : null;
                string state = w != null ? GameApi.GetField(w, world, "isLocalMap") + " / status " + GameApi.GetField(w, world, "currentStatus") : "no GUIMapWorld found";
                MelonLogger.Msg(string.Format("map copy check: transforms={0} panels={1} isLocalMap={2}", transforms, panels, state));
            }
            catch (Exception e)
            {
                MelonLogger.Warning("map copy check failed: " + e.Message);
            }
        }

        public byte[] Read()
        {
            if (target == null || !target.IsCreated()) return null;
            if (readback == null) readback = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                readback.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
            return ImageConversion.EncodeToJPG(readback, 80);
        }

        public void Destroy()
        {
            diagnoseAt = 0f;
            if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
            if (root != null) UnityEngine.Object.Destroy(root);
            cameraObject = null;
            root = null;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
                target = null;
            }
            if (readback != null)
            {
                UnityEngine.Object.Destroy(readback);
                readback = null;
            }
        }

        private static int FreeLayer()
        {
            for (int i = 31; i >= FirstLayer; i--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            }
            return -1;
        }
    }
}
