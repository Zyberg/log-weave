using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Cecil.Cil;
using Fody;
using System;

public class ModuleWeaver : BaseModuleWeaver
{
    public MethodReference LogInfoMethodReference { get; private set; }

    public override void Execute()
  {

    LogInfoMethodReference = FindLogInformationMethod();

    foreach (var type in ModuleDefinition.Types)
    {
      ProcessType(type);
    }
  }

  void ProcessType(TypeDefinition type)
  {
    if (!type.HasMethods || !type.Fields.Any())
      return;

    // Check for a private readonly ILogger<T> field
    var loggerField = type.Fields.FirstOrDefault(f =>
        f.IsPrivate &&
        f.IsInitOnly &&
        f.FieldType.FullName.StartsWith("Microsoft.Extensions.Logging.ILogger`1"));

    if (loggerField == null)
      return;

    foreach (var method in type.Methods.Where(m => m.HasBody && !m.IsConstructor))
    {
      InjectLogCall(method, loggerField);
    }
  }

   private MethodReference GetArrayEmptyObjectMethod()
{
    // Find System.Array type
    var arrayType = ModuleDefinition
        .TypeSystem
        .Object
        .Resolve()
        .Module
        .Types
        .FirstOrDefault(t => t.FullName == "System.Array");

    if (arrayType == null)
    {
        LogError("Could not resolve System.Array.");
        return null;
    }

    // Find the generic method definition
    var emptyDef = arrayType.Methods
        .FirstOrDefault(m =>
            m.Name == "Empty" &&
            m.HasGenericParameters &&
            m.Parameters.Count == 0);

    if (emptyDef == null)
    {
        LogError("Could not find Array.Empty<T>() method.");
        return null;
    }

    // Create a GenericInstanceMethod with T = object
    var generic = new GenericInstanceMethod(ModuleDefinition.ImportReference(emptyDef));
    generic.GenericArguments.Add(ModuleDefinition.TypeSystem.Object);

    return ModuleDefinition.ImportReference(generic);
}
void InjectLogCall(MethodDefinition method, FieldDefinition loggerField)
{
  var il = method.Body.GetILProcessor();
  var firstInstruction = method.Body.Instructions.First();



  var parameters = method.Parameters;
  int paramCount = parameters.Count;

  // Format string: "param1={0}, param2={1}, ..."
  var format = string.Join(", ", method.Parameters.Select((p, i) => $"{p.Name}={{{i}}}"));

  // Inject: this._logger
  il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldarg_0));
  il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldfld, loggerField));

  il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldstr, format));
  // 3. Create new object[paramCount]
  il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldc_I4, paramCount));
  il.InsertBefore(firstInstruction, il.Create(OpCodes.Newarr, ModuleDefinition.TypeSystem.Object));

  // 4. Fill the array using `dup`
  for (int i = 0; i < paramCount; i++)
  {
    il.InsertBefore(firstInstruction, il.Create(OpCodes.Dup)); // keep array on stack for next stelem.ref

    il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldc_I4, i)); // array index
    il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldarg, i + 1)); // parameter value

    if (parameters[i].ParameterType.IsValueType)
    {
      il.InsertBefore(firstInstruction, il.Create(OpCodes.Box, ModuleDefinition.ImportReference(parameters[i].ParameterType)));
    }

    il.InsertBefore(firstInstruction, il.Create(OpCodes.Stelem_Ref));
  }


  il.InsertBefore(firstInstruction, il.Create(OpCodes.Call, LogInfoMethodReference));


}


MethodReference FindLogInformationMethod()
{
  foreach (var asmRef in ModuleDefinition.AssemblyReferences)
  {
    AssemblyDefinition asmDef;

    try
    {
      asmDef = AssemblyResolver.Resolve(asmRef);
    }
    catch
    {
      continue; // Skip unresolved assemblies
    }

    foreach (var type in asmDef.MainModule.Types)
    {
      if (type.Name != "LoggerExtensions" || type.Namespace != "Microsoft.Extensions.Logging")
        continue;

      foreach (var method in type.Methods)
      {
        if (!method.IsStatic || method.Name != "LogInformation" || method.Parameters.Count < 3)
          continue;

        if (method.HasGenericParameters) // ❗ filter generic LogInformation<T>()
          continue;

        var p0 = method.Parameters[0].ParameterType;
        var p1 = method.Parameters[1].ParameterType;
        var p2 = method.Parameters[2].ParameterType;

        if (p0.FullName == "Microsoft.Extensions.Logging.ILogger" &&
            p1.FullName == "System.String" &&
            p2.FullName == "System.Object[]")
        {
          LogInfo($"Found correct LogInformation method in {asmDef.Name.Name}");
          return ModuleDefinition.ImportReference(method);
        }
      }
    }
  }

  LogWarning("Could not locate non-generic LoggerExtensions.LogInformation(ILogger, string, object[])");
  return null;
}

  #region GetAssembliesForScanning

  public override IEnumerable<string> GetAssembliesForScanning()
  {
    yield return "netstandard";
    yield return "mscorlib";
    yield return "Microsoft.Extensions.Logging";
    yield return "Microsoft.Extensions.Logging.Abstractions";
  }

  #endregion

}
