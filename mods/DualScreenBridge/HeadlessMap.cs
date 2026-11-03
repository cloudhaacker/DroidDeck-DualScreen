using System;
using System.Collections.Generic;
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
        private GameObject instance;
        private RenderTexture target;
        private Texture2D readback;
        private float diagnoseAt;
        private float isolateAt;
        private int layer = -1;
        private bool loadRequested;
        private bool camerasLogged;
        private float createdAt;
        private int fixLogged;
        private float strayAt;
        private int strayScans;
        private readonly List<GameObject> strays = new List<GameObject>();
        private readonly HashSet<int> preexisting = new HashSet<int>();
        private string lastSignature;
        private float readyAt;
        private int cameraChanges;
        private int afterDumps;

        public string Key = "";

        public float Age { get { return Time.realtimeSinceStartup - createdAt; } }

        public bool Active { get { return root != null; } }

        // The map window builds its parts over the first second or so; frames before that are black.
        public bool Ready
        {
            get
            {
                if (root == null) return false;
                return (readyAt > 0f && Time.realtimeSinceStartup >= readyAt) || Age > 3f;
            }
        }

        public bool Alive { get { return root != null && instance != null; } }

        // The game's map window runs cleanup code after the copy is gone, so the copy's map window is remembered
        // by reference even once its root no longer exists.
        private Component ownMap;
        private Camera uiCamera;
        private int uiMask;
        private readonly List<Component> retired = new List<Component>();

        public bool Owns(object component)
        {
            if (component == null) return false;
            if (object.ReferenceEquals(component, ownMap)) return true;
            foreach (Component r in retired)
            {
                if (object.ReferenceEquals(r, component)) return true;
            }
            Component c = component as Component;
            return c != null && root != null && c.transform.IsChildOf(root.transform);
        }

        public string Create(int mode, string key)
        {
            Destroy();
            if (!GameApi.HasPlayer()) return "waiting for the field";
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
            if (!(bool)GameApi.Invoke(res, resource, "IsLoaded", path))
            {
                if (!loadRequested)
                {
                    loadRequested = true;
                    GameApi.Invoke(res, resource, "Load", path);
                    MelonLogger.Msg("asked the game to load the map assets");
                }
                if (!(bool)GameApi.Invoke(res, resource, "IsLoaded", path)) return "loading the map assets";
            }
            GameObject prefab = GameApi.Invoke(res, resource, "GetPrefabResource", path) as GameObject;
            if (prefab == null) return "map prefab missing";
            Camera main = GameApi.Invoke(null, cameraControl, "GetCamera") as Camera;
            if (main == null) return "no UI camera";
            // The map window edits this camera's layer mask; it is held at this value while the copy exists.
            uiCamera = main;
            uiMask = main.cullingMask;

            layer = FreeLayer();
            if (layer < 0) return "no free layer";

            float useW = Convert.ToSingle(GameApi.Invoke(null, cameraControl, "GetUseWidth"));
            float useH = Convert.ToSingle(GameApi.Invoke(null, cameraControl, "GetUseHeight"));
            if (useW < 16f || useH < 16f) { useW = main.pixelWidth; useH = main.pixelHeight; }
            float aspect = useW / useH;
            int width = 720;
            int height = Mathf.Clamp(Mathf.RoundToInt(width / aspect), 200, 1280);
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
            Isolate();
            if (!camerasLogged) LogCameras(main, useW, useH);

            object modeValue = GameApi.GetField(null, guiMap, "gMapMode");
            GameApi.SetField(null, guiMap, "mapMode", Enum.ToObject(modeValue.GetType(), mode));

            preexisting.Clear();
            Type captured = GameApi.Find("CapturedBgNGUI");
            if (captured != null)
            {
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(captured)) preexisting.Add(o.GetInstanceID());
            }

            BridgeMod.OwnDepth++;
            try
            {
                instance = (GameObject)UnityEngine.Object.Instantiate(prefab, root.transform, false);
            }
            finally
            {
                BridgeMod.OwnDepth--;
            }
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            ownMap = instance.GetComponentInChildren(guiMap, true);

            Key = key;
            createdAt = Time.realtimeSinceStartup;
            lastSignature = null;
            readyAt = 0f;
            cameraChanges = 0;
            fixLogged = 0;
            strayScans = 0;
            strayAt = Time.realtimeSinceStartup + 0.2f;
            // Runs right before any camera that draws the game's menu layer, so the copy can never show on the main screen.
            Camera.onPreCull -= OnPreCull;
            Camera.onPreCull += OnPreCull;
            diagnoseAt = Time.realtimeSinceStartup + 2f;
            MelonLogger.Msg(string.Format("map copy created: key={0} mode={1} layer={2} target={3}x{4} ui={5}x{6}", key, mode, layer, width, height, useW, useH));
            return null;
        }

        public void Tick()
        {
            if (root == null) return;
            if (instance != null) Relayer();
            if (strayScans < 12 && Time.realtimeSinceStartup >= strayAt)
            {
                strayAt = Time.realtimeSinceStartup + 0.4f;
                strayScans++;
                HideStrays();
            }
            if (Time.realtimeSinceStartup >= isolateAt)
            {
                isolateAt = Time.realtimeSinceStartup + 1f;
                Isolate();
                if (Age < 30f) WatchCameras();
            }
            if (diagnoseAt <= 0f || Time.realtimeSinceStartup < diagnoseAt) return;
            diagnoseAt = 0f;
            try
            {
                Type panel = GameApi.Find("UIPanel");
                Type world = GameApi.Find("GUIMapWorld");
                int panels = panel != null ? root.GetComponentsInChildren(panel, true).Length : -1;
                int transforms = root.GetComponentsInChildren<Transform>(true).Length;
                Component w = world != null ? root.GetComponentInChildren(world, true) : null;
                string state = w != null ? GameApi.GetField(w, world, "isLocalMap") + " / " + GameApi.GetField(w, world, "currentStatus") : "no GUIMapWorld";
                MelonLogger.Msg(string.Format("map copy check: transforms={0} panels={1} world={2}", transforms, panels, state));
                if (afterDumps < 3)
                {
                    afterDumps++;
                    DumpCameras("after");
                }
                LogSelected();
                Type cap = GameApi.Find("CapturedBgNGUI");
                if (cap != null)
                {
                    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(cap))
                    {
                        Component c = o as Component;
                        if (c == null) continue;
                        MelonLogger.Msg("captured bg " + c.name + " layer=" + c.gameObject.layer + " owned=" + c.transform.IsChildOf(root.transform) + " parent=" + (c.transform.parent != null ? c.transform.parent.name : "-"));
                    }
                }
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

        public void Destroy(string reason)
        {
            if (root != null) MelonLogger.Msg(string.Format("map copy removed: {0} (age {1:0.0}s)", reason, Age));
            Destroy();
        }

        public void Destroy()
        {
            diagnoseAt = 0f;
            Key = "";
            Camera.onPreCull -= OnPreCull;
            if (uiCamera != null && !BridgeMod.RealMapOpen) uiCamera.cullingMask = uiMask;
            uiCamera = null;
            if (!object.ReferenceEquals(ownMap, null))
            {
                retired.Add(ownMap);
                if (retired.Count > 8) retired.RemoveAt(0);
                ownMap = null;
            }
            if (cameraObject != null)
            {
                Camera own = cameraObject.GetComponent<Camera>();
                if (own != null) own.enabled = false;
                UnityEngine.Object.Destroy(cameraObject);
            }
            // The window's own cleanup (input unlock, captured background) must run inside an own call, not at end of frame.
            BridgeMod.OwnDepth++;
            try
            {
                foreach (GameObject s in strays) if (s != null) UnityEngine.Object.DestroyImmediate(s);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("could not remove the map copy cleanly: " + e.Message);
            }
            finally
            {
                BridgeMod.OwnDepth--;
            }
            strays.Clear();
            cameraObject = null;
            root = null;
            instance = null;
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

        private void OnPreCull(Camera cam)
        {
            try
            {
                if (root == null || cam == null) return;
                if (uiCamera != null && cam == uiCamera && !BridgeMod.RealMapOpen && cam.cullingMask != uiMask) cam.cullingMask = uiMask;
                if (instance == null) return;
                if ((cam.cullingMask & (1 << FirstLayer)) == 0) return;
                Relayer();
            }
            catch (Exception)
            {
            }
        }

        // The game also makes a full-screen "captured background" object for its map window, on the main layer.
        private void HideStrays()
        {
            if (BridgeMod.RealMapOpen) return;
            Type cap = GameApi.Find("CapturedBgNGUI");
            if (cap == null) return;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(cap))
            {
                Component c = o as Component;
                if (c == null || !c.gameObject.activeSelf || c.transform.IsChildOf(root.transform) || preexisting.Contains(c.GetInstanceID())) continue;
                c.gameObject.SetActive(false);
                strays.Add(c.gameObject);
                MelonLogger.Msg("hid the map copy's captured background (" + c.name + ")");
            }
        }

        // The game moves parts of its windows onto its own UI layer; pull them back every frame.
        private void Relayer()
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer == layer) continue;
                if (readyAt <= 0f) readyAt = Time.realtimeSinceStartup + 0.4f;
                if (fixLogged < 12)
                {
                    fixLogged++;
                    MelonLogger.Msg("moved " + t.name + " from layer " + t.gameObject.layer + " back to " + layer);
                }
                t.gameObject.layer = layer;
            }
        }

        // Only the copy's own camera may draw its layer, or the game's UI camera shows it on the main screen.
        private void Isolate()
        {
            if (layer < 0 || cameraObject == null) return;
            int bit = 1 << layer;
            Camera own = cameraObject.GetComponent<Camera>();
            foreach (Camera c in Camera.allCameras)
            {
                if (c == own) continue;
                if ((c.cullingMask & bit) != 0) c.cullingMask &= ~bit;
            }
        }

        private void LogCameras(Camera main, float useW, float useH)
        {
            camerasLogged = true;
            MelonLogger.Msg(string.Format("ui camera {0}: aspect={1} rect={2} ortho={3} size={4} pixels={5}x{6}; fixed ui {7}x{8}; screen {9}x{10}",
                main.name, main.aspect, main.rect, main.orthographic, main.orthographicSize, main.pixelWidth, main.pixelHeight, useW, useH, Screen.width, Screen.height));
            DumpCameras("before");
        }

        private static void DumpCameras(string tag)
        {
            foreach (Camera c in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                MelonLogger.Msg(string.Format("{0} camera {1}: on={2} mask=0x{3:X} depth={4} clear={5} rect={6} target={7}",
                    tag, c.name, c.enabled && c.gameObject.activeInHierarchy, c.cullingMask, c.depth, c.clearFlags, c.rect, c.targetTexture != null));
            }
        }

        // Logs every camera again the moment any of them is switched off or has its layer mask changed while the copy exists.
        private void WatchCameras()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (Camera c in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                sb.Append(c.name).Append(':').Append(c.enabled && c.gameObject.activeInHierarchy).Append(':').Append(c.cullingMask).Append(';');
            }
            string signature = sb.ToString();
            if (signature == lastSignature) return;
            bool first = lastSignature == null;
            lastSignature = signature;
            if (first || cameraChanges >= 6) return;
            cameraChanges++;
            MelonLogger.Msg("camera state changed while the copy exists");
            DumpCameras("changed");
        }

        private static void LogSelected()
        {
            try
            {
                Type ui = GameApi.Find("UICamera");
                if (ui != null) MelonLogger.Msg("controller focus: " + GameApi.Invoke(null, ui, "get_selectedObject"));
            }
            catch (Exception e)
            {
                MelonLogger.Msg("controller focus unavailable: " + e.Message);
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
