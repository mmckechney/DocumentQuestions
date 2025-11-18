using Azure.AI.Agents.Persistent;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DocumentQuestions.Library
{
   public class LocalToolsUtility
   {
      public LocalToolsUtility()
      {
         _toolMethods = new Dictionary<string, MethodInfo>();
         _toolInstances = new Dictionary<string, object?>();
      }

      private readonly Dictionary<string, MethodInfo> _toolMethods;
      private readonly Dictionary<string, object?> _toolInstances;

      /// <summary>
      /// Executes a tool call by name using reflection
      /// </summary>
      public async Task<string> ExecuteToolCallAsync(string functionName, string argumentsJson)
      {
         var toolName = GetSanitizedToolName(functionName);

         if (!_toolMethods.TryGetValue(toolName, out var method))
         {
            return $"Unknown function: {functionName} (sanitized: {toolName})";
         }

         try
         {
            var arguments = ParseArgumentsForMethod(method, argumentsJson);
            var instance = _toolInstances[toolName];

            var result = method.Invoke(instance, arguments);

            // Handle async methods
            if (result is Task task)
            {
               await task;

               // Get result from Task<T>
               if (task.GetType().IsGenericType)
               {
                  var resultProperty = task.GetType().GetProperty("Result");
                  result = resultProperty?.GetValue(task);
               }
               else
               {
                  result = "Task completed successfully";
               }
            }

            return SerializeResult(result);
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error executing tool {functionName}: {ex.Message}");
            return $"Error executing {functionName}: {ex.Message}";
         }
      }


      /// <summary>
      /// Discovers all methods marked with [Description] attributes as potential tool functions
      /// </summary>
      public void RegisterLocalToolMethods(Object typeInstance)
      {
         var type = typeInstance.GetType();
         var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

         foreach (var method in methods)
         {
            var descAttr = method.GetCustomAttribute<DescriptionAttribute>();
            if (descAttr != null)
            {
               var toolName = GetSanitizedToolName(method.Name);
               RegisterToolMethod(toolName, method, method.IsStatic ? null : typeInstance);
            }
         }
      }

      /// <summary>
      /// Maps .NET types to JSON schema types
      /// </summary>
      private static string GetJsonTypeForParameter(Type paramType)
      {
         if (paramType == typeof(int) || paramType == typeof(long) || paramType == typeof(short))
            return "integer";
         if (paramType == typeof(double) || paramType == typeof(float) || paramType == typeof(decimal))
            return "number";
         if (paramType == typeof(bool))
            return "boolean";
         if (paramType.IsArray || (paramType.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(paramType.GetGenericTypeDefinition())))
            return "array";

         return "string";
      }
      /// <summary>
      /// Gets all discovered tool definitions
      /// </summary>
      public IEnumerable<FunctionToolDefinition> GetRegisterLocalToolDefinitions()
      {
         return _toolMethods.Select(kvp => CreateToolDefinitionFromMethod(kvp.Key, kvp.Value));
      }
      /// <summary>
      /// Creates a tool definition from a method using reflection
      /// </summary>
      public FunctionToolDefinition CreateToolDefinitionFromMethod(string toolName, MethodInfo method)
      {
         var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? $"Executes {method.Name}";

         var props = new Dictionary<string, object>();
         var required = new List<string>();

         foreach (var param in method.GetParameters())
         {
            // Skip CancellationToken parameters
            if (param.ParameterType == typeof(CancellationToken))
               continue;

            var paramDesc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? param.Name!;
            var paramType = GetJsonTypeForParameter(param.ParameterType);

            props[param.Name!] = new { type = paramType, description = paramDesc };

            if (!param.IsOptional && param.ParameterType != typeof(CancellationToken))
               required.Add(param.Name!);
         }

         // Create schema object conditionally - only include 'required' if there are required parameters
         object schema;
         if (required.Count > 0)
         {
            schema = new
            {
               type = "object",
               properties = props,
               required = required.ToArray()
            };
         }
         else
         {
            schema = new
            {
               type = "object",
               properties = props
            };
         }

         return new FunctionToolDefinition(
             name: toolName,
             description: description,
             parameters: BinaryData.FromObjectAsJson(schema, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
         );
      }

      /// <summary>
      /// Parses JSON arguments and maps them to method parameters
      /// </summary>
      private object[] ParseArgumentsForMethod(MethodInfo method, string argumentsJson)
      {
         var parameters = method.GetParameters();
         var arguments = new object[parameters.Length];

         if (string.IsNullOrWhiteSpace(argumentsJson))
         {
            // Fill with default values for optional parameters
            for (int i = 0; i < parameters.Length; i++)
            {
               arguments[i] = parameters[i].ParameterType == typeof(CancellationToken)
                   ? CancellationToken.None
                   : parameters[i].DefaultValue ?? GetDefaultValue(parameters[i].ParameterType);
            }
            return arguments;
         }

         using var doc = JsonDocument.Parse(argumentsJson);
         var root = doc.RootElement;

         for (int i = 0; i < parameters.Length; i++)
         {
            var param = parameters[i];

            if (param.ParameterType == typeof(CancellationToken))
            {
               arguments[i] = CancellationToken.None;
               continue;
            }

            if (root.TryGetProperty(param.Name!, out var jsonValue))
            {
               arguments[i] = ConvertJsonValueToType(jsonValue, param.ParameterType);
            }
            else if (param.IsOptional)
            {
               arguments[i] = param.DefaultValue ?? GetDefaultValue(param.ParameterType);
            }
            else
            {
               throw new ArgumentException($"Missing required parameter: {param.Name}");
            }
         }

         return arguments;
      }

      /// <summary>
      /// Converts JsonElement to the specified type
      /// </summary>
      private object? ConvertJsonValueToType(JsonElement jsonValue, Type targetType)
      {
         if (targetType == typeof(string))
            return jsonValue.GetString();
         if (targetType == typeof(int))
            return jsonValue.GetInt32();
         if (targetType == typeof(long))
            return jsonValue.GetInt64();
         if (targetType == typeof(bool))
            return jsonValue.GetBoolean();
         if (targetType == typeof(double))
            return jsonValue.GetDouble();
         if (targetType == typeof(decimal))
            return jsonValue.GetDecimal();

         // For complex types, try JSON deserialization
         try
         {
            return JsonSerializer.Deserialize(jsonValue.GetRawText(), targetType);
         }
         catch
         {
            return jsonValue.GetString(); // Fallback to string
         }
      }

      /// <summary>
      /// Gets default value for a type
      /// </summary>
      private static object? GetDefaultValue(Type type)
      {
         return type.IsValueType ? Activator.CreateInstance(type) : null;
      }

      /// <summary>
      /// Serializes the result to JSON string format
      /// </summary>
      private static string SerializeResult(object? result)
      {
         if (result == null)
            return "null";

         if (result is string str)
            return str;

         try
         {
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
               PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
               WriteIndented = false
            });
         }
         catch
         {
            return result.ToString() ?? "null";
         }
      }


      /// <summary>
      /// Sanitizes method names to comply with tool naming requirements
      /// </summary>
      private static string GetSanitizedToolName(string methodName)
      {
         // Remove angle brackets and other invalid characters from lambda/anonymous method names
         var sanitized = Regex.Replace(methodName, "[^a-zA-Z0-9_-]", "_");
         sanitized = Regex.Replace(sanitized, "_+", "_").Trim('_');

         return string.IsNullOrWhiteSpace(sanitized) ? "tool" : sanitized.ToLowerInvariant();
      }
      /// <summary>
      /// Registers an external tool method with an instance
      /// </summary>
      private void RegisterToolMethod(string toolName, MethodInfo method, object? instance = null)
      {
         var sanitizedName = GetSanitizedToolName(toolName);
         _toolMethods[sanitizedName] = method;
         _toolInstances[sanitizedName] = instance;

         Console.WriteLine($"Registered external tool: {sanitizedName} -> {method.DeclaringType?.Name}.{method.Name}");
      }

      /// <summary>
      /// Registers multiple tool methods from a delegate
      /// </summary>
      public void RegisterToolMethod(string toolName, Delegate toolDelegate)
      {
         RegisterToolMethod(toolName, toolDelegate.Method, toolDelegate.Target);
      }
      public FunctionToolDefinition CreateToolDefinitionFromMethod(Delegate method)
      {
         var toolName = GetSanitizedToolName(method.Method.Name);
         RegisterToolMethod(toolName, method.Method, method.Target);
         return CreateToolDefinitionFromMethod(toolName, method.Method);
      }


      /// <summary>
      /// Processes a tool call by executing the handler function
      /// </summary>
      public string ProcessToolCall(string functionName, string arguments, Func<string, string, string> toolHandler)
      {
         using var doc = JsonDocument.Parse(arguments);

         // Extract parameters based on function name
         switch (functionName)
         {
            case "get_weather":
               var city = doc.RootElement.GetProperty("city").GetString() ?? "Unknown";
               var units = doc.RootElement.TryGetProperty("units", out var u) ? u.GetString() : "imperial";
               return toolHandler(city, units ?? "imperial");
            default:
               return $"Unknown function: {functionName}";
         }
      }

      //public FunctionToolDefinition CreateToolDefinitionFromMethod(Delegate method)
      //{
      //   var mi = method.Method;
      //   var rawName = mi.Name;
      //   var methodDesc = mi.GetCustomAttribute<DescriptionAttribute>()?.Description
      //                    ?? $"Invoke {rawName}";

      //   // Sanitize name to comply with pattern ^[a-zA-Z0-9_-]+$
      //   // Lambdas / local functions often have chars like '<', '>', '|', etc.
      //   var sanitized = Regex.Replace(rawName, "[^a-zA-Z0-9_-]", "_");
      //   // Collapse multiple underscores
      //   sanitized = Regex.Replace(sanitized, "_+", "_");
      //   // Avoid leading underscore only name by providing a fallback
      //   if (string.IsNullOrWhiteSpace(sanitized))
      //   {
      //      sanitized = "tool";
      //   }

      //   // Build a minimal JSON schema for parameters
      //   var props = new Dictionary<string, object?>();
      //   var required = new List<string>();

      //   foreach (var p in mi.GetParameters())
      //   {
      //      var pDesc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? p.Name!;
      //      var type = p.ParameterType == typeof(int) ? "integer"
      //               : p.ParameterType == typeof(double) ? "number"
      //               : p.ParameterType == typeof(bool) ? "boolean"
      //               : "string"; // simple map; extend as needed

      //      props[p.Name!] = new { type, description = pDesc };
      //      if (!p.IsOptional) required.Add(p.Name!);
      //   }

      //   var schema = new
      //   {
      //      type = "object",
      //      properties = props,
      //      required = required.Count > 0 ? required : null
      //   };

      //   var tool = new FunctionToolDefinition(
      //       name: sanitized,
      //       description: methodDesc,
      //       parameters: BinaryData.FromObjectAsJson(schema,
      //           new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
      //   );
      //   return tool;
      //}
   }
}
