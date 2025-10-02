using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Dashboard
{
    /// <summary>
    /// Executes method calls using reflection (simplified - no runtime compilation)
    /// </summary>
    public static class CodeExecutor
    {
        public class ExecutionResult
        {
            public bool success;
            public string output;
            public string error;
            public object returnValue;
        }

        /// <summary>
        /// Execute a method call expression like "ClassName.MethodName(arg1, arg2)"
        /// Format: ClassName.MethodName(param1, param2, ...)
        /// Examples:
        ///   MurderController.Instance.TryPickNewVictimSite()
        ///   Player.Instance.SetHealth(100)
        /// </summary>
        public static ExecutionResult Execute(string code)
        {
            var result = new ExecutionResult { success = false };
            var output = new StringBuilder();

            try
            {
                // Parse simple method call expressions
                code = code.Trim();
                
                // Remove trailing semicolon if present
                if (code.EndsWith(";")) code = code.Substring(0, code.Length - 1);

                // Try to parse as: ClassName.MethodName() or ClassName.Instance.MethodName()
                var methodCallMatch = System.Text.RegularExpressions.Regex.Match(
                    code, 
                    @"^(\w+(?:\.\w+)*?)\.(\w+)\((.*?)\)$"
                );

                if (!methodCallMatch.Success)
                {
                    result.error = "Invalid format. Use: ClassName.MethodName(args) or ClassName.Instance.MethodName(args)\n\n" +
                        "Examples:\n" +
                        "  MurderController.Instance.TryPickNewVictimSite()\n" +
                        "  Player.Instance.SetHealth(100.0)\n" +
                        "  SessionData.Instance.PauseGame()\n\n" +
                        "Note: Full C# code execution requires additional setup.";
                    return result;
                }

                var classPath = methodCallMatch.Groups[1].Value; // e.g., "MurderController.Instance"
                var methodName = methodCallMatch.Groups[2].Value;
                var argsString = methodCallMatch.Groups[3].Value.Trim();

                // Parse arguments
                var args = new List<string>();
                if (!string.IsNullOrEmpty(argsString))
                {
                    // Simple comma split (doesn't handle nested parentheses)
                    args.AddRange(argsString.Split(',').Select(a => a.Trim()));
                }

                output.AppendLine($"Parsing: {classPath}.{methodName}({string.Join(", ", args)})");

                // Find the type and instance
                object targetInstance = null;
                Type targetType = null;

                // Split classPath (e.g., "MurderController.Instance" -> ["MurderController", "Instance"])
                var pathParts = classPath.Split('.');
                
                // Find the class type
                var className = pathParts[0];
                targetType = FindType(className);
                
                if (targetType == null)
                {
                    result.error = $"Class '{className}' not found in game assemblies.";
                    return result;
                }

                output.AppendLine($"Found type: {targetType.FullName}");

                // If there are more parts, traverse properties/fields
                targetInstance = null;
                for (int i = 1; i < pathParts.Length; i++)
                {
                    var propName = pathParts[i];
                    
                    // Try property
                    var prop = targetType.GetProperty(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (prop != null)
                    {
                        targetInstance = prop.GetValue(targetInstance);
                        targetType = prop.PropertyType;
                        output.AppendLine($"Accessed property: {propName} -> {targetType.Name}");
                        continue;
                    }

                    // Try field
                    var field = targetType.GetField(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (field != null)
                    {
                        targetInstance = field.GetValue(targetInstance);
                        targetType = field.FieldType;
                        output.AppendLine($"Accessed field: {propName} -> {targetType.Name}");
                        continue;
                    }

                    result.error = $"Property or field '{propName}' not found on type '{targetType.Name}'";
                    return result;
                }

                // Find and invoke the method
                var method = targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (method == null)
                {
                    result.error = $"Method '{methodName}' not found on type '{targetType.Name}'";
                    return result;
                }

                output.AppendLine($"Found method: {method.Name}");

                // Convert arguments
                var methodParams = method.GetParameters();
                var convertedArgs = new object[methodParams.Length];
                
                if (args.Count != methodParams.Length)
                {
                    result.error = $"Method requires {methodParams.Length} parameters but {args.Count} were provided.\n" +
                        $"Expected: {string.Join(", ", methodParams.Select(p => $"{p.ParameterType.Name} {p.Name}"))}";
                    return result;
                }

                for (int i = 0; i < methodParams.Length; i++)
                {
                    try
                    {
                        convertedArgs[i] = ConvertParameter(args[i], methodParams[i].ParameterType);
                        output.AppendLine($"Param {i}: {args[i]} -> {convertedArgs[i]} ({methodParams[i].ParameterType.Name})");
                    }
                    catch (Exception ex)
                    {
                        result.error = $"Failed to convert parameter {i} ('{args[i]}') to {methodParams[i].ParameterType.Name}: {ex.Message}";
                        return result;
                    }
                }

                // Execute on main thread
                object returnValue = null;
                try
                {
                    returnValue = Plugin.RunSync(() => method.Invoke(targetInstance, convertedArgs));
                }
                catch
                {
                    returnValue = method.Invoke(targetInstance, convertedArgs);
                }

                output.AppendLine($"\n✅ Execution successful!");
                if (returnValue != null)
                {
                    output.AppendLine($"Return value: {returnValue} ({returnValue.GetType().Name})");
                }
                else if (method.ReturnType == typeof(void))
                {
                    output.AppendLine($"Method completed (void return type)");
                }

                result.success = true;
                result.output = output.ToString();
                result.returnValue = returnValue;
            }
            catch (Exception ex)
            {
                result.error = $"Execution Error: {ex.Message}\n\n{output}\n\nStack Trace:\n{ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    result.error += $"\n\nInner Exception: {ex.InnerException.Message}";
                }
            }

            return result;
        }

        private static Type FindType(string className)
        {
            // Search in game assembly
            var gameAssembly = typeof(Game).Assembly;
            var type = gameAssembly.GetType(className);
            if (type != null) return type;

            // Search in all game assembly types
            type = gameAssembly.GetTypes().FirstOrDefault(t => t.Name == className);
            if (type != null) return type;

            // Search in Unity assemblies
            var unityTypes = typeof(UnityEngine.GameObject).Assembly.GetTypes();
            type = unityTypes.FirstOrDefault(t => t.Name == className);
            
            return type;
        }

        private static object ConvertParameter(string value, Type targetType)
        {
            value = value.Trim();
            
            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2);

            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(double)) return double.Parse(value);
            if (targetType == typeof(long)) return long.Parse(value);
            
            // Try generic conversion
            return Convert.ChangeType(value, targetType);
        }
    }
}
