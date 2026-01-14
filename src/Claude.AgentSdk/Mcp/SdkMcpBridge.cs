// Claude Agent SDK for .NET
// SDK MCP Server Bridge - enables in-process MCP servers via handler registration

using System.Text.Json;

namespace Claude.AgentSdk.Mcp;

/// <summary>
/// Delegate for listing tools available in an MCP server.
/// </summary>
public delegate Task<IReadOnlyList<McpToolDefinition>> ListToolsDelegate(CancellationToken ct);

/// <summary>
/// Delegate for calling a tool in an MCP server.
/// </summary>
public delegate Task<McpToolResult> CallToolDelegate(string name, JsonElement arguments, CancellationToken ct);

/// <summary>
/// Delegate for listing prompts available in an MCP server.
/// </summary>
public delegate Task<IReadOnlyList<McpPromptDefinition>> ListPromptsDelegate(CancellationToken ct);

/// <summary>
/// Delegate for getting a prompt from an MCP server.
/// </summary>
public delegate Task<McpPromptResult> GetPromptDelegate(string name, Dictionary<string, string>? arguments, CancellationToken ct);

/// <summary>
/// Delegate for listing resources available in an MCP server.
/// </summary>
public delegate Task<IReadOnlyList<McpResourceDefinition>> ListResourcesDelegate(CancellationToken ct);

/// <summary>
/// Delegate for reading a resource from an MCP server.
/// </summary>
public delegate Task<McpResourceResult> ReadResourceDelegate(string uri, CancellationToken ct);

/// <summary>
/// Handlers for an in-process MCP server.
/// </summary>
public class McpServerHandlers
{
    /// <summary>Handler for tools/list requests.</summary>
    public ListToolsDelegate? ListTools { get; init; }

    /// <summary>Handler for tools/call requests.</summary>
    public CallToolDelegate? CallTool { get; init; }

    /// <summary>Handler for prompts/list requests.</summary>
    public ListPromptsDelegate? ListPrompts { get; init; }

    /// <summary>Handler for prompts/get requests.</summary>
    public GetPromptDelegate? GetPrompt { get; init; }

    /// <summary>Handler for resources/list requests.</summary>
    public ListResourcesDelegate? ListResources { get; init; }

    /// <summary>Handler for resources/read requests.</summary>
    public ReadResourceDelegate? ReadResource { get; init; }
}

/// <summary>
/// Definition of an MCP tool.
/// </summary>
public class McpToolDefinition
{
    /// <summary>Tool name.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Tool description.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>JSON Schema for the tool's input parameters.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("inputSchema")]
    public JsonElement? InputSchema { get; init; }
}

/// <summary>
/// Result of an MCP tool call.
/// </summary>
public class McpToolResult
{
    /// <summary>Content blocks returned by the tool.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public required IReadOnlyList<McpContent> Content { get; init; }

    /// <summary>Whether the tool execution resulted in an error.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("isError")]
    public bool IsError { get; init; }
}

/// <summary>
/// MCP content block (text or other types).
/// </summary>
public class McpContent
{
    /// <summary>Content type (e.g., "text", "image").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Text content (for type="text").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Additional data (for other types).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}

/// <summary>
/// Definition of an MCP prompt.
/// </summary>
public class McpPromptDefinition
{
    /// <summary>Prompt name.</summary>
    public required string Name { get; init; }

    /// <summary>Prompt description.</summary>
    public string? Description { get; init; }

    /// <summary>Arguments the prompt accepts.</summary>
    public IReadOnlyList<McpPromptArgument>? Arguments { get; init; }
}

/// <summary>
/// Argument definition for an MCP prompt.
/// </summary>
public class McpPromptArgument
{
    /// <summary>Argument name.</summary>
    public required string Name { get; init; }

    /// <summary>Argument description.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the argument is required.</summary>
    public bool Required { get; init; }
}

/// <summary>
/// Result of getting an MCP prompt.
/// </summary>
public class McpPromptResult
{
    /// <summary>Description of the prompt.</summary>
    public string? Description { get; init; }

    /// <summary>Messages that make up the prompt.</summary>
    public required IReadOnlyList<McpPromptMessage> Messages { get; init; }
}

/// <summary>
/// A message in an MCP prompt.
/// </summary>
public class McpPromptMessage
{
    /// <summary>Role of the message (e.g., "user", "assistant").</summary>
    public required string Role { get; init; }

    /// <summary>Content of the message.</summary>
    public required McpContent Content { get; init; }
}

/// <summary>
/// Definition of an MCP resource.
/// </summary>
public class McpResourceDefinition
{
    /// <summary>Resource URI.</summary>
    public required string Uri { get; init; }

    /// <summary>Resource name.</summary>
    public required string Name { get; init; }

    /// <summary>Resource description.</summary>
    public string? Description { get; init; }

    /// <summary>MIME type of the resource.</summary>
    public string? MimeType { get; init; }
}

/// <summary>
/// Result of reading an MCP resource.
/// </summary>
public class McpResourceResult
{
    /// <summary>Contents of the resource.</summary>
    public required IReadOnlyList<McpResourceContent> Contents { get; init; }
}

/// <summary>
/// Content of an MCP resource.
/// </summary>
public class McpResourceContent
{
    /// <summary>Resource URI.</summary>
    public required string Uri { get; init; }

    /// <summary>MIME type.</summary>
    public string? MimeType { get; init; }

    /// <summary>Text content.</summary>
    public string? Text { get; init; }

    /// <summary>Binary content (base64 encoded).</summary>
    public string? Blob { get; init; }
}

/// <summary>
/// Bridge between the Claude Agent SDK control protocol and an in-process MCP server.
/// Provides handler-based routing for JSONRPC messages.
/// </summary>
internal class SdkMcpBridge : IAsyncDisposable
{
    private readonly McpServerHandlers _handlers;
    private readonly string _serverName;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Create a new SDK MCP bridge.
    /// </summary>
    /// <param name="handlers">The MCP server handlers.</param>
    /// <param name="serverName">Name for logging/diagnostics.</param>
    public SdkMcpBridge(McpServerHandlers handlers, string serverName)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));
    }

    /// <summary>
    /// Start the MCP bridge (no-op for handler-based implementation).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SdkMcpBridge));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a JSONRPC message to the MCP server and get the response.
    /// </summary>
    /// <param name="message">The JSONRPC request message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The JSONRPC response from the server.</returns>
    public async Task<JsonElement> SendMessageAsync(
        JsonElement message,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SdkMcpBridge));

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var method = message.TryGetProperty("method", out var m) ? m.GetString() : null;
            var id = message.TryGetProperty("id", out var idEl) ? idEl.Clone() : default;
            var paramsEl = message.TryGetProperty("params", out var p) ? p : default;

            object? result = null;
            string? error = null;

            try
            {
                // Handle notifications (no response needed)
                if (method?.StartsWith("notifications/") == true)
                {
                    // Notifications don't require a response, but return empty for consistency
                    return JsonSerializer.SerializeToElement(new
                    {
                        jsonrpc = "2.0",
                        id = id.ValueKind != JsonValueKind.Undefined ? (object?)id.Clone() : null,
                        result = new { }
                    });
                }

                result = method switch
                {
                    "initialize" => HandleInitialize(),
                    "tools/list" => await HandleToolsListAsync(cancellationToken),
                    "tools/call" => await HandleToolsCallAsync(paramsEl, cancellationToken),
                    "prompts/list" => await HandlePromptsListAsync(cancellationToken),
                    "prompts/get" => await HandlePromptsGetAsync(paramsEl, cancellationToken),
                    "resources/list" => await HandleResourcesListAsync(cancellationToken),
                    "resources/read" => await HandleResourcesReadAsync(paramsEl, cancellationToken),
                    _ => throw new NotSupportedException($"Method '{method}' is not supported")
                };
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // Build JSONRPC response
            if (error != null)
            {
                return JsonSerializer.SerializeToElement(new
                {
                    jsonrpc = "2.0",
                    id = id.ValueKind != JsonValueKind.Undefined ? (object?)id.Clone() : null,
                    error = new { code = -32603, message = error }
                });
            }

            return JsonSerializer.SerializeToElement(new
            {
                jsonrpc = "2.0",
                id = id.ValueKind != JsonValueKind.Undefined ? (object?)id.Clone() : null,
                result
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    private object HandleInitialize()
    {
        // Build capabilities dynamically - only include supported capabilities
        var capabilities = new Dictionary<string, object>();
        if (_handlers.ListTools != null)
            capabilities["tools"] = new { };
        if (_handlers.ListPrompts != null)
            capabilities["prompts"] = new { };
        if (_handlers.ListResources != null)
            capabilities["resources"] = new { };

        return new
        {
            protocolVersion = "2024-11-05",
            capabilities,
            serverInfo = new
            {
                name = _serverName,
                version = "1.0.0"
            }
        };
    }

    private async Task<object> HandleToolsListAsync(CancellationToken ct)
    {
        if (_handlers.ListTools == null)
            return new { tools = Array.Empty<object>() };

        var tools = await _handlers.ListTools(ct);
        return new { tools };
    }

    private async Task<object> HandleToolsCallAsync(JsonElement paramsEl, CancellationToken ct)
    {
        if (_handlers.CallTool == null)
            throw new NotSupportedException("Tool calls not supported by this server");

        var name = paramsEl.GetProperty("name").GetString()!;
        var arguments = paramsEl.TryGetProperty("arguments", out var args)
            ? args
            : JsonSerializer.SerializeToElement(new { });

        var result = await _handlers.CallTool(name, arguments, ct);
        return result;
    }

    private async Task<object> HandlePromptsListAsync(CancellationToken ct)
    {
        if (_handlers.ListPrompts == null)
            return new { prompts = Array.Empty<object>() };

        var prompts = await _handlers.ListPrompts(ct);
        return new { prompts };
    }

    private async Task<object> HandlePromptsGetAsync(JsonElement paramsEl, CancellationToken ct)
    {
        if (_handlers.GetPrompt == null)
            throw new NotSupportedException("Prompts not supported by this server");

        var name = paramsEl.GetProperty("name").GetString()!;
        var arguments = paramsEl.TryGetProperty("arguments", out var args)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(args.GetRawText())
            : null;

        var result = await _handlers.GetPrompt(name, arguments, ct);
        return result;
    }

    private async Task<object> HandleResourcesListAsync(CancellationToken ct)
    {
        if (_handlers.ListResources == null)
            return new { resources = Array.Empty<object>() };

        var resources = await _handlers.ListResources(ct);
        return new { resources };
    }

    private async Task<object> HandleResourcesReadAsync(JsonElement paramsEl, CancellationToken ct)
    {
        if (_handlers.ReadResource == null)
            throw new NotSupportedException("Resources not supported by this server");

        var uri = paramsEl.GetProperty("uri").GetString()!;
        var result = await _handlers.ReadResource(uri, ct);
        return result;
    }

    /// <summary>
    /// Clean up resources.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _lock.Dispose();

        return ValueTask.CompletedTask;
    }
}
