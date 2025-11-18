# Generic Tool Calling System

This document explains how to use the new generic, reflection-based tool calling system in the `LocalFunctionTools` class.

## Overview

The new system automatically discovers methods marked with `[Description]` attributes and makes them available as AI agent tools. This eliminates the need for hardcoded `if` statements and manual tool name matching.

## Features

- **Automatic Discovery**: Methods with `[Description]` attributes are automatically discovered as tools
- **Reflection-based Execution**: Tools are executed using reflection, no hardcoding required
- **Type Safety**: Automatic parameter parsing and type conversion
- **Async Support**: Handles both synchronous and asynchronous tool methods
- **External Tool Registration**: Can register methods from other classes
- **Multiple Tools**: Create agents with multiple tools automatically

## How It Works

### 1. Tool Method Declaration

Mark any method with a `[Description]` attribute to make it discoverable:

```csharp
[Description("Fetches the weather information for the specified location")]
private static string FetchWeather([Description("The location to fetch weather for.")] string location)
{
    // Implementation
    return JsonSerializer.Serialize(result);
}

[Description("Calculates mathematical operations")]
private static string Calculator(
    [Description("First number")] double a, 
    [Description("Second number")] double b, 
    [Description("Operation: add, subtract, multiply, divide")] string operation = "add")
{
    // Implementation
    return JsonSerializer.Serialize(result);
}
```

### 2. Automatic Tool Discovery

The system automatically discovers these methods during initialization:

```csharp
var tools = new LocalFunctionTools(projectEndpoint);
// Automatically discovers all [Description] marked methods
```

### 3. Agent Creation with All Tools

Create an agent that can use all discovered tools:

```csharp
var agent = await tools.CreateAgentWithAllToolsAsync(
    model: "gpt-4o-mini",
    name: "MultiToolAgent",
    instructions: "You are a helpful assistant with access to various tools."
);
```

### 4. Generic Tool Execution

The system handles all tool calls generically without hardcoded logic:

```csharp
// This replaces all the hardcoded if-statements
foreach (RequiredToolCall toolCall in submitToolOutputsAction.ToolCalls)
{
    if (toolCall is RequiredFunctionToolCall functionToolCall)
    {
        try
        {
            // Generic execution - works for any discovered tool
            string toolResult = await ExecuteToolCallAsync(functionToolCall.Name, functionToolCall.Arguments ?? "{}");
            toolOutputs.Add(new ToolOutput(toolCall, toolResult));
        }
        catch (Exception ex)
        {
            string errorResult = $"Error: {ex.Message}";
            toolOutputs.Add(new ToolOutput(toolCall, errorResult));
        }
    }
}
```

## Usage Examples

### Basic Usage

```csharp
// Set up environment
Environment.SetEnvironmentVariable("AIFOUNDRY_ENDPOINT", "https://your-project.region.models.ai.azure.com");

// Create tools instance (automatically discovers methods)
var tools = new LocalFunctionTools(Environment.GetEnvironmentVariable("AIFOUNDRY_ENDPOINT"));

// Test with multiple tool calls
var response = await tools.QuickTestAllToolsAsync("gpt-4o-mini", 
    "What's the weather in Seattle? Also calculate 15 + 27.");
```

### Registering External Tools

```csharp
// Register a method from another class
tools.RegisterToolMethod("document_search", searchMethod, searchInstance);

// Register a lambda function
tools.RegisterToolMethod("string_length", 
    (string input) => $"The string '{input}' has {input.Length} characters.");
```

### Custom Agent with Specific Tools

```csharp
// Get specific tool definitions
var weatherTool = tools.CreateWeatherToolDefinition();
var calculatorTool = tools.CreateToolDefinitionFromMethod("calculator", calculatorMethod);

// Create agent with specific tools
var agent = await tools.CreateAgentWithMultipleToolsAsync(
    model: "gpt-4o-mini",
    name: "SpecializedAgent",
    instructions: "You specialize in weather and math.",
    weatherTool, calculatorTool
);
```

## Benefits

1. **No Hardcoding**: No more `if (toolName == "specific_tool")` statements
2. **Maintainable**: Adding new tools just requires adding `[Description]` attribute
3. **Type Safe**: Automatic parameter parsing and validation
4. **Reusable**: Works with any method signature
5. **Extensible**: Easy to register external tools from other classes
6. **Error Handling**: Automatic error catching and reporting

## Tool Method Requirements

1. Must be marked with `[Description]` attribute
2. Parameters should have `[Description]` attributes for better documentation
3. Return type should be `string` or JSON-serializable object
4. Async methods (`Task<T>`) are supported
5. `CancellationToken` parameters are automatically handled

## Migration from Old System

### Before (Hardcoded)
```csharp
if (functionToolCall.Name.ToLower() == "fetchweather")
{
    // Parse arguments manually
    string location = "New York";
    if (!string.IsNullOrEmpty(functionToolCall.Arguments))
    {
        // Manual JSON parsing...
    }
    string weatherResult = FetchWeather(location);
    toolOutputs.Add(new ToolOutput(toolCall, weatherResult));
}
```

### After (Generic)
```csharp
// Single line handles any tool
string toolResult = await ExecuteToolCallAsync(functionToolCall.Name, functionToolCall.Arguments ?? "{}");
toolOutputs.Add(new ToolOutput(toolCall, toolResult));
```

This system scales automatically as you add more tools, requiring no changes to the execution logic.