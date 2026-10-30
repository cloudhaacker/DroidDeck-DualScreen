using System;
using System.Collections.Generic;
using System.Reflection;

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
    }
}
