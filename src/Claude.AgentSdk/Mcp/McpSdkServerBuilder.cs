using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Claude.AgentSdk;

namespace Claude.AgentSdk.Mcp;

/// <summary>
/// Builds an in-process ("sdk") MCP server configuration with strongly-typed tool registration.
/// </summary>
public sealed class McpSdkServerBuilder
{
    private readonly string _serverName;
    private readonly Dictionary<string, ToolRegistration> _tools = new(StringComparer.Ordinal);

    internal McpSdkServerBuilder(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name must be non-empty.", nameof(serverName));
        _serverName = serverName;
    }

    /// <summary>
    /// Register a tool by delegate; input schema and argument binding are inferred from the delegate signature.
    /// </summary>
    /// <remarks>
    /// Supported signatures include:
    /// <list type="bullet">
    /// <item><description><c>(T1 a, T2 b, CancellationToken ct) => TResult</c></description></item>
    /// <item><description><c>(T1 a, T2 b) => Task&lt;TResult&gt;</c></description></item>
    /// <item><description><c>(TArgs args) => TResult</c> (single complex param binds from the whole JSON args object)</description></item>
    /// </list>
    /// </remarks>
    public McpSdkServerBuilder Tool(string name, Delegate handler, string? description = null, McpToolAnnotations? annotations = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name must be non-empty.", nameof(name));

        if (_tools.ContainsKey(name))
            throw new ArgumentException($"Tool '{name}' is already registered.", nameof(name));

        _tools[name] = ToolRegistration.Create(name, description, handler, annotations);
        return this;
    }

    internal McpSdkServerConfig Build()
    {
        var handlers = new McpServerHandlers
        {
            ListTools = ct => Task.FromResult<IReadOnlyList<McpToolDefinition>>(
                _tools.Values
                    .Select(t => t.Definition)
                    .ToList()
            ),
            CallTool = async (toolName, args, ct) =>
            {
                if (!_tools.TryGetValue(toolName, out var tool))
                    return McpToolResults.Text($"Unknown tool: '{toolName}'", isError: true);

                try
                {
                    return await tool.InvokeAsync(args, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return McpToolResults.Text(ex.Message, isError: true);
                }
            }
        };

        return new McpSdkServerConfig
        {
            Name = _serverName,
            Handlers = handlers
        };
    }

    private sealed class ToolRegistration
    {
        private static readonly JsonSerializerOptions ToolJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Delegate _handler;
        private readonly BindingPlan _bindingPlan;

        public McpToolDefinition Definition { get; }

        private ToolRegistration(string name, string? description, JsonElement inputSchema, Delegate handler, BindingPlan bindingPlan, McpToolAnnotations? annotations)
        {
            _handler = handler;
            _bindingPlan = bindingPlan;
            Definition = new McpToolDefinition
            {
                Name = name,
                Description = description,
                InputSchema = inputSchema,
                Annotations = annotations
            };
        }

        public static ToolRegistration Create(string name, string? description, Delegate handler, McpToolAnnotations? annotations = null)
        {
            var plan = BindingPlan.Create(handler.Method);
            var schema = plan.BuildInputSchema();
            return new ToolRegistration(name, description, schema, handler, plan, annotations);
        }

        public async Task<McpToolResult> InvokeAsync(JsonElement args, CancellationToken ct)
        {
            var invokeArgs = _bindingPlan.BindArguments(args, ct);
            var result = _handler.DynamicInvoke(invokeArgs);
            var value = await AwaitIfNeededAsync(result).ConfigureAwait(false);
            return ConvertToToolResult(value);
        }

        private static McpToolResult ConvertToToolResult(object? value)
        {
            switch (value)
            {
                case McpToolResult toolResult:
                    return toolResult;
                case McpContent content:
                    return new McpToolResult { Content = [content] };
                case IEnumerable<McpContent> contents:
                    return new McpToolResult { Content = contents.ToList() };
                case null:
                    return McpToolResults.Text("");
                case string s:
                    return McpToolResults.Text(s);
                case JsonElement je:
                    return McpToolResults.Text(je.GetRawText());
                default:
                    if (IsSimpleScalar(value.GetType()))
                        return McpToolResults.Text(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                    return McpToolResults.Text(JsonSerializer.Serialize(value, ToolJsonOptions));
            }
        }

        private static bool IsSimpleScalar(Type type)
        {
            if (type.IsEnum) return true;
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => true,
                TypeCode.Byte => true,
                TypeCode.SByte => true,
                TypeCode.Int16 => true,
                TypeCode.UInt16 => true,
                TypeCode.Int32 => true,
                TypeCode.UInt32 => true,
                TypeCode.Int64 => true,
                TypeCode.UInt64 => true,
                TypeCode.Single => true,
                TypeCode.Double => true,
                TypeCode.Decimal => true,
                TypeCode.String => true,
                _ => false
            };
        }

        private static async Task<object?> AwaitIfNeededAsync(object? result)
        {
            if (result is null) return null;

            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                var taskType = task.GetType();
                if (taskType.IsGenericType)
                    return taskType.GetProperty("Result")?.GetValue(task);
                return null;
            }

            var type = result.GetType();
            if (type.FullName is { } fullName && fullName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal))
            {
                var asTask = type.GetMethod("AsTask", BindingFlags.Public | BindingFlags.Instance);
                if (asTask != null && asTask.Invoke(result, null) is Task vtTask)
                {
                    await vtTask.ConfigureAwait(false);
                    var vtTaskType = vtTask.GetType();
                    if (vtTaskType.IsGenericType)
                        return vtTaskType.GetProperty("Result")?.GetValue(vtTask);
                    return null;
                }
            }

            return result;
        }

        private sealed class BindingPlan
        {
            private readonly List<BindingParameter> _parameters;
            private readonly bool _hasCancellationToken;
            private readonly bool _bindWholeObject;

            private BindingPlan(List<BindingParameter> parameters, bool hasCancellationToken, bool bindWholeObject)
            {
                _parameters = parameters;
                _hasCancellationToken = hasCancellationToken;
                _bindWholeObject = bindWholeObject;
            }

            public static BindingPlan Create(MethodInfo method)
            {
                var allParams = method.GetParameters();
                var hasCt = allParams.Length > 0 && allParams[^1].ParameterType == typeof(CancellationToken);
                var logicalParams = hasCt ? allParams[..^1] : allParams;

                var bindWhole = false;
                if (logicalParams.Length == 1 && IsComplexObject(logicalParams[0].ParameterType))
                    bindWhole = true;

                var parameters = logicalParams
                    .Select(p => new BindingParameter(p))
                    .ToList();

                return new BindingPlan(parameters, hasCt, bindWhole);
            }

            public JsonElement BuildInputSchema()
            {
                if (_bindWholeObject && _parameters.Count == 1)
                {
                    var schema = McpSchemaGenerator.GenerateForType(_parameters[0].ParameterType);
                    return JsonSerializer.SerializeToElement(schema, ToolJsonOptions);
                }

                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                var required = new List<string>();

                foreach (var p in _parameters)
                {
                    properties[p.JsonName] = McpSchemaGenerator.GenerateForType(p.ParameterType);
                    if (p.IsRequired)
                        required.Add(p.JsonName);
                }

                var root = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = properties
                };

                if (required.Count > 0)
                    root["required"] = required;

                return JsonSerializer.SerializeToElement(root, ToolJsonOptions);
            }

            public object?[] BindArguments(JsonElement args, CancellationToken ct)
            {
                var values = new object?[_parameters.Count + (_hasCancellationToken ? 1 : 0)];

                if (_bindWholeObject && _parameters.Count == 1)
                {
                    values[0] = Deserialize(args, _parameters[0].ParameterType);
                }
                else
                {
                    foreach (var (p, idx) in _parameters.Select((p, i) => (p, i)))
                    {
                        if (!TryGetProperty(args, p.JsonName, out var prop))
                        {
                            if (p.HasDefaultValue)
                            {
                                values[idx] = p.DefaultValue;
                                continue;
                            }

                            if (p.AllowsNull)
                            {
                                values[idx] = null;
                                continue;
                            }

                            throw new ArgumentException($"Missing required argument '{p.JsonName}'.");
                        }

                        values[idx] = ConvertElement(prop, p.ParameterType);
                    }
                }

                if (_hasCancellationToken)
                    values[^1] = ct;

                return values;
            }

            private static bool TryGetProperty(JsonElement args, string name, out JsonElement value)
            {
                if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value))
                    return true;

                if (args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in args.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            value = prop.Value;
                            return true;
                        }
                    }
                }

                value = default;
                return false;
            }

            private static object? ConvertElement(JsonElement element, Type targetType)
            {
                if (targetType == typeof(JsonElement))
                    return element.Clone();

                if (targetType == typeof(string))
                    return element.ValueKind == JsonValueKind.Null ? null : element.GetString();

                if (targetType == typeof(bool) || targetType == typeof(bool?))
                    return element.ValueKind == JsonValueKind.Null ? null : element.GetBoolean();

                if (targetType.IsEnum)
                {
                    if (element.ValueKind == JsonValueKind.String)
                        return Enum.Parse(targetType, element.GetString() ?? "", ignoreCase: true);
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var i))
                        return Enum.ToObject(targetType, i);
                }

                if (IsNumber(targetType))
                {
                    if (element.ValueKind == JsonValueKind.Null)
                        return null;

                    var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
                    if (underlying == typeof(decimal) && element.TryGetDecimal(out var dec))
                        return dec;

                    var isIntegral = IsIntegralNumber(targetType);
                    if (isIntegral)
                    {
                        if (element.TryGetInt64(out var l))
                            return Convert.ChangeType(l, Nullable.GetUnderlyingType(targetType) ?? targetType, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        if (element.TryGetDouble(out var d))
                            return Convert.ChangeType(d, Nullable.GetUnderlyingType(targetType) ?? targetType, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                return Deserialize(element, targetType);
            }

            private static object? Deserialize(JsonElement element, Type targetType)
                => JsonSerializer.Deserialize(element, targetType, ToolJsonOptions);

            private static bool IsNumber(Type t)
            {
                t = Nullable.GetUnderlyingType(t) ?? t;
                return Type.GetTypeCode(t) switch
                {
                    TypeCode.Byte => true,
                    TypeCode.SByte => true,
                    TypeCode.Int16 => true,
                    TypeCode.UInt16 => true,
                    TypeCode.Int32 => true,
                    TypeCode.UInt32 => true,
                    TypeCode.Int64 => true,
                    TypeCode.UInt64 => true,
                    TypeCode.Single => true,
                    TypeCode.Double => true,
                    TypeCode.Decimal => true,
                    _ => false
                };
            }

            private static bool IsIntegralNumber(Type t)
            {
                t = Nullable.GetUnderlyingType(t) ?? t;
                return Type.GetTypeCode(t) switch
                {
                    TypeCode.Byte => true,
                    TypeCode.SByte => true,
                    TypeCode.Int16 => true,
                    TypeCode.UInt16 => true,
                    TypeCode.Int32 => true,
                    TypeCode.UInt32 => true,
                    TypeCode.Int64 => true,
                    TypeCode.UInt64 => true,
                    _ => false
                };
            }

            private static bool IsComplexObject(Type t)
            {
                t = Nullable.GetUnderlyingType(t) ?? t;
                if (t == typeof(string)) return false;
                if (t == typeof(JsonElement)) return false;
                if (t.IsPrimitive) return false;
                if (t.IsEnum) return false;
                return Type.GetTypeCode(t) == TypeCode.Object;
            }
        }

        private sealed class BindingParameter
        {
            private static readonly NullabilityInfoContext Nullability = new();

            public string JsonName { get; }
            public Type ParameterType { get; }
            public bool HasDefaultValue { get; }
            public object? DefaultValue { get; }
            public bool AllowsNull { get; }
            public bool IsRequired { get; }

            public BindingParameter(ParameterInfo p)
            {
                ParameterType = p.ParameterType;
                var paramName = p.Name ?? throw new ArgumentException("Delegate parameters must have names.");
                JsonName = JsonNamingPolicy.CamelCase.ConvertName(paramName);

                HasDefaultValue = p.HasDefaultValue;
                DefaultValue = p.HasDefaultValue ? p.DefaultValue : null;

                var nullability = Nullability.Create(p);
                AllowsNull = nullability.WriteState == NullabilityState.Nullable ||
                             Nullable.GetUnderlyingType(ParameterType) != null;

                IsRequired = !HasDefaultValue && !AllowsNull;
            }
        }
    }

    private static class McpSchemaGenerator
    {
        private static readonly NullabilityInfoContext Nullability = new();

        public static object GenerateForType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(JsonElement) || type == typeof(object))
                return new Dictionary<string, object?>();

            if (type.IsEnum)
                return new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = Enum.GetNames(type)
                };

            if (type == typeof(string))
                return new Dictionary<string, object?> { ["type"] = "string" };

            if (type == typeof(bool))
                return new Dictionary<string, object?> { ["type"] = "boolean" };

            if (type == typeof(Guid))
                return new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" };

            if (type == typeof(Uri))
                return new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uri" };

            if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
                return new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time" };

            if (IsIntegral(type))
                return new Dictionary<string, object?> { ["type"] = "integer" };

            if (IsNumber(type))
                return new Dictionary<string, object?> { ["type"] = "number" };

            if (TryGetEnumerableElementType(type, out var elementType))
                return new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = GenerateForType(elementType)
                };

            if (TryGetStringDictionaryValueType(type, out var valueType))
                return new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = GenerateForType(valueType)
                };

            return GenerateObjectSchema(type);
        }

        private static object GenerateObjectSchema(Type type)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod != null && p.GetMethod.IsPublic)
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToArray();

            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            var required = new List<string>();

            foreach (var prop in props)
            {
                var jsonName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                               ?? JsonNamingPolicy.CamelCase.ConvertName(prop.Name);

                properties[jsonName] = GenerateForType(prop.PropertyType);

                var nullability = Nullability.Create(prop);
                var allowsNull = nullability.WriteState == NullabilityState.Nullable ||
                                 Nullable.GetUnderlyingType(prop.PropertyType) != null;

                var requiredByAttr = prop.GetCustomAttribute<RequiredAttribute>() != null;
                var requiredByNullability = !allowsNull;
                if (requiredByAttr || requiredByNullability)
                    required.Add(jsonName);
            }

            var schema = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
                schema["required"] = required;

            var typeDescription = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (!string.IsNullOrWhiteSpace(typeDescription))
                schema["description"] = typeDescription;

            return schema;
        }

        private static bool IsIntegral(Type t) => Type.GetTypeCode(t) switch
        {
            TypeCode.Byte => true,
            TypeCode.SByte => true,
            TypeCode.Int16 => true,
            TypeCode.UInt16 => true,
            TypeCode.Int32 => true,
            TypeCode.UInt32 => true,
            TypeCode.Int64 => true,
            TypeCode.UInt64 => true,
            _ => false
        };

        private static bool IsNumber(Type t) => Type.GetTypeCode(t) switch
        {
            TypeCode.Single => true,
            TypeCode.Double => true,
            TypeCode.Decimal => true,
            _ => false
        };

        private static bool TryGetEnumerableElementType(Type type, out Type elementType)
        {
            if (type == typeof(string))
            {
                elementType = typeof(void);
                return false;
            }

            if (type.IsArray)
            {
                elementType = type.GetElementType()!;
                return true;
            }

            var enumerable = type.GetInterfaces()
                .Concat([type])
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumerable != null)
            {
                elementType = enumerable.GetGenericArguments()[0];
                return true;
            }

            elementType = typeof(void);
            return false;
        }

        private static bool TryGetStringDictionaryValueType(Type type, out Type valueType)
        {
            var dict = type.GetInterfaces()
                .Concat([type])
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

            if (dict != null)
            {
                var args = dict.GetGenericArguments();
                if (args[0] == typeof(string))
                {
                    valueType = args[1];
                    return true;
                }
            }

            valueType = typeof(void);
            return false;
        }
    }
}
