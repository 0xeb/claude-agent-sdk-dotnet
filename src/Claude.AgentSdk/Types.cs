// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/types.py

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.AgentSdk;

#region Enums

/// <summary>
/// Permission modes for controlling tool execution.
/// </summary>
public enum PermissionMode
{
    Default,
    AcceptEdits,
    Plan,
    BypassPermissions
}

/// <summary>
/// Hook event types.
/// </summary>
public enum HookEvent
{
    PreToolUse,
    PostToolUse,
    PostToolUseFailure,
    UserPromptSubmit,
    Stop,
    SubagentStop,
    PreCompact,
    Notification,
    SubagentStart,
    PermissionRequest
}

/// <summary>
/// Effort level for thinking depth.
/// </summary>
public enum EffortLevel
{
    Low,
    Medium,
    High,
    Max
}

/// <summary>
/// Setting sources to load.
/// </summary>
public enum SettingSource
{
    User,
    Project,
    Local
}

/// <summary>
/// Permission behavior options.
/// </summary>
public enum PermissionBehavior
{
    Allow,
    Deny,
    Ask
}

/// <summary>
/// Permission update destinations.
/// </summary>
public enum PermissionUpdateDestination
{
    UserSettings,
    ProjectSettings,
    LocalSettings,
    Session
}

/// <summary>
/// Permission update types.
/// </summary>
public enum PermissionUpdateType
{
    AddRules,
    ReplaceRules,
    RemoveRules,
    SetMode,
    AddDirectories,
    RemoveDirectories
}

/// <summary>
/// Assistant message error types.
/// </summary>
public enum AssistantMessageError
{
    AuthenticationFailed,
    BillingError,
    RateLimit,
    InvalidRequest,
    ServerError,
    Unknown
}

/// <summary>
/// Helper methods for enum string conversion.
/// </summary>
internal static class EnumHelpers
{
    public static string ToJsonString(this PermissionMode mode) => mode switch
    {
        PermissionMode.Default => "default",
        PermissionMode.AcceptEdits => "acceptEdits",
        PermissionMode.Plan => "plan",
        PermissionMode.BypassPermissions => "bypassPermissions",
        _ => mode.ToString().ToLowerInvariant()
    };

    public static string ToJsonString(this SettingSource source) => source switch
    {
        SettingSource.User => "user",
        SettingSource.Project => "project",
        SettingSource.Local => "local",
        _ => source.ToString().ToLowerInvariant()
    };

    public static string ToJsonString(this PermissionBehavior behavior) => behavior switch
    {
        PermissionBehavior.Allow => "allow",
        PermissionBehavior.Deny => "deny",
        PermissionBehavior.Ask => "ask",
        _ => behavior.ToString().ToLowerInvariant()
    };

    public static string ToJsonString(this PermissionUpdateDestination dest) => dest switch
    {
        PermissionUpdateDestination.UserSettings => "userSettings",
        PermissionUpdateDestination.ProjectSettings => "projectSettings",
        PermissionUpdateDestination.LocalSettings => "localSettings",
        PermissionUpdateDestination.Session => "session",
        _ => dest.ToString()
    };

    public static string ToJsonString(this PermissionUpdateType type) => type switch
    {
        PermissionUpdateType.AddRules => "addRules",
        PermissionUpdateType.ReplaceRules => "replaceRules",
        PermissionUpdateType.RemoveRules => "removeRules",
        PermissionUpdateType.SetMode => "setMode",
        PermissionUpdateType.AddDirectories => "addDirectories",
        PermissionUpdateType.RemoveDirectories => "removeDirectories",
        _ => type.ToString()
    };

    public static string ToJsonString(this EffortLevel effort) => effort switch
    {
        EffortLevel.Low => "low",
        EffortLevel.Medium => "medium",
        EffortLevel.High => "high",
        EffortLevel.Max => "max",
        _ => effort.ToString().ToLowerInvariant()
    };

    public static string ToJsonString(this HookEvent hookEvent) => hookEvent switch
    {
        HookEvent.PreToolUse => "PreToolUse",
        HookEvent.PostToolUse => "PostToolUse",
        HookEvent.PostToolUseFailure => "PostToolUseFailure",
        HookEvent.UserPromptSubmit => "UserPromptSubmit",
        HookEvent.Stop => "Stop",
        HookEvent.SubagentStop => "SubagentStop",
        HookEvent.PreCompact => "PreCompact",
        HookEvent.Notification => "Notification",
        HookEvent.SubagentStart => "SubagentStart",
        HookEvent.PermissionRequest => "PermissionRequest",
        _ => hookEvent.ToString()
    };
}

#endregion

#region Content Blocks

/// <summary>
/// Base class for content blocks.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ThinkingBlock), "thinking")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
public abstract record ContentBlock;

/// <summary>
/// Text content block.
/// </summary>
public record TextBlock(
    [property: JsonPropertyName("text")] string Text
) : ContentBlock;

/// <summary>
/// Thinking content block.
/// </summary>
public record ThinkingBlock(
    [property: JsonPropertyName("thinking")] string Thinking,
    [property: JsonPropertyName("signature")] string Signature
) : ContentBlock;

/// <summary>
/// Tool use content block.
/// </summary>
public record ToolUseBlock(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("input")] JsonElement Input
) : ContentBlock;

/// <summary>
/// Tool result content block.
/// </summary>
public record ToolResultBlock(
    [property: JsonPropertyName("tool_use_id")] string ToolUseId,
    [property: JsonPropertyName("content")] JsonElement? Content = null,
    [property: JsonPropertyName("is_error")] bool? IsError = null
) : ContentBlock;

#endregion

#region Messages

/// <summary>
/// Base class for messages.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserMessage), "user")]
[JsonDerivedType(typeof(AssistantMessage), "assistant")]
[JsonDerivedType(typeof(SystemMessage), "system")]
[JsonDerivedType(typeof(ResultMessage), "result")]
[JsonDerivedType(typeof(StreamEvent), "stream_event")]
public abstract record Message;

/// <summary>
/// User message.
/// </summary>
public record UserMessage : Message
{
    [JsonPropertyName("content")]
    public required JsonElement Content { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }

    /// <summary>
    /// Gets the content as a string if it's a simple text message.
    /// </summary>
    public string? GetTextContent()
    {
        if (Content.ValueKind == JsonValueKind.String)
            return Content.GetString();
        return null;
    }

    /// <summary>
    /// Gets the content blocks if the content is an array.
    /// </summary>
    public IReadOnlyList<ContentBlock>? GetContentBlocks()
    {
        if (Content.ValueKind == JsonValueKind.Array)
        {
            var blocks = new List<ContentBlock>();
            foreach (var element in Content.EnumerateArray())
            {
                var block = JsonSerializer.Deserialize<ContentBlock>(element, ClaudeJsonContext.Default.ContentBlock);
                if (block != null)
                    blocks.Add(block);
            }
            return blocks;
        }
        return null;
    }
}

/// <summary>
/// Assistant message with content blocks.
/// </summary>
public record AssistantMessage : Message
{
    [JsonPropertyName("content")]
    public required IReadOnlyList<ContentBlock> Content { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }

    [JsonPropertyName("error")]
    public AssistantMessageError? Error { get; init; }
}

/// <summary>
/// System message with metadata.
/// </summary>
public record SystemMessage : Message
{
    [JsonPropertyName("subtype")]
    public required string Subtype { get; init; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }
}

/// <summary>
/// Result message with cost and usage information.
/// </summary>
public record ResultMessage : Message
{
    [JsonPropertyName("subtype")]
    public required string Subtype { get; init; }

    [JsonPropertyName("duration_ms")]
    public required int DurationMs { get; init; }

    [JsonPropertyName("duration_api_ms")]
    public required int DurationApiMs { get; init; }

    [JsonPropertyName("is_error")]
    public required bool IsError { get; init; }

    [JsonPropertyName("num_turns")]
    public required int NumTurns { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("total_cost_usd")]
    public decimal? TotalCostUsd { get; init; }

    [JsonPropertyName("usage")]
    public JsonElement? Usage { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("structured_output")]
    public JsonElement? StructuredOutput { get; init; }
}

/// <summary>
/// Stream event for partial message updates during streaming.
/// </summary>
public record StreamEvent : Message
{
    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("event")]
    public required JsonElement Event { get; init; }

    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }
}

#endregion

#region Permission Types

/// <summary>
/// Permission rule value.
/// </summary>
public record PermissionRuleValue(
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("rule_content")] string? RuleContent = null
);

/// <summary>
/// Permission update configuration.
/// </summary>
public record PermissionUpdate(
    [property: JsonPropertyName("type")] PermissionUpdateType Type,
    [property: JsonPropertyName("rules")] IReadOnlyList<PermissionRuleValue>? Rules = null,
    [property: JsonPropertyName("behavior")] PermissionBehavior? Behavior = null,
    [property: JsonPropertyName("mode")] PermissionMode? Mode = null,
    [property: JsonPropertyName("directories")] IReadOnlyList<string>? Directories = null,
    [property: JsonPropertyName("destination")] PermissionUpdateDestination? Destination = null
)
{
    /// <summary>
    /// Convert to dictionary format matching TypeScript control protocol.
    /// </summary>
    public Dictionary<string, object?> ToDictionary()
    {
        var result = new Dictionary<string, object?>
        {
            ["type"] = Type.ToJsonString()
        };

        if (Destination.HasValue)
            result["destination"] = Destination.Value.ToJsonString();

        if (Type is PermissionUpdateType.AddRules or PermissionUpdateType.ReplaceRules or PermissionUpdateType.RemoveRules)
        {
            if (Rules != null)
            {
                result["rules"] = Rules.Select(r => new Dictionary<string, object?>
                {
                    ["toolName"] = r.ToolName,
                    ["ruleContent"] = r.RuleContent
                }).ToList();
            }
            if (Behavior.HasValue)
                result["behavior"] = Behavior.Value.ToJsonString();
        }
        else if (Type == PermissionUpdateType.SetMode)
        {
            if (Mode.HasValue)
                result["mode"] = Mode.Value.ToJsonString();
        }
        else if (Type is PermissionUpdateType.AddDirectories or PermissionUpdateType.RemoveDirectories)
        {
            if (Directories != null)
                result["directories"] = Directories.ToList();
        }

        return result;
    }
}

/// <summary>
/// Context information for tool permission callbacks.
/// </summary>
public record ToolPermissionContext(
    object? Signal = null,
    IReadOnlyList<PermissionUpdate>? Suggestions = null
);

/// <summary>
/// Base class for permission results.
/// </summary>
public abstract record PermissionResult;

/// <summary>
/// Allow permission result.
/// </summary>
public record PermissionResultAllow(
    JsonElement? UpdatedInput = null,
    IReadOnlyList<PermissionUpdate>? UpdatedPermissions = null
) : PermissionResult
{
    public string Behavior => "allow";
}

/// <summary>
/// Deny permission result.
/// </summary>
public record PermissionResultDeny(
    string Message = "",
    bool Interrupt = false
) : PermissionResult
{
    public string Behavior => "deny";
}

/// <summary>
/// Delegate for tool permission callbacks.
/// </summary>
public delegate Task<PermissionResult> CanUseToolCallback(
    string toolName,
    JsonElement input,
    ToolPermissionContext context,
    CancellationToken cancellationToken = default
);

#endregion

#region Hook Types

/// <summary>
/// Base hook input fields.
/// </summary>
public record BaseHookInput
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("transcript_path")]
    public required string TranscriptPath { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; init; }
}

/// <summary>
/// Input data for PreToolUse hook events.
/// </summary>
public record PreToolUseHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "PreToolUse";

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public required JsonElement ToolInput { get; init; }

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }
}

/// <summary>
/// Input data for PostToolUse hook events.
/// </summary>
public record PostToolUseHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "PostToolUse";

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public required JsonElement ToolInput { get; init; }

    [JsonPropertyName("tool_response")]
    public required JsonElement ToolResponse { get; init; }

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }
}

/// <summary>
/// Input data for PostToolUseFailure hook events.
/// </summary>
public record PostToolUseFailureHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "PostToolUseFailure";

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public required JsonElement ToolInput { get; init; }

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("is_interrupt")]
    public bool? IsInterrupt { get; init; }
}

/// <summary>
/// Input data for UserPromptSubmit hook events.
/// </summary>
public record UserPromptSubmitHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "UserPromptSubmit";

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

/// <summary>
/// Input data for Stop hook events.
/// </summary>
public record StopHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "Stop";

    [JsonPropertyName("stop_hook_active")]
    public required bool StopHookActive { get; init; }
}

/// <summary>
/// Input data for SubagentStop hook events.
/// </summary>
public record SubagentStopHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "SubagentStop";

    [JsonPropertyName("stop_hook_active")]
    public required bool StopHookActive { get; init; }

    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }

    [JsonPropertyName("agent_transcript_path")]
    public required string AgentTranscriptPath { get; init; }

    [JsonPropertyName("agent_type")]
    public required string AgentType { get; init; }
}

/// <summary>
/// Input data for PreCompact hook events.
/// </summary>
public record PreCompactHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "PreCompact";

    [JsonPropertyName("trigger")]
    public required string Trigger { get; init; }

    [JsonPropertyName("custom_instructions")]
    public string? CustomInstructions { get; init; }
}

/// <summary>
/// Input data for Notification hook events.
/// </summary>
public record NotificationHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "Notification";

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("notification_type")]
    public required string NotificationType { get; init; }
}

/// <summary>
/// Input data for SubagentStart hook events.
/// </summary>
public record SubagentStartHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "SubagentStart";

    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }

    [JsonPropertyName("agent_type")]
    public required string AgentType { get; init; }
}

/// <summary>
/// Input data for PermissionRequest hook events.
/// </summary>
public record PermissionRequestHookInput : BaseHookInput
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName => "PermissionRequest";

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public required JsonElement ToolInput { get; init; }

    [JsonPropertyName("permission_suggestions")]
    public JsonElement? PermissionSuggestions { get; init; }
}

/// <summary>
/// Hook output configuration.
/// </summary>
public record HookOutput
{
    [JsonPropertyName("continue")]
    public bool? Continue { get; init; }

    [JsonPropertyName("suppressOutput")]
    public bool? SuppressOutput { get; init; }

    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("systemMessage")]
    public string? SystemMessage { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("hookSpecificOutput")]
    public JsonElement? HookSpecificOutput { get; init; }

    /// <summary>
    /// Set to true to defer hook execution (async mode).
    /// </summary>
    [JsonPropertyName("async")]
    public bool? Async { get; init; }

    /// <summary>
    /// Timeout in milliseconds for async hook operations.
    /// </summary>
    [JsonPropertyName("asyncTimeout")]
    public int? AsyncTimeout { get; init; }
}

/// <summary>
/// Hook context information.
/// </summary>
public record HookContext(object? Signal = null);

/// <summary>
/// Delegate for hook callbacks.
/// </summary>
public delegate Task<HookOutput> HookCallback(
    JsonElement input,
    string? toolUseId,
    HookContext context,
    CancellationToken cancellationToken = default
);

/// <summary>
/// Hook matcher configuration.
/// </summary>
public record HookMatcher(
    string? Matcher = null,
    IReadOnlyList<HookCallback>? Hooks = null,
    double? Timeout = null
);

#endregion

#region MCP Server Config

/// <summary>
/// MCP stdio server configuration.
/// </summary>
public record McpStdioServerConfig
{
    [JsonPropertyName("type")]
    public string Type => "stdio";

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("args")]
    public IReadOnlyList<string>? Args { get; init; }

    [JsonPropertyName("env")]
    public IReadOnlyDictionary<string, string>? Env { get; init; }
}

/// <summary>
/// MCP SSE server configuration.
/// </summary>
public record McpSSEServerConfig
{
    [JsonPropertyName("type")]
    public string Type => "sse";

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// MCP HTTP server configuration.
/// </summary>
public record McpHttpServerConfig
{
    [JsonPropertyName("type")]
    public string Type => "http";

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// SDK MCP server configuration for in-process servers.
/// </summary>
/// <remarks>
/// Use this configuration to run an MCP server in the same process as your application.
/// The server will communicate with Claude Code via the SDK's control protocol bridge.
/// </remarks>
/// <example>
/// <code>
/// var config = new McpSdkServerConfig
/// {
///     Name = "calculator",
///     Handlers = new McpServerHandlers
///     {
///         ListTools = ct => Task.FromResult&lt;IReadOnlyList&lt;McpToolDefinition&gt;&gt;(
///             [new McpToolDefinition { Name = "add", Description = "Add two numbers" }]
///         ),
///         CallTool = (name, args, ct) => Task.FromResult(
///             new McpToolResult { Content = [new McpContent { Type = "text", Text = "4" }] }
///         )
///     }
/// };
/// </code>
/// </example>
public record McpSdkServerConfig
{
    /// <summary>
    /// The server type identifier. Always "sdk" for in-process servers.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type => "sdk";

    /// <summary>
    /// The name of the server, used for routing MCP messages.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The MCP server handlers for processing requests.
    /// </summary>
    /// <remarks>
    /// Define handlers for tools, prompts, and resources that your server supports.
    /// Only the handlers you provide will be advertised as capabilities.
    /// </remarks>
    [JsonIgnore]
    public Mcp.McpServerHandlers Handlers { get; set; } = null!;
}

#endregion

#region System Prompt Config

/// <summary>
/// Configuration for using Claude Code's preset system prompt with optional additions.
/// </summary>
public record SystemPromptPreset
{
    /// <summary>Type identifier. Always "preset" for preset system prompts.</summary>
    [JsonPropertyName("type")]
    public string Type => "preset";

    /// <summary>The preset to use. Currently only "claude_code" is supported.</summary>
    [JsonPropertyName("preset")]
    public required string Preset { get; init; }

    /// <summary>Additional instructions to append to the preset system prompt.</summary>
    [JsonPropertyName("append")]
    public string? Append { get; init; }

    /// <summary>
    /// Creates a Claude Code preset system prompt with the specified append text.
    /// </summary>
    public static SystemPromptPreset ClaudeCode(string? append = null) =>
        new() { Preset = "claude_code", Append = append };
}

#endregion

#region Agent and Sandbox Config

/// <summary>
/// Agent definition configuration.
/// </summary>
public record AgentDefinition(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("tools")] IReadOnlyList<string>? Tools = null,
    [property: JsonPropertyName("model")] string? Model = null
);

/// <summary>
/// SDK plugin configuration.
/// </summary>
public record SdkPluginConfig(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("path")] string Path
);

/// <summary>
/// Network configuration for sandbox.
/// </summary>
public record SandboxNetworkConfig
{
    [JsonPropertyName("allowUnixSockets")]
    public IReadOnlyList<string>? AllowUnixSockets { get; init; }

    [JsonPropertyName("allowAllUnixSockets")]
    public bool? AllowAllUnixSockets { get; init; }

    [JsonPropertyName("allowLocalBinding")]
    public bool? AllowLocalBinding { get; init; }

    [JsonPropertyName("httpProxyPort")]
    public int? HttpProxyPort { get; init; }

    [JsonPropertyName("socksProxyPort")]
    public int? SocksProxyPort { get; init; }
}

/// <summary>
/// Violations to ignore in sandbox.
/// </summary>
public record SandboxIgnoreViolations
{
    [JsonPropertyName("file")]
    public IReadOnlyList<string>? File { get; init; }

    [JsonPropertyName("network")]
    public IReadOnlyList<string>? Network { get; init; }
}

/// <summary>
/// Sandbox settings configuration.
/// </summary>
public record SandboxSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("autoAllowBashIfSandboxed")]
    public bool? AutoAllowBashIfSandboxed { get; init; }

    [JsonPropertyName("excludedCommands")]
    public IReadOnlyList<string>? ExcludedCommands { get; init; }

    [JsonPropertyName("allowUnsandboxedCommands")]
    public bool? AllowUnsandboxedCommands { get; init; }

    [JsonPropertyName("network")]
    public SandboxNetworkConfig? Network { get; init; }

    [JsonPropertyName("ignoreViolations")]
    public SandboxIgnoreViolations? IgnoreViolations { get; init; }

    [JsonPropertyName("enableWeakerNestedSandbox")]
    public bool? EnableWeakerNestedSandbox { get; init; }
}

#endregion

#region ThinkingConfig

/// <summary>
/// Base interface for thinking configuration.
/// </summary>
public interface IThinkingConfig
{
    /// <summary>The thinking configuration type.</summary>
    string Type { get; }
}

/// <summary>
/// Adaptive thinking configuration — lets the model decide how much to think.
/// </summary>
public record ThinkingConfigAdaptive : IThinkingConfig
{
    /// <inheritdoc />
    public string Type => "adaptive";
}

/// <summary>
/// Enabled thinking configuration with a specific budget.
/// </summary>
public record ThinkingConfigEnabled(int BudgetTokens) : IThinkingConfig
{
    /// <inheritdoc />
    public string Type => "enabled";
}

/// <summary>
/// Disabled thinking configuration.
/// </summary>
public record ThinkingConfigDisabled : IThinkingConfig
{
    /// <inheritdoc />
    public string Type => "disabled";
}

#endregion

#region Claude Agent Options

/// <summary>
/// Query options for Claude SDK.
/// </summary>
public class ClaudeAgentOptions
{
    /// <summary>Base set of tools to enable.</summary>
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>Additional tools to allow.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>System prompt for the conversation. Can be a string or <see cref="SystemPromptPreset"/>.</summary>
    public object? SystemPrompt { get; init; }

    /// <summary>MCP server configurations (dict or path). Use <c>McpServers</c> helpers for in-process SDK servers.</summary>
    public object? McpServers { get; init; }

    /// <summary>Permission mode for tool execution.</summary>
    public PermissionMode? PermissionMode { get; init; }

    /// <summary>Continue from previous conversation.</summary>
    public bool ContinueConversation { get; init; }

    /// <summary>Session ID to resume.</summary>
    public string? Resume { get; init; }

    /// <summary>Maximum number of turns.</summary>
    public int? MaxTurns { get; init; }

    /// <summary>Maximum budget in USD.</summary>
    public decimal? MaxBudgetUsd { get; init; }

    /// <summary>Tools to disallow.</summary>
    public IReadOnlyList<string> DisallowedTools { get; init; } = [];

    /// <summary>Model to use.</summary>
    public string? Model { get; init; }

    /// <summary>Fallback model if primary unavailable.</summary>
    public string? FallbackModel { get; init; }

    /// <summary>Beta features to enable.</summary>
    public IReadOnlyList<string> Betas { get; init; } = [];

    /// <summary>Permission prompt tool name.</summary>
    public string? PermissionPromptToolName { get; init; }

    /// <summary>Working directory.</summary>
    public string? Cwd { get; init; }

    /// <summary>Path to CLI binary.</summary>
    public string? CliPath { get; init; }

    /// <summary>Settings path or JSON.</summary>
    public string? Settings { get; init; }

    /// <summary>Additional directories to include.</summary>
    public IReadOnlyList<string> AddDirs { get; init; } = [];

    /// <summary>Environment variables to set.</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    /// <summary>Extra CLI arguments.</summary>
    public IReadOnlyDictionary<string, string?> ExtraArgs { get; init; } = new Dictionary<string, string?>();

    /// <summary>Maximum buffer size for CLI stdout.</summary>
    public int? MaxBufferSize { get; init; }

    /// <summary>Callback for stderr output from CLI.</summary>
    public Action<string>? StderrCallback { get; init; }

    /// <summary>Tool permission callback.</summary>
    public CanUseToolCallback? CanUseTool { get; init; }

    /// <summary>Hook configurations.</summary>
    public IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcher>>? Hooks { get; init; }

    /// <summary>User identifier.</summary>
    public string? User { get; init; }

    /// <summary>Include partial messages during streaming.</summary>
    public bool IncludePartialMessages { get; init; }

    /// <summary>Fork session when resuming.</summary>
    public bool ForkSession { get; init; }

    /// <summary>Agent definitions.</summary>
    public IReadOnlyDictionary<string, AgentDefinition>? Agents { get; init; }

    /// <summary>Setting sources to load.</summary>
    public IReadOnlyList<SettingSource>? SettingSources { get; init; }

    /// <summary>Sandbox settings.</summary>
    public SandboxSettings? Sandbox { get; init; }

    /// <summary>Plugin configurations.</summary>
    public IReadOnlyList<SdkPluginConfig> Plugins { get; init; } = [];

    /// <summary>Maximum thinking tokens.</summary>
    /// <remarks>Deprecated: Use <see cref="Thinking"/> instead.</remarks>
    [Obsolete("Use Thinking instead.")]
    public int? MaxThinkingTokens { get; init; }

    /// <summary>
    /// Controls extended thinking behavior. Takes precedence over MaxThinkingTokens.
    /// </summary>
    public IThinkingConfig? Thinking { get; init; }

    /// <summary>
    /// Effort level for thinking depth.
    /// </summary>
    public EffortLevel? Effort { get; init; }

    /// <summary>Output format for structured outputs.</summary>
    public JsonElement? OutputFormat { get; init; }

    /// <summary>Enable file checkpointing.</summary>
    public bool EnableFileCheckpointing { get; init; }
}

#endregion

#region JSON Serialization Context

/// <summary>
/// JSON serialization context for AOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(UserMessage))]
[JsonSerializable(typeof(AssistantMessage))]
[JsonSerializable(typeof(SystemMessage))]
[JsonSerializable(typeof(ResultMessage))]
[JsonSerializable(typeof(StreamEvent))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(TextBlock))]
[JsonSerializable(typeof(ThinkingBlock))]
[JsonSerializable(typeof(ToolUseBlock))]
[JsonSerializable(typeof(ToolResultBlock))]
[JsonSerializable(typeof(HookOutput))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(List<ContentBlock>))]
internal partial class ClaudeJsonContext : JsonSerializerContext
{
}

#endregion
