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
        var processor = method.Body.GetILProcessor();
        var firstInstruction = method.Body.Instructions.First();

        var methodName = method.Name;
        var loggerFieldRef = loggerField;

        // Reference to LogInformation(string)
        var logInfoMethod = FindLogInformationMethod();

        var emptyArrayCall = GetArrayEmptyObjectMethod();
if (emptyArrayCall == null)
{
    LogError("Could not inject log call — Array.Empty<object>() not found.");
    return;
}

        // Instructions to insert
        processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg_0));
        processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldfld, loggerFieldRef));
        processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldstr, $"Entering {methodName}"));
        processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Call, emptyArrayCall));
        processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Call, logInfoMethod));
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
                if (!method.IsStatic || method.Name != "LogInformation" || method.Parameters.Count < 2)
                    continue;

                var p0 = method.Parameters[0].ParameterType;
                var p1 = method.Parameters[1].ParameterType;

                if (p0.FullName.StartsWith("Microsoft.Extensions.Logging.ILogger") &&
                    p1.FullName == "System.String")
                {
                    LogInfo($"Found LogInformation method in {asmDef.Name.Name}");
                    return ModuleDefinition.ImportReference(method);
                }
            }
        }
    }

    LogWarning("Could not locate Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(ILogger, string)");
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
