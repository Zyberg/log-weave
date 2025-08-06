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
      InjectLogCallEntry(method, loggerField);
      InjectLogCallExit(method, loggerField);
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


void InjectLogCallExit(MethodDefinition method, FieldDefinition loggerField)
{
  var processor = method.Body.GetILProcessor();
  var emptyArrayCall = GetArrayEmptyObjectMethod();

  var retInstructions = method.Body.Instructions
    .Where(i => i.OpCode == OpCodes.Ret)
    .ToList();

  foreach (var ret in retInstructions)
  {
    // Create fresh instructions each time
    var newInstructions = new List<Instruction>
    {
      processor.Create(OpCodes.Ldarg_0),
      processor.Create(OpCodes.Ldfld, loggerField),
      processor.Create(OpCodes.Ldstr, $"[InjectedIL] Exited {method.Name}"),
      processor.Create(OpCodes.Call, emptyArrayCall),
      processor.Create(OpCodes.Call, LogInfoMethodReference)
    };

    foreach (var instr in newInstructions)
    {
      processor.InsertBefore(ret, instr);
    }
  }
}
void InjectLogCallEntry(MethodDefinition method, FieldDefinition loggerField)
{
  var newInstructions = new List<Instruction>();

  // Create variable to hold the array to be passed to the LogEntry() method       
  var arrayDef = new VariableDefinition(new ArrayType(ModuleDefinition.TypeSystem.Object));
   
  // Add variable to the method
  method.Body.Variables.Add(arrayDef);

  var processor = method.Body.GetILProcessor();

  // laod the this._logger
  newInstructions.Add(processor.Create(OpCodes.Ldarg_0));
  newInstructions.Add(processor.Create(OpCodes.Ldfld, loggerField));

  var format = string.Join(", ", method.Parameters.Select((p, i) => $"{p.Name}={{{i}}}"));
  // Load the method name to the stack
  newInstructions.Add(processor.Create(OpCodes.Ldstr, $"[InjectedIL] Entered {method.Name} [{format}]")); 

  // Load to the stack the number of parameters         
  newInstructions.Add(processor.Create(OpCodes.Ldc_I4, method.Parameters.Count));               

  // Create a new object[] with the number loaded to the stack           
  newInstructions.Add(processor.Create(OpCodes.Newarr, ModuleDefinition.TypeSystem.Object)); 

  // Store the array in the local variable
  newInstructions.Add(processor.Create(OpCodes.Stloc, arrayDef)); 

  // Loop through the parameters of the method to run
  for (int i = 0; i < method.Parameters.Count; i++)
  {
    // Load the array from the local variable
    newInstructions.Add(processor.Create(OpCodes.Ldloc, arrayDef)); 

    // Load the index
    newInstructions.Add(processor.Create(OpCodes.Ldc_I4, i)); 

    // Load the argument of the original method (note that parameter 0 is 'this', that's omitted)
    newInstructions.Add(processor.Create(OpCodes.Ldarg, i+1)); 

    if (method.Parameters[i].ParameterType.IsValueType)
    {
      // Boxing is needed for value types
      newInstructions.Add(processor.Create(OpCodes.Box, method.Parameters[i].ParameterType)); 
    }
    else
    { 
      // Casting for reference types
      newInstructions.Add(processor.Create(OpCodes.Castclass, ModuleDefinition.TypeSystem.Object)); 
    }
    // Store in the array
    newInstructions.Add(processor.Create(OpCodes.Stelem_Ref)); 
  }

  // Load the array to the stack
  newInstructions.Add(processor.Create(OpCodes.Ldloc, arrayDef)); 

  // Call the LogEntry() method
  newInstructions.Add(processor.Create(OpCodes.Call, LogInfoMethodReference)); 

  // Add the new instructions in referse order
  foreach (var newInstruction in newInstructions.Reverse<Instruction>()) 
  {
    var firstInstruction = method.Body.Instructions[0];
    processor.InsertBefore(firstInstruction, newInstruction);
  }

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
