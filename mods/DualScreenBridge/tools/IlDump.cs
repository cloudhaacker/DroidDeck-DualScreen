using System;
using Mono.Cecil;

class IlDump
{
    static void Main(string[] a)
    {
        var rp = new DefaultAssemblyResolver();
        rp.AddSearchDirectory(a[1]);
        var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = rp });
        var t = asm.MainModule.GetType(a[2]);
        foreach (var m in t.Methods)
        {
            if (m.Name != a[3] || !m.HasBody) continue;
            Console.WriteLine("== " + t.FullName + "." + m.Name + "(" + string.Join(", ", System.Linq.Enumerable.Select(m.Parameters, p => p.ParameterType.Name)) + ")");
            foreach (var i in m.Body.Instructions) Console.WriteLine("  " + i.Offset.ToString("x4") + " " + i.OpCode + " " + (i.Operand is Mono.Cecil.Cil.Instruction ? "->" + ((Mono.Cecil.Cil.Instruction)i.Operand).Offset.ToString("x4") : i.Operand));
        }
    }
}
