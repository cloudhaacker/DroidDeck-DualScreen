using System.Collections.Generic;
using UnityEngine;

namespace DualScreenBridge
{
    // The toolkit draws the minimap with its own camera. The minimap itself must stay open: its init is what loads the
    // map data of a new area, which the map copy reads. Only that camera's drawing is switched off.
    internal static class MiniMapHider
    {
        private const string CameraName = "DQ8MR minimap camera";
        private static readonly Dictionary<int, bool> known = new Dictionary<int, bool>();

        public static void Start()
        {
            Camera.onPreCull += OnPreCull;
        }

        private static void OnPreCull(Camera cam)
        {
            if (BridgeMod.KeepMiniMap || BridgeMod.NoCopy) return;
            BridgeClient client = BridgeMod.Client;
            if (client == null || !client.Connected) return;
            int id = cam.GetInstanceID();
            bool hide;
            if (!known.TryGetValue(id, out hide))
            {
                hide = cam.name == CameraName;
                known[id] = hide;
            }
            if (hide) cam.cullingMask = 0;
        }
    }
}
