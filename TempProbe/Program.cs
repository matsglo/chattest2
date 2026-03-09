using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using System.Reflection;

#pragma warning disable OPENAI001

var assembly = typeof(ResponsesClient).Assembly;

// Probe ResponseTokenUsage
Console.WriteLine("=== ResponseTokenUsage properties ===");
var usageType = assembly.GetTypes().FirstOrDefault(t => t.Name == "ResponseTokenUsage");
if (usageType != null)
{
    foreach (var p in usageType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
}

// Verify FunctionCallResponseItem.FunctionArguments returns BinaryData
Console.WriteLine("\n=== FunctionCallResponseItem.FunctionArguments type ===");
var prop = typeof(FunctionCallResponseItem).GetProperty("FunctionArguments");
Console.WriteLine($"  Type: {prop?.PropertyType.FullName}");

// CreateResponseOptions.Model property type
Console.WriteLine("\n=== CreateResponseOptions.Model property ===");
var modelProp = typeof(CreateResponseOptions).GetProperty("Model");
Console.WriteLine($"  Type: {modelProp?.PropertyType.FullName}");
var modelIdProp = typeof(CreateResponseOptions).GetProperty("ModelId");
Console.WriteLine($"  ModelId: {modelIdProp?.PropertyType.FullName}");

// Check Tools property type on CreateResponseOptions
Console.WriteLine("\n=== CreateResponseOptions.Tools property ===");
var toolsProp = typeof(CreateResponseOptions).GetProperty("Tools");
Console.WriteLine($"  Type: {toolsProp?.PropertyType.FullName}");

// Verify CreateFunctionTool signature
Console.WriteLine("\n=== ResponseTool.CreateFunctionTool signature ===");
var cft = typeof(ResponseTool).GetMethod("CreateFunctionTool");
if (cft != null)
{
    foreach (var p in cft.GetParameters())
    {
        Console.WriteLine($"  {p.Position}: {p.ParameterType.Name} {p.Name} (optional={p.IsOptional}, default={p.DefaultValue})");
    }
}

// Check InputItems type
Console.WriteLine("\n=== CreateResponseOptions.InputItems ===");
var inputItemsProp = typeof(CreateResponseOptions).GetProperty("InputItems");
Console.WriteLine($"  Type: {inputItemsProp?.PropertyType.FullName}");

// Check if CreateResponseOptions has a constructor or Model setter
Console.WriteLine("\n=== CreateResponseOptions constructors ===");
foreach (var ctor in typeof(CreateResponseOptions).GetConstructors())
{
    var parms = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
    Console.WriteLine($"  CreateResponseOptions({parms})");
}
