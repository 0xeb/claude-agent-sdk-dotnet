namespace Claude.AgentSdk.Mcp;

/// <summary>
/// Convenience helpers for producing MCP tool results.
/// </summary>
public static class McpToolResults
{
    public static McpToolResult Text(string text, bool isError = false) => new()
    {
        IsError = isError,
        Content = [new McpContent { Type = "text", Text = text }]
    };
}

