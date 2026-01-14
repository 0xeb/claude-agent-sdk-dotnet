using Claude.AgentSdk;

namespace Claude.AgentSdk.Mcp;

/// <summary>
/// A dictionary of MCP server configs suitable for <see cref="ClaudeAgentOptions.McpServers"/>.
/// </summary>
public sealed class McpServerRegistry : Dictionary<string, object>
{
    /// <summary>
    /// Add an in-process ("sdk") MCP server.
    /// </summary>
    public McpServerRegistry AddSdk(string serverName, Action<McpSdkServerBuilder> configure)
    {
        var builder = new McpSdkServerBuilder(serverName);
        configure(builder);
        this[serverName] = builder.Build();
        return this;
    }
}

