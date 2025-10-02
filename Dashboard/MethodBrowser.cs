using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Dashboard
{
    /// <summary>
    /// Provides reflection-based method search and invocation for the game
    /// </summary>
    public static class MethodBrowser
    {
        public class MethodInfo
        {
            public string className;
            public string methodName;
            public string fullName;
            public string returnType;
            public bool isStatic;
            public bool isPublic;
            public List<ParamInfo> parameters;
            public string declaringAssembly;
        }

        public class ParamInfo
        {
            public string name;
            public string type;
            public bool hasDefaultValue;
            public string defaultValue;
        }

        public class InvokeResult
        {
            public bool success;
            public string message;
            public string returnValue;
            public string error;
        }

        private static List<Type> _cachedTypes = null;
        private static readonly object _lock = new object();

        /// <summary>
        /// Get all game types (cached)
        /// </summary>
        private static List<Type> GetGameTypes()
        {
            lock (_lock)
            {
                if (_cachedTypes == null)
                {
                    _cachedTypes = new List<Type>();
                    try
                    {
                        // Scan the main game assembly
                        var gameAssembly = typeof(Game).Assembly;
                        _cachedTypes.AddRange(gameAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract));
                        ModLogger.Info($"MethodBrowser: Cached {_cachedTypes.Count} types from game assembly");
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"MethodBrowser: Failed to cache types: {ex.Message}");
                    }
                }
                return _cachedTypes;
            }
        }

        /// <summary>
        /// Search for methods by name (case-insensitive partial match)
        /// </summary>
        public static List<MethodInfo> SearchMethods(string query, int maxResults = 50)
        {
            var results = new List<MethodInfo>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            var queryLower = query.Trim().ToLowerInvariant();

            try
            {
                var types = GetGameTypes();
                foreach (var type in types)
                {
                    try
                    {
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        
                        foreach (var method in methods)
                        {
                            if (method.Name.ToLowerInvariant().Contains(queryLower))
                            {
                                results.Add(new MethodInfo
                                {
                                    className = type.Name,
                                    methodName = method.Name,
                                    fullName = $"{type.Name}.{method.Name}",
                                    returnType = method.ReturnType.Name,
                                    isStatic = method.IsStatic,
                                    isPublic = method.IsPublic,
                                    parameters = method.GetParameters().Select(p => new ParamInfo
                                    {
                                        name = p.Name,
                                        type = p.ParameterType.Name,
                                        hasDefaultValue = p.HasDefaultValue,
                                        defaultValue = p.HasDefaultValue ? (p.DefaultValue?.ToString() ?? "null") : null
                                    }).ToList(),
                                    declaringAssembly = type.Assembly.GetName().Name
                                });

                                if (results.Count >= maxResults) return results;
                            }
                        }
                    }
                    catch { /* Skip problematic types */ }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MethodBrowser.SearchMethods error: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Invoke a method by class and method name
        /// </summary>
        public static InvokeResult InvokeMethod(string className, string methodName, Dictionary<string, string> parameters)
        {
            try
            {
                var types = GetGameTypes();
                var type = types.FirstOrDefault(t => t.Name.Equals(className, StringComparison.OrdinalIgnoreCase));
                
                if (type == null)
                {
                    return new InvokeResult
                    {
                        success = false,
                        error = $"Class '{className}' not found"
                    };
                }

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (methods.Count == 0)
                {
                    return new InvokeResult
                    {
                        success = false,
                        error = $"Method '{methodName}' not found in class '{className}'"
                    };
                }

                // Try to find a matching overload
                System.Reflection.MethodInfo targetMethod = null;
                object[] args = null;

                foreach (var method in methods)
                {
                    var methodParams = method.GetParameters();
                    
                    // If no parameters required and none provided
                    if (methodParams.Length == 0 && (parameters == null || parameters.Count == 0))
                    {
                        targetMethod = method;
                        args = new object[0];
                        break;
                    }

                    // Try to match and convert parameters (by index: p0, p1, p2...)
                    if (parameters != null && methodParams.Length == parameters.Count)
                    {
                        try
                        {
                            args = new object[methodParams.Length];
                            bool allMatch = true;

                            for (int i = 0; i < methodParams.Length; i++)
                            {
                                var param = methodParams[i];
                                // Try by index first (p0, p1, p2...)
                                if (parameters.TryGetValue($"p{i}", out string value))
                                {
                                    args[i] = ConvertParameter(value, param.ParameterType);
                                }
                                // Try by parameter name
                                else if (parameters.TryGetValue(param.Name, out value))
                                {
                                    args[i] = ConvertParameter(value, param.ParameterType);
                                }
                                else
                                {
                                    allMatch = false;
                                    break;
                                }
                            }

                            if (allMatch)
                            {
                                targetMethod = method;
                                break;
                            }
                        }
                        catch { continue; }
                    }
                }

                if (targetMethod == null)
                {
                    return new InvokeResult
                    {
                        success = false,
                        error = $"No matching overload found. Available: {string.Join(", ", methods.Select(m => m.ToString()))}"
                    };
                }

                // Get instance if needed
                object instance = null;
                if (!targetMethod.IsStatic)
                {
                    // Try to find singleton instance
                    var instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProp != null)
                    {
                        instance = instanceProp.GetValue(null);
                    }
                    else
                    {
                        return new InvokeResult
                        {
                            success = false,
                            error = $"Method is not static and no 'Instance' property found on '{className}'"
                        };
                    }
                }

                // Invoke the method
                object result = targetMethod.Invoke(instance, args);

                return new InvokeResult
                {
                    success = true,
                    message = $"Successfully invoked {className}.{methodName}",
                    returnValue = result != null ? result.ToString() : "void/null"
                };
            }
            catch (Exception ex)
            {
                return new InvokeResult
                {
                    success = false,
                    error = $"Invocation error: {ex.Message}\n{ex.StackTrace}"
                };
            }
        }

        /// <summary>
        /// Convert string parameter to the target type
        /// </summary>
        private static object ConvertParameter(string value, Type targetType)
        {
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
