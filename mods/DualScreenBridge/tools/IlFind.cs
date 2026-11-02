using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class IlFind
{
    static void Main(string[] a)
    {
        var rp = new DefaultAssemblyResolver();
        rp.AddSearchDirectory(a[1]);
        var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = rp });
        string mode = a[2];
        string needle = a[3];
        foreach (var t in asm.MainModule.GetTypes())
        {
            if (mode == "types") { if (t.FullName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) Console.WriteLine(t.FullName + " : " + (t.BaseType != null ? t.BaseType.Name : "")); continue; }
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    string op = i.Operand is MethodReference mr ? mr.DeclaringType.Name + "." + mr.Name : i.Operand is FieldReference fr ? fr.DeclaringType.Name + "." + fr.Name : i.Operand as string;
                    if (op != null && op.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine(t.FullName + "." + m.Name + "  ->  " + op);
                        break;
                    }
                }
            }
        }
    }
}
