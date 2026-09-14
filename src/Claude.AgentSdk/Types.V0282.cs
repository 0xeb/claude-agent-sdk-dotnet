// Claude Agent SDK for .NET — types added for parity with v0.2.82.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/types.py @ c352a50

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.AgentSdk;

#region Task Messages (T16)

/// <summary>
/// Usage statistics reported in task_progress and task_notification messages.
/// Python commit 9af27d7.
/// </summary>
public record TaskUsage(
    [property: JsonPropertyName("total_tokens")] int TotalTokens,
    [property: JsonPropertyName("tool_uses")] int ToolUses,
    [property: JsonPropertyName("duration_ms")] int DurationMs
);

/// <summary>
/// Possible status values for a task_notification message.
/// Python commit 9af27d7.
/// </summary>
public enum TaskNotificationStatus
{
    Completed,
    Failed,
    Stopped
}

/// <summary>
/// System message emitted when a task starts. Python commit 9af27d7.
/// </summary>
public record TaskStartedMessage : SystemMessage
{
    [JsonPropertyName("task_id")]
    public required string TaskId { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("task_type")]
    public string? TaskType { get; init; }
}

/// <summary>
/// System message emitted while a task is in progress. Python commit 9af27d7.
/// </summary>
public record TaskProgressMessage : SystemMessage
{
    [JsonPropertyName("task_id")]
    public required string TaskId { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("usage")]
    public required TaskUsage Usage { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("last_tool_name")]
    public string? LastToolName { get; init; }
}

/// <summary>
/// System message emitted when a task completes, fails, or is stopped.
/// Python commit 9af27d7.
/// </summary>
public record TaskNotificationMessage : SystemMessage
{
    [JsonPropertyName("task_id")]
    public required string TaskId { get; init; }

    [JsonPropertyName("status")]
    public required TaskNotificationStatus Status { get; init; }

    [JsonPropertyName("output_file")]
    public required string OutputFile { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("usage")]
    public TaskUsage? Usage { get; init; }
}

#endregion

#region MirrorErrorMessage (T17)

/// <summary>
/// System message emitted when a SessionStore.AppendAsync call fails. Non-fatal.
/// Python commit 6e3d54f.
/// </summary>
public record MirrorErrorMessage : SystemMessage
{
    [JsonPropertyName("key")]
    public SessionKey? Key { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

#endregion

#region Rate Limits (T18)

/// <summary>Rate limit status values.</summary>
public enum RateLimitStatus
{
    Allowed,
    AllowedWarning,
    Rejected
}

/// <summary>Rate limit window types.</summary>
public enum RateLimitType
{
    FiveHour,
    SevenDay,
    SevenDayOpus,
    SevenDaySonnet,
    Overage
}

internal static class RateLimitEnumHelpers
{
    public static string ToJsonString(this RateLimitStatus s) => s switch
    {
        RateLimitStatus.Allowed => "allowed",
        RateLimitStatus.AllowedWarning => "allowed_warning",
        RateLimitStatus.Rejected => "rejected",
        _ => s.ToString().ToLowerInvariant()
    };

    public static RateLimitStatus? ParseRateLimitStatus(string? value) => value switch
    {
        "allowed" => RateLimitStatus.Allowed,
        "allowed_warning" => RateLimitStatus.AllowedWarning,
        "rejected" => RateLimitStatus.Rejected,
        _ => null
    };

    public static string ToJsonString(this RateLimitType t) => t switch
    {
        RateLimitType.FiveHour => "five_hour",
        RateLimitType.SevenDay => "seven_day",
        RateLimitType.SevenDayOpus => "seven_day_opus",
        RateLimitType.SevenDaySonnet => "seven_day_sonnet",
        RateLimitType.Overage => "overage",
        _ => t.ToString().ToLowerInvariant()
    };
}

/// <summary>
/// Rate limit status emitted by the CLI when rate limit state changes.
/// Python commit 2d5c3cb.
/// </summary>
public record RateLimitInfo
{
    [JsonPropertyName("status")]
    public required RateLimitStatus Status { get; init; }

    [JsonPropertyName("resets_at")]
    public long? ResetsAt { get; init; }

    [JsonPropertyName("rate_limit_type")]
    public RateLimitType? RateLimitType { get; init; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; init; }

    [JsonPropertyName("overage_status")]
    public RateLimitStatus? OverageStatus { get; init; }

    [JsonPropertyName("overage_resets_at")]
    public long? OverageResetsAt { get; init; }

    [JsonPropertyName("overage_disabled_reason")]
    public string? OverageDisabledReason { get; init; }

    [JsonPropertyName("raw")]
    public JsonElement? Raw { get; init; }
}

/// <summary>
/// Rate limit event emitted when rate limit info changes. Python commit 2d5c3cb.
/// </summary>
public record RateLimitEvent : Message
{
    [JsonPropertyName("rate_limit_info")]
    public required RateLimitInfo RateLimitInfo { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}

#endregion

#region HookEventMessage (T19)

/// <summary>
/// Hook event emitted by the CLI when include_hook_events is enabled.
/// Python commit c1182a4.
/// </summary>
public record HookEventMessage : SystemMessage
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }
}

#endregion

#region MCP Status Types (T20)

/// <summary>Connection status values for an MCP server.</summary>
public enum McpServerConnectionStatus
{
    Connected,
    Failed,
    NeedsAuth,
    Pending,
    Disabled
}

internal static class McpEnumHelpers
{
    public static string ToJsonString(this McpServerConnectionStatus s) => s switch
    {
        McpServerConnectionStatus.Connected => "connected",
        McpServerConnectionStatus.Failed => "failed",
        McpServerConnectionStatus.NeedsAuth => "needs-auth",
        McpServerConnectionStatus.Pending => "pending",
        McpServerConnectionStatus.Disabled => "disabled",
        _ => s.ToString().ToLowerInvariant()
    };
}

/// <summary>Server info from MCP initialize handshake. Python commit 28f9b4b.
/// Named "Status" to disambiguate from <see cref="Claude.AgentSdk.Mcp"/>'s
/// in-process MCP server types.</summary>
public record McpStatusServerInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version
);

/// <summary>Tool annotations as returned in MCP server status. Python commit 28f9b4b.
/// Named "Status" to disambiguate from <see cref="Claude.AgentSdk.Mcp"/>'s
/// in-process MCP server <c>McpToolAnnotations</c>.</summary>
public record McpStatusToolAnnotations
{
    [JsonPropertyName("readOnly")]
    public bool? ReadOnly { get; init; }

    [JsonPropertyName("destructive")]
    public bool? Destructive { get; init; }

    [JsonPropertyName("openWorld")]
    public bool? OpenWorld { get; init; }
}

/// <summary>Information about a tool provided by an MCP server. Python commit 28f9b4b.
/// Named "Status" to disambiguate from <see cref="Claude.AgentSdk.Mcp"/>'s
/// in-process MCP server <c>McpToolDefinition</c>.</summary>
public record McpStatusToolInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("annotations")]
    public McpStatusToolAnnotations? Annotations { get; init; }
}

/// <summary>
/// SDK MCP server config as returned in status responses (no instance). Python commit 28f9b4b.
/// </summary>
public record McpSdkServerConfigStatus
{
    [JsonPropertyName("type")]
    public string Type => "sdk";

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// Claude.ai proxy MCP server config (output-only). Python commit 28f9b4b.
/// </summary>
public record McpClaudeAIProxyServerConfig
{
    [JsonPropertyName("type")]
    public string Type => "claudeai-proxy";

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

/// <summary>
/// Broader config type for status responses (includes claudeai-proxy).
/// In Python this is a union — modeled here as an opaque <see cref="JsonElement"/>.
/// Python commit 28f9b4b.
/// </summary>
public record McpServerStatusConfig
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Full raw config payload — opaque structural supertype.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; init; }
}

/// <summary>Status information for an MCP server connection. Python commit 28f9b4b.</summary>
public record McpServerStatus
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required McpServerConnectionStatus Status { get; init; }

    [JsonPropertyName("serverInfo")]
    public McpStatusServerInfo? ServerInfo { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("config")]
    public McpServerStatusConfig? Config { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<McpStatusToolInfo>? Tools { get; init; }
}

/// <summary>Response from ClaudeSDKClient.GetMcpStatus(). Python commit 28f9b4b.</summary>
public record McpStatusResponse
{
    [JsonPropertyName("mcpServers")]
    public required IReadOnlyList<McpServerStatus> McpServers { get; init; }
}

#endregion

#region Context Usage Types (T21)

/// <summary>A single context usage category. Python commit ac900bd.</summary>
public record ContextUsageCategory
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("tokens")]
    public required int Tokens { get; init; }

    [JsonPropertyName("color")]
    public required string Color { get; init; }

    [JsonPropertyName("isDeferred")]
    public bool? IsDeferred { get; init; }
}

/// <summary>Response from ClaudeSDKClient.GetContextUsage(). Python commit ac900bd.</summary>
public record ContextUsageResponse
{
    [JsonPropertyName("categories")]
    public required IReadOnlyList<ContextUsageCategory> Categories { get; init; }

    [JsonPropertyName("totalTokens")]
    public required int TotalTokens { get; init; }

    [JsonPropertyName("maxTokens")]
    public required int MaxTokens { get; init; }

    [JsonPropertyName("rawMaxTokens")]
    public required int RawMaxTokens { get; init; }

    [JsonPropertyName("percentage")]
    public required double Percentage { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("isAutoCompactEnabled")]
    public required bool IsAutoCompactEnabled { get; init; }

    [JsonPropertyName("memoryFiles")]
    public IReadOnlyList<JsonElement> MemoryFiles { get; init; } = [];

    [JsonPropertyName("mcpTools")]
    public IReadOnlyList<JsonElement> McpTools { get; init; } = [];

    [JsonPropertyName("agents")]
    public IReadOnlyList<JsonElement> Agents { get; init; } = [];

    [JsonPropertyName("gridRows")]
    public IReadOnlyList<IReadOnlyList<JsonElement>> GridRows { get; init; } = [];

    [JsonPropertyName("autoCompactThreshold")]
    public int? AutoCompactThreshold { get; init; }

    [JsonPropertyName("deferredBuiltinTools")]
    public IReadOnlyList<JsonElement>? DeferredBuiltinTools { get; init; }

    [JsonPropertyName("systemTools")]
    public IReadOnlyList<JsonElement>? SystemTools { get; init; }

    [JsonPropertyName("systemPromptSections")]
    public IReadOnlyList<JsonElement>? SystemPromptSections { get; init; }

    [JsonPropertyName("slashCommands")]
    public JsonElement? SlashCommands { get; init; }

    [JsonPropertyName("skills")]
    public JsonElement? Skills { get; init; }

    [JsonPropertyName("messageBreakdown")]
    public JsonElement? MessageBreakdown { get; init; }

    [JsonPropertyName("apiUsage")]
    public JsonElement? ApiUsage { get; init; }
}

#endregion

#region SDK Control Permission Request (T11)

/// <summary>
/// Control protocol request for can_use_tool. Python commit reference:
/// SDKControlPermissionRequest in types.py. PermissionSuggestions is tightened
/// to <see cref="PermissionUpdate"/> rather than a raw object list.
/// </summary>
public record SDKControlPermissionRequest
{
    [JsonPropertyName("subtype")]
    public string Subtype => "can_use_tool";

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("input")]
    public required JsonElement Input { get; init; }

    [JsonPropertyName("permission_suggestions")]
    public IReadOnlyList<PermissionUpdate>? PermissionSuggestions { get; init; }

    [JsonPropertyName("blocked_path")]
    public string? BlockedPath { get; init; }

    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }
}

#endregion

#region Typed Hook-Specific Outputs (T9)

/// <summary>
/// Hook-specific output for PreToolUse events. Includes "defer" decision.
/// Python commit f5a1b67.
/// </summary>
public record PreToolUseHookSpecificOutput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName => "PreToolUse";

    /// <summary>
    /// Permission decision: "allow", "deny", "ask", or "defer". Python commit f5a1b67.
    /// </summary>
    [JsonPropertyName("permissionDecision")]
    public string? PermissionDecision { get; init; }

    [JsonPropertyName("permissionDecisionReason")]
    public string? PermissionDecisionReason { get; init; }

    [JsonPropertyName("updatedInput")]
    public JsonElement? UpdatedInput { get; init; }

    [JsonPropertyName("additionalContext")]
    public string? AdditionalContext { get; init; }
}

/// <summary>
/// Hook-specific output for PostToolUse events. Python commit b0b652f.
/// </summary>
public record PostToolUseHookSpecificOutput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName => "PostToolUse";

    [JsonPropertyName("additionalContext")]
    public string? AdditionalContext { get; init; }

    /// <summary>Replaces the tool output before it is sent to the model. Python commit b0b652f.</summary>
    [JsonPropertyName("updatedToolOutput")]
    public JsonElement? UpdatedToolOutput { get; init; }

    /// <summary>Replaces the output for MCP tools only. Prefer UpdatedToolOutput. Python commit b0b652f.</summary>
    [JsonPropertyName("updatedMCPToolOutput")]
    public JsonElement? UpdatedMCPToolOutput { get; init; }
}

#endregion
