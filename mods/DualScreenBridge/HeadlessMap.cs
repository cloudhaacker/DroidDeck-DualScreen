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
        private static int widgetDumps;
        // The view inside the copy: zoom and a centre in picture pixels (x right, y down), clamped to the map rows that are sent.
        private const float DefaultZoom = 2f, MinZoom = 1f, MaxZoom = 4f;
        private float zoom = DefaultZoom;
        private bool follow = true;
        private float viewX, viewY;
        private bool heroSeen;
        private string createdMarker;
        // The game swaps the hero's marker (arrow outside, star inside) as soon as a building is entered or left; the copy built before that is out of date.
        public bool MarkerSwapped { get; private set; }
        private float baseOrtho;
        private Vector3 basePos;
        private Camera ownCamera;
        private Transform cursor;
        private float cursorScanAt;
        private float lastZoom = -1f, lastX = -1f, lastY = -1f;
        private Component ownTown, ownWorld;
        private float cursorSetAt;
        private int cursorLogs;
        private volatile bool dirty;
        private int createdMode;
        private Transform iconRoot;
        private float localRefreshAt;
        private Vector3 localRefreshPos = new Vector3(float.MaxValue, 0f, 0f);
        private int localRefreshLogs;
        private string markerName = "map_cursor";
        private int pixelLogs;
        private Vector3 lastCursorPos;

        public bool TakeDirty()
        {
            bool was = dirty;
            dirty = false;
            return was;
        }

        public string Key = "";

        public float Age { get { return Time.realtimeSinceStartup - createdAt; } }

        public bool Active { get { return root != null; } }

        // The map window builds its parts over the first second or so; frames before that are black.
        public bool Ready
        {
            get
            {
                if (root == null) return false;
                // The hero marker has to be placed too, or the first pictures show the view centered before it finds the star.
                return (readyAt > 0f && Time.realtimeSinceStartup >= readyAt && (heroSeen || !follow)) || Age > 3f;
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
            ownCamera = cam;
            baseOrtho = cam.orthographicSize;
            basePos = cameraObject.transform.position;
            zoom = BridgeMod.StartZoom;
            follow = true;
            cursor = null;
            cursorScanAt = 0f;
            ownTown = ownWorld = null;
            iconRoot = null;
            localRefreshAt = 0f;
            localRefreshPos = new Vector3(float.MaxValue, 0f, 0f);
            localRefreshLogs = 0;
            createdMode = mode;
            cursorSetAt = 0f;
            pixelLogs = 0;
            dirty = true;
            cursorLogs = 0;
            viewX = width / 2f;
            viewY = height * (CropTop + CropBottom) / 2f;
            lastZoom = lastX = lastY = -1f;
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
            heroSeen = false;
            createdMarker = null;
            MarkerSwapped = false;
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
            if (instance != null && Time.realtimeSinceStartup >= cursorSetAt)
            {
                cursorSetAt = Time.realtimeSinceStartup + 0.2f;
                PlaceCursor();
            }
            if (cameraObject != null) ApplyView();
            if (instance != null) Relayer();
            if (Time.realtimeSinceStartup >= iconsAt)
            {
                iconsAt = Time.realtimeSinceStartup + 0.5f;
                ApplyIcons();
            }
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
                if (widgetDumps < 4) { widgetDumps++; DumpWidgets(); }
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

        // Touch gestures from the second screen: zoom:<factor> multiplies the zoom, pan:<dx>,<dy> drags by fractions of the picture, view:reset goes back to the start view.
        // The shop, church, inn and well icons can be hidden from the app; the choice outlives copies, which are rebuilt often.
        public static bool IconsHidden;
        private static readonly string[] PlaceSprites = { "map_Well", "map_Tool", "map_Protector", "map_INN", "map_G", "map_Chrch", "map_Bar", "map_Arms" };
        private readonly System.Collections.Generic.List<Component> iconsOff = new System.Collections.Generic.List<Component>();
        private float iconsAt;

        // Switches the copy's place icons off (or back on) by disabling the sprite widgets; only widgets this switched off are switched back on.
        private void ApplyIcons()
        {
            if (IconsHidden)
            {
                Type widget = GameApi.Find("UIWidget");
                if (widget == null) return;
                bool changed = false;
                foreach (Component c in root.GetComponentsInChildren(widget, false))
                {
                    Behaviour b = c as Behaviour;
                    if (b == null || !b.enabled) continue;
                    string sprite = GameApi.GetField(c, null, "mSpriteName") as string;
                    if (sprite == null || Array.IndexOf(PlaceSprites, sprite) < 0) continue;
                    b.enabled = false;
                    iconsOff.Add(c);
                    changed = true;
                }
                if (changed) dirty = true;
            }
            else if (iconsOff.Count > 0)
            {
                foreach (Component c in iconsOff)
                {
                    Behaviour b = c as Behaviour;
                    if (b != null) b.enabled = true;
                }
                iconsOff.Clear();
                dirty = true;
            }
        }

        public void Gesture(string command)
        {
            dirty = true;
            if (command.StartsWith("icons:")) { IconsHidden = command == "icons:0"; iconsAt = 0f; return; }
            try
            {
                System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
                if (command == "view:reset") { zoom = BridgeMod.StartZoom; follow = true; return; }
                if (command.StartsWith("zoom:"))
                {
                    zoom = Mathf.Clamp(zoom * float.Parse(command.Substring(5), c), MinZoom, MaxZoom);
                    return;
                }
                if (command.StartsWith("pan:") && target != null)
                {
                    string[] parts = command.Substring(4).Split(',');
                    float dx = float.Parse(parts[0], c), dy = float.Parse(parts[1], c);
                    float rows = target.height * (CropBottom - CropTop);
                    follow = false;
                    viewX -= dx * target.width / zoom;
                    viewY -= dy * rows / zoom;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("bad gesture '" + command + "': " + e.Message);
            }
        }

        // The game places the hero's marker only when its own map opens, which the copy skips, so the same calls run here.
        private void PlaceCursor()
        {
            if (!Ready || !GameApi.HasPlayer()) return;
            BridgeMod.OwnDepth++;
            try
            {
                Type townType = GameApi.Find("GUIMapTownAndDungeon"), worldType = GameApi.Find("GUIMapWorld");
                if (ownTown == null && townType != null) ownTown = instance.GetComponentInChildren(townType, true);
                if (ownWorld == null && worldType != null) ownWorld = instance.GetComponentInChildren(worldType, true);
                object[] info = new object[] { Vector3.zero, Vector3.zero };
                bool world = createdMode == 1 && ownWorld != null;
                // The game places tiles and markers with world positions that assume its menu sits at the origin.
                Vector3 savedRoot = root.transform.position;
                root.transform.position = new Vector3(0f, 0f, savedRoot.z);
                try
                {
                    if (world)
                    {
                        GameApi.Invoke(ownWorld, worldType, "GetPlayerInfo", info);
                        GameApi.SetField(ownWorld, worldType, "playerPos", info[0]);
                        GameApi.SetField(ownWorld, worldType, "playerRot", info[1]);
                        bool local = Convert.ToBoolean(GameApi.GetField(ownWorld, worldType, "isLocalMap"));
                        if (local) RefreshLocalMap(worldType, (Vector3)info[0]);
                        GameApi.Invoke(ownWorld, worldType, local ? "SetLocalCursor" : "SetWorldCursor");
                        GameObject icons = GameApi.GetField(ownWorld, worldType, local ? "bigMapIcon" : "smallMapIcon") as GameObject;
                        Transform rootIcons = icons != null ? icons.transform : null;
                        if (rootIcons != iconRoot) { iconRoot = rootIcons; cursor = null; cursorScanAt = 0f; }
                    }
                    else if (ownTown != null)
                    {
                        GameApi.Invoke(ownTown, townType, "GetPlayerInfo", info);
                        GameApi.SetField(null, townType, "playerPos", info[0]);
                        GameApi.SetField(null, townType, "playerRot", info[1]);
                        int mode = Convert.ToInt32(GameApi.Invoke(null, GameApi.Find("guidmap"), "get_GetDQMapMode"));
                        if (mode == 1) GameApi.Invoke(ownTown, townType, "SetCursorDungeon");
                        else if (mode == 2) GameApi.Invoke(ownTown, townType, "SetCursorTown");
                    }
                }
                finally
                {
                    root.transform.position = savedRoot;
                }
                bool interior;
                if (world) interior = Convert.ToInt32(GameApi.GetField(null, worldType, "interior")) != 0;
                else interior = Convert.ToInt32(GameApi.GetField(GameApi.Invoke(null, GameApi.Find("guidmap"), "get_GetTownMapInfo"), null, "Interior")) != 0;
                string wanted = interior ? "map_star" : "map_cursor";
                if (createdMarker == null) createdMarker = wanted;
                else if (wanted != createdMarker && !MarkerSwapped)
                {
                    MarkerSwapped = true;
                    MelonLogger.Msg("hero marker changed from " + createdMarker + " to " + wanted + "; holding the picture until the map copy is built again");
                }
                if (wanted != markerName) { markerName = wanted; cursor = null; cursorScanAt = 0f; }
                // The game positions the marker as if the map sat at the origin; the copy is far away.
                Transform holder = cursor != null ? cursor.parent : null;
                if (holder != null && holder.position.x < FarAway / 2f && holder.position.x > -10000f)
                {
                    holder.position += new Vector3(root.transform.position.x, root.transform.position.y, 0f);
                }
                if (cursor != null && cursor.position != lastCursorPos)
                {
                    lastCursorPos = cursor.position;
                    dirty = true;
                }
                if (cursorLogs < 6 && cursor != null)
                {
                    cursorLogs++;
                    MelonLogger.Msg(string.Format("hero marker: world={0} pos={1} active={2}", world, cursor.position, cursor.gameObject.activeInHierarchy));
                }
            }
            catch (Exception e)
            {
                if (cursorLogs < 6) { cursorLogs++; MelonLogger.Warning("could not place the hero marker: " + (e.InnerException ?? e).Message); }
            }
            finally
            {
                BridgeMod.OwnDepth--;
            }
        }

        // The local map keeps the hero's marker at its centre and lays its tiles out around the hero once, when the window opens.
        private void RefreshLocalMap(Type worldType, Vector3 pos)
        {
            if (Time.realtimeSinceStartup < localRefreshAt || (pos - localRefreshPos).sqrMagnitude < 60f * 60f) return;
            localRefreshAt = Time.realtimeSinceStartup + 0.4f;
            localRefreshPos = pos;
            object[] spot = new object[] { pos, GameApi.GetField(null, worldType, "interior") };
            GameApi.Invoke(null, GameApi.Find("guidmap"), "SetSpecificPlayerPos", spot);
            GameApi.SetField(ownWorld, worldType, "playerPos", spot[0]);
            GameApi.SetField(null, worldType, "interior", spot[1]);
            GameApi.SetField(ownWorld, worldType, "localMapSize", GameApi.Invoke(ownWorld, worldType, "GetLocalMapSize"));
            GameApi.Invoke(ownWorld, worldType, "GetLocalMapInfo", GameApi.GetField(null, worldType, "MapTexPutPos"), GameApi.GetField(null, worldType, "MapTexPutIndex"));
            GameApi.Invoke(ownWorld, worldType, "SetMapDrawItem");
            if (localRefreshLogs < 6) { localRefreshLogs++; MelonLogger.Msg("local map re-laid out around " + pos); }
            dirty = true;
        }

        // The hero's marker on the map copy, in picture pixels, or false while the game keeps it parked off the picture.
        private bool HeroPixel(float unit, out float px, out float py)
        {
            px = py = 0f;
            if (cursor == null || !cursor.gameObject.activeInHierarchy)
            {
                cursor = null;
                if (Time.realtimeSinceStartup < cursorScanAt || root == null) return false;
                cursorScanAt = Time.realtimeSinceStartup + 1f;
                foreach (Transform t in (iconRoot != null ? iconRoot : root.transform).GetComponentsInChildren<Transform>(false))
                {
                    if (t.name == markerName) { cursor = t; break; }
                }
                if (cursor == null) return false;
            }
            px = target.width / 2f + (cursor.position.x - basePos.x) / unit;
            py = target.height / 2f - (cursor.position.y - basePos.y) / unit;
            if (pixelLogs < 8) { pixelLogs++; MelonLogger.Msg(string.Format("hero at picture {0:0},{1:0} ({2})", px, py, markerName)); }
            return px > 0f && px < target.width && py > 0f && py < target.height;
        }

        private void ApplyView()
        {
            if (ownCamera == null || target == null || baseOrtho <= 0f) return;
            float w = target.width, h = target.height;
            float unit = 2f * baseOrtho / h;
            float top = h * CropTop, bottom = h * CropBottom, centreY = (top + bottom) / 2f;
            if (follow)
            {
                float hx, hy;
                // The game parks or swaps the marker while it changes scenes; the view stays where it was instead of jumping to the middle.
                if (HeroPixel(unit, out hx, out hy)) { viewX = hx; viewY = hy; heroSeen = true; }
                else if (!heroSeen) { viewX = w / 2f; viewY = centreY; }
            }
            float halfW = w / zoom / 2f, halfH = (bottom - top) / zoom / 2f;
            viewX = Mathf.Clamp(viewX, halfW, w - halfW);
            viewY = Mathf.Clamp(viewY, top + halfH, bottom - halfH);
            if (zoom == lastZoom && viewX == lastX && viewY == lastY) return;
            lastZoom = zoom; lastX = viewX; lastY = viewY;
            dirty = true;
            // The sent rows are centred above the middle of the picture, so the camera sits a little lower than the view centre.
            float cx = viewX, cy = viewY + (h / 2f - centreY) / zoom;
            cameraObject.transform.position = new Vector3(basePos.x + (cx - w / 2f) * unit, basePos.y - (cy - h / 2f) * unit, basePos.z);
            ownCamera.orthographicSize = baseOrtho / zoom;
        }

        // The map window fills only the middle of the picture; its zoom buttons, Icons Off and close sit below it.
        // Rows kept, as fractions of the picture height measured from the top (all of the map pictures fit inside).
        private const float CropTop = 0.19f;
        private const float CropBottom = 0.727f;

        public byte[] ReadRaw(out int width, out int height)
        {
            width = height = 0;
            if (target == null || !target.IsCreated()) return null;
            int top = 0, rows = target.height;
            if (BridgeMod.Crop)
            {
                top = Mathf.Clamp(Mathf.RoundToInt(target.height * CropTop), 0, target.height - 16);
                rows = Mathf.Clamp(Mathf.RoundToInt(target.height * CropBottom) - top, 16, target.height - top);
            }
            if (readback != null && (readback.width != target.width || readback.height != rows))
            {
                UnityEngine.Object.Destroy(readback);
                readback = null;
            }
            if (readback == null) readback = new Texture2D(target.width, rows, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                // Render textures count rows from the bottom.
                readback.ReadPixels(new Rect(0, target.height - top - rows, target.width, rows), 0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
            }
            width = target.width;
            height = rows;
            return readback.GetRawTextureData();
        }

        public void Destroy(string reason)
        {
            if (root != null) MelonLogger.Msg(string.Format("map copy removed: {0} (age {1:0.0}s); {2}", reason, Age, GameApi.MiniMapState()));
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
            iconsOff.Clear();
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
        // One-off inventory of the copy's visible NGUI widgets, in pixels of the full bottom picture (this NGUI is old: a widget's size is its scale times relativeSize).
        private void DumpWidgets()
        {
            Type widget = GameApi.Find("UIWidget");
            if (widget == null || cameraObject == null || target == null) { MelonLogger.Msg("widgets: UIWidget not found"); return; }
            Camera cam = cameraObject.GetComponent<Camera>();
            Component[] all = root.GetComponentsInChildren(widget, true);
            int shown = 0, failed = 0;
            string firstError = "";
            MelonLogger.Msg(string.Format("widgets: {0} in the copy, picture {1}x{2}: path [type] centre x,y (px from top left) size w x h pivot depth detail", all.Length, target.width, target.height));
            foreach (Component c in all)
            {
                if (shown >= 200) break;
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                try
                {
                    Transform t = c.transform;
                    Vector3 p = cam.WorldToViewportPoint(t.position);
                    Vector3 right = cam.WorldToViewportPoint(t.position + t.right * t.lossyScale.x);
                    Vector3 up = cam.WorldToViewportPoint(t.position + t.up * t.lossyScale.y);
                    Vector2 rel = (Vector2)GameApi.Invoke(c, null, "get_relativeSize");
                    float w = Mathf.Abs(right.x - p.x) * target.width * rel.x;
                    float h = Mathf.Abs(up.y - p.y) * target.height * rel.y;
                    string detail = "";
                    string kind = c.GetType().Name;
                    try
                    {
                        if (kind == "UISprite" || kind == "UISlicedSprite" || kind == "UIFilledSprite") detail = "sprite=" + GameApi.GetField(c, null, "mSpriteName");
                        else if (kind == "UILabel") detail = "text=" + GameApi.GetField(c, null, "mText");
                        else detail = "texture=" + GameApi.Invoke(c, null, "get_mainTexture");
                    }
                    catch (Exception) { }
                    if (detail.Length > 60) detail = detail.Substring(0, 60);
                    string path = c.name;
                    Transform parent = t.parent;
                    for (int i = 0; i < 2 && parent != null && parent.name != "DualScreenMapRoot"; i++, parent = parent.parent) path = parent.name + "/" + path;
                    MelonLogger.Msg(string.Format("widget: {0} [{1}] {2:0},{3:0} size {4:0}x{5:0} pivot={6} depth={7} {8}", path, kind, p.x * target.width, (1f - p.y) * target.height, w, h,
                        GameApi.Invoke(c, null, "get_pivot"), GameApi.Invoke(c, null, "get_depth"), detail));
                    shown++;
                }
                catch (Exception e)
                {
                    failed++;
                    if (firstError.Length == 0) firstError = e.GetType().Name + ": " + e.Message;
                }
            }
            if (failed > 0) MelonLogger.Msg("widgets: " + failed + " could not be read (first error: " + firstError + ")");
        }

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
