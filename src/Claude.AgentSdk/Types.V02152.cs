// Claude Agent SDK for .NET — types added for parity with v0.2.152.
// Reference: claude-agent-sdk-python src/claude_agent_sdk/types.py @ 37a52c9

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.AgentSdk;

#region Task lifecycle

/// <summary>
/// Status values for a task_updated message.
/// </summary>
/// <remarks>
/// This vocabulary differs from <see cref="TaskNotificationStatus"/>: a killed
/// task is reported here as <c>killed</c>, while task_notification reports the
/// CLI's mapped <c>stopped</c>. See <see cref="TaskStatus.IsTerminal"/>.
/// </remarks>
public static class TaskUpdatedStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Killed = "killed";
}

/// <summary>
/// Task status helpers spanning both task message vocabularies.
/// </summary>
public static class TaskStatus
{
    /// <summary>
    /// Statuses meaning a task has finished and should be cleared from any
    /// "active task" tracking.
    /// </summary>
    /// <remarks>
    /// Deliberately spans both vocabularies. A task's terminal state can arrive
    /// as a TaskNotificationMessage (<c>stopped</c>) or as a
    /// <see cref="TaskUpdatedMessage"/> (<c>killed</c>) — and a task stopped via
    /// TaskStop reports only the latter, with the notification sometimes
    /// suppressed. Checking one vocabulary leaves such a task tracked as active
    /// forever.
    /// </remarks>
    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>
    {
        "completed",
        "failed",
        "stopped",
        "killed",
    };

    /// <summary>True when <paramref name="status"/> means the task has finished.</summary>
    public static bool IsTerminal(string? status) => status is not null && Terminal.Contains(status);
}

/// <summary>Origin subkinds for a task_notification message.</summary>
public static class TaskNotificationOriginSubkind
{
    public const string ScheduledTrigger = "scheduled-trigger";
    public const string PeerSendMessage = "peer-send-message";
}

/// <summary>
/// System message emitted when a background task's state changes.
/// </summary>
/// <remarks>
/// <see cref="Patch"/> carries only the changed fields. When <see cref="Status"/>
/// is terminal (see <see cref="TaskStatus.IsTerminal"/>) the task has finished —
/// and for a task stopped via TaskStop this message may be the only notice, with
/// no accompanying TaskNotificationMessage.
/// </remarks>
public record TaskUpdatedMessage : SystemMessage
{
    [JsonPropertyName("task_id")]
    public required string TaskId { get; init; }

    /// <summary>Changed fields only, as emitted by the CLI.</summary>
    [JsonPropertyName("patch")]
    public JsonElement Patch { get; init; }

    /// <summary>One of <see cref="TaskUpdatedStatus"/>, when the patch changed it.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }
}

#endregion

#region Message origin

/// <summary>
/// Known values of <see cref="MessageOrigin.Kind"/>.
/// </summary>
/// <remarks>
/// Open set. Newer CLI versions may emit kinds not listed here; treat anything
/// unrecognized as "not human" rather than rejecting it. That is why
/// <see cref="MessageOrigin.Kind"/> is a string and not an enum.
/// </remarks>
public static class MessageOriginKind
{
    public const string Human = "human";
    public const string Channel = "channel";
    public const string Peer = "peer";
    public const string TaskNotification = "task-notification";
    public const string Coordinator = "coordinator";
    public const string Unclassified = "unclassified";
    public const string Observer = "observer";
    public const string AutoContinuation = "auto-continuation";
    public const string ObserverActivity = "observer-activity";
}

/// <summary>
/// Where a message came from. Properties other than <see cref="Kind"/> are
/// populated only for the kinds that define them.
/// </summary>
public record MessageOrigin
{
    /// <summary>Discriminator. See <see cref="MessageOriginKind"/>; may hold an unrecognized value.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>kind == "channel": name of the MCP server the message arrived on.</summary>
    [JsonPropertyName("server")]
    public string? Server { get; init; }

    /// <summary>
    /// kind == "peer"/"observer": sender address. Sender-asserted — use it for
    /// reply routing or display, never as proof of identity.
    /// </summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>kind == "peer": sender display name, already normalized by the CLI.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// True only for an explicitly human-originated message. An unrecognized
    /// kind is not human, which is the upstream-documented default.
    /// </summary>
    [JsonIgnore]
    public bool IsHuman => Kind == MessageOriginKind.Human;
}

#endregion

#region Usage

/// <summary>
/// Per-model token usage and cost breakdown.
/// </summary>
/// <remarks>
/// Property names are camelCase on the wire, matching the TypeScript SDK: the
/// value is passed through verbatim from the CLI's <c>modelUsage</c> field
/// rather than being renamed to this SDK's usual snake_case.
/// </remarks>
public record ModelUsage
{
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("cacheReadInputTokens")]
    public int CacheReadInputTokens { get; init; }

    [JsonPropertyName("cacheCreationInputTokens")]
    public int CacheCreationInputTokens { get; init; }

    [JsonPropertyName("webSearchRequests")]
    public int WebSearchRequests { get; init; }

    [JsonPropertyName("costUSD")]
    public double CostUSD { get; init; }

    [JsonPropertyName("contextWindow")]
    public int ContextWindow { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; init; }
}

#endregion

#region Conversation reset

/// <summary>
/// Emitted when the session's conversation is replaced without ending the
/// connection — e.g. after <c>/clear</c>.
/// </summary>
/// <remarks>
/// A reset clears the transcript and zeroes the running totals reported on
/// subsequent ResultMessages. If you accumulate those totals across a long-lived
/// session, snapshot them when this arrives.
/// </remarks>
public record ConversationResetMessage
{
    /// <summary>
    /// Identifier for the fresh conversation. This is not the SessionId of
    /// subsequent messages — read that from the next message.
    /// </summary>
    [JsonPropertyName("new_conversation_id")]
    public required string NewConversationId { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    /// <summary>The outgoing session; messages after the reset carry a new id.</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}

#endregion
