using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DualScreenBridge
{
    // The game's types live in Assembly-CSharp, which the mod does not reference at build time.
    internal static class GameApi
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();

        public static Type Find(string name)
        {
            Type found;
            if (types.TryGetValue(name, out found)) return found;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
                if (asmName != "Assembly-CSharp" && asmName != "Assembly-CSharp-firstpass") continue;
                found = asm.GetType(name);
                if (found != null) break;
            }
            types[name] = found;
            return found;
        }

        public static object Invoke(object target, Type type, string name, params object[] args)
        {
            Type start = target != null ? target.GetType() : type;
            for (Type t = start; t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(All))
                {
                    if (m.Name != name || m.GetParameters().Length != args.Length) continue;
                    return m.Invoke(m.IsStatic ? null : target, args);
                }
            }
            throw new MissingMethodException(start.Name + "." + name);
        }

        public static object GetField(object target, Type type, string name)
        {
            Type start = target != null ? target.GetType() : type;
            for (Type t = start; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, All);
                if (f != null) return f.GetValue(f.IsStatic ? null : target);
            }
            throw new MissingFieldException(start.Name, name);
        }

        public static void SetField(object target, Type type, string name, object value)
        {
            Type start = target != null ? target.GetType() : type;
            for (Type t = start; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, All);
                if (f != null) { f.SetValue(f.IsStatic ? null : target, value); return; }
            }
            throw new MissingFieldException(start.Name, name);
        }

        public static bool HasPlayer()
        {
            try
            {
                Type loop = Find("mloop");
                Type cpp = Find("CppClass");
                if (loop == null || cpp == null) return false;
                object scene = Invoke(null, loop, "GetMainScene");
                if (scene == null) return false;
                object chara = Invoke(scene, null, "GetControlChara");
                if (chara == null) return false;
                foreach (MethodInfo m in cpp.GetMethods(All))
                {
                    if (m.Name == "op_Implicit" && m.ReturnType == typeof(bool)) return (bool)m.Invoke(null, new object[] { chara });
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The game's own test for opening its menu: walking, in a walking control mode, and no message window up.
        public static bool PlayerFree(out string detail)
        {
            detail = "";
            try
            {
                Type main = Find("dq_main");
                if (main == null) return true;
                bool walking = (bool)Invoke(null, main, "IsWalkLoop");
                int mode = Convert.ToInt32(GetField(null, main, "MainCtrlMode"));
                int message = 0;
                object info = GetField(null, Find("dq_ctrl"), "main_ctrl_info");
                if (info != null) message = Convert.ToInt32(GetField(info, info.GetType(), "mes_draw_sys"));
                detail = "walking=" + walking + " mode=" + mode + " message=" + message;
                return walking && (mode == 1 || mode == 5 || mode == 8) && (message & 1) == 0;
            }
            catch (Exception e)
            {
                detail = "unavailable: " + e.Message;
                return true;
            }
        }

        // The game names its main camera "a battle" for the whole fight and "a main" again afterwards.
        public static bool InBattle()
        {
            foreach (Camera c in Camera.allCameras)
            {
                if (c.name == "a battle") return true;
            }
            return false;
        }

        public static string MiniMapState()
        {
            Type hud = Find("DQ8MR.Mods.Toolkit.FieldHud");
            string text = "minimap: open=" + Read(() => GetField(null, Find("guidmap"), "isMiniMapOpen"));
            if (hud == null) return text + " (no FieldHud)";
            text += " visible=" + Read(() => GetField(null, hud, "Visible"));
            text += " xHidden=" + Read(() => GetField(null, hud, "_xHidden"));
            text += " noMapHidden=" + Read(() => GetField(null, hud, "_noMapHidden"));
            text += " settling=" + Read(() => Invoke(null, hud, "get_MinimapSettling"));
            text += " hiddenObjects=" + Read(() =>
            {
                System.Collections.ICollection list = GetField(null, hud, "Hidden") as System.Collections.ICollection;
                return list == null ? null : (object)list.Count;
            });
            return text;
        }

        private static string Read(Func<object> get)
        {
            try
            {
                object value = get();
                return value == null ? "null" : value.ToString();
            }
            catch (Exception e)
            {
                return "?(" + e.GetType().Name + ")";
            }
        }

        public static int MapNo()
        {
            return Convert.ToInt32(Invoke(null, Find("dq_mapjump"), "GetMainMapNo"));
        }

        // The title scene stays loaded from the title screen on; before it the game is still booting.
        public static bool TitleUp()
        {
            return UnityEngine.SceneManagement.SceneManager.GetSceneByName("TitleNGUI").isLoaded;
        }

        // The place name the game lists for the current map; empty when it has none.
        public static string LocationName()
        {
            try
            {
                string title = Invoke(null, Find("dq_mapinfo"), "GetMapTitle", MapNo()) as string;
                if (title == null) return "";
                System.Text.StringBuilder clean = new System.Text.StringBuilder();
                foreach (char c in title)
                {
                    if (c >= ' ') clean.Append(c);
                }
                string text = clean.ToString().Trim();
                if (text.Length > 2 && text[0] == '(' && text[text.Length - 1] == ')') text = text.Substring(1, text.Length - 2);
                return text.Length > 60 ? text.Substring(0, 60) : text;
            }
            catch (Exception)
            {
                return "";
            }
        }

        // Whether the game lets the player see the world map from where they stand.
        public static bool WorldMapAvailable()
        {
            object mapNo = Invoke(null, Find("dq_mapjump"), "GetMainMapNo");
            return (bool)Invoke(null, Find("guidmap"), "CheckOpenFieldMap", mapNo);
        }

        // Mirrors the game's own map button: 2 town or dungeon map, 1 world map, 0 no map here.
        public static int AutoMode()
        {
            Type guide = Find("guidmap");
            object mapNo = Invoke(null, Find("dq_mapjump"), "GetMainMapNo");
            int navi = Convert.ToInt32(Invoke(null, guide, "IsEnableOpenNaviMap", mapNo));
            if (navi > 0)
            {
                if (navi == 2 && !(bool)Invoke(null, guide, "CheckOpenNaviMap", mapNo)) return 0;
                return 2;
            }
            return (bool)Invoke(null, guide, "CheckOpenFieldMap", mapNo) ? 1 : 0;
        }
    }
}
