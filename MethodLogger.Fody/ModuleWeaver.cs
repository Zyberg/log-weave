using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Cecil.Cil;
using Fody;
using System;

public class ModuleWeaver : BaseModuleWeaver
{
  public override void Execute()
  {
    LogInfo($"[MethodLogger] Weaving assembly {ModuleDefinition.Name}");
    foreach (var type in ModuleDefinition.Types)
    {
      foreach (var method in type.Methods.Where(m => m.HasBody && !m.IsConstructor))
      {
        var processor = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        var writeLine = ModuleDefinition.ImportReference(
            typeof(Console).GetMethod("WriteLine", new[] { typeof(string) })
            );

        var message = $"[InjectedLogger] Entered {method.FullName}";
        processor.InsertBefore(first, processor.Create(OpCodes.Ldstr, message));
        processor.InsertBefore(first, processor.Create(OpCodes.Call, writeLine));

      }
    }
  }


  #region GetAssembliesForScanning

  public override IEnumerable<string> GetAssembliesForScanning()
  {
    yield return "netstandard";
    yield return "mscorlib";
  }

  #endregion

}
