using Claude.AgentSdk;

namespace Claude.AgentSdk.Mcp;

/// <summary>
/// Helpers for building MCP server configurations.
/// </summary>
public static class McpServers
{
    /// <summary>
    /// Create an empty MCP server registry.
    /// </summary>
    public static McpServerRegistry Create() => new();

    /// <summary>
    /// Create a registry with a single in-process ("sdk") MCP server.
    /// </summary>
    public static McpServerRegistry Sdk(string serverName, Action<McpSdkServerBuilder> configure)
        => new McpServerRegistry().AddSdk(serverName, configure);
}

