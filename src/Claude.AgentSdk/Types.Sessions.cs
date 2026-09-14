// Claude Agent SDK for .NET — Session store stub (T23 / Phase 2B).
// Full implementation (InMemorySessionStore, file-based, batcher) lands in Phase 3B.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/types.py @ c352a50
// (lines 1276..1553) and session_store.py.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.AgentSdk;

#region Session Store Types

/// <summary>
/// Identifies a session transcript or subagent transcript in a store.
/// Main transcripts have no <see cref="Subpath"/>; subagent transcripts include
/// e.g. "subagents/agent-{id}". Python commit 6e3d54f.
/// </summary>
public record SessionKey
{
    [JsonPropertyName("project_key")]
    public required string ProjectKey { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    /// <summary>Omit for the main transcript; set for subagent files.</summary>
    [JsonPropertyName("subpath")]
    public string? Subpath { get; init; }
}

/// <summary>
/// One JSONL transcript line. Concrete shape is the CLI's on-disk format
/// (a large discriminated union); adapters should treat entries as pass-through
/// blobs. Python commit 6e3d54f.
/// </summary>
public record SessionStoreEntry
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>All other fields (opaque JSON pass-through).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; init; }
}

/// <summary>Entry returned by <see cref="ISessionStore.ListSessionsAsync"/>.</summary>
public record SessionStoreListEntry(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("mtime")] long Mtime
);

/// <summary>
/// Incrementally-maintained session summary. Stores obtain this from
/// fold_session_summary inside <see cref="ISessionStore.AppendAsync"/> and
/// persist it verbatim. Python commit 6e3d54f.
/// </summary>
public record SessionSummaryEntry
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("mtime")]
    public required long Mtime { get; init; }

    /// <summary>Opaque SDK-owned summary state. Persist verbatim; do not interpret.</summary>
    [JsonPropertyName("data")]
    public required JsonElement Data { get; init; }
}

/// <summary>Key argument to <see cref="ISessionStore.ListSubkeysAsync"/> (no subpath).</summary>
public record SessionListSubkeysKey(
    [property: JsonPropertyName("project_key")] string ProjectKey,
    [property: JsonPropertyName("session_id")] string SessionId
);

/// <summary>
/// Controls when transcript-mirror entries are flushed to a SessionStore.
/// Python commit 0a69e94.
/// </summary>
public enum SessionStoreFlushMode
{
    /// <summary>
    /// Buffer entries and flush once per turn (default), or when the buffer
    /// exceeds 500 entries / 1 MiB.
    /// </summary>
    Batched,
    /// <summary>Trigger a background flush after every transcript_mirror frame.</summary>
    Eager
}

internal static class SessionEnumHelpers
{
    public static string ToJsonString(this SessionStoreFlushMode m) => m switch
    {
        SessionStoreFlushMode.Batched => "batched",
        SessionStoreFlushMode.Eager => "eager",
        _ => m.ToString().ToLowerInvariant()
    };
}

/// <summary>
/// Session metadata returned by SessionHelper.ListSessions(). Python commit reference:
/// SDKSessionInfo in types.py.
/// </summary>
public record SDKSessionInfo
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("last_modified")]
    public required long LastModified { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    [JsonPropertyName("custom_title")]
    public string? CustomTitle { get; init; }

    [JsonPropertyName("first_prompt")]
    public string? FirstPrompt { get; init; }

    [JsonPropertyName("git_branch")]
    public string? GitBranch { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; init; }
}

/// <summary>
/// A user or assistant message from a session transcript. Python commit reference:
/// SessionMessage in types.py.
/// </summary>
public record SessionMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("uuid")] string Uuid,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("message")] JsonElement MessageData,
    [property: JsonPropertyName("parent_tool_use_id")] string? ParentToolUseId = null
);

/// <summary>
/// Adapter for mirroring session transcripts to external storage.
/// Only <see cref="AppendAsync"/> and <see cref="LoadAsync"/> are required;
/// the rest are optional (return null/throw <see cref="NotImplementedException"/>
/// to signal "absent"). Python commit 6e3d54f.
///
/// <para>NOTE: This is a stub interface added in Phase 2B for API surface area.
/// The InMemorySessionStore, file-based store, conformance harness, and wiring
/// into ClaudeAgentOptions/QueryHandler arrive in Phase 3B.</para>
/// </summary>
public interface ISessionStore
{
    /// <summary>Mirror a batch of transcript entries.</summary>
    Task AppendAsync(SessionKey key, IReadOnlyList<SessionStoreEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>Load a full session for resume; return null if never written.</summary>
    Task<IReadOnlyList<SessionStoreEntry>?> LoadAsync(SessionKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// List sessions for a project key. Optional — throw <see cref="NotImplementedException"/>
    /// if unsupported (caller probes via try/catch).
    /// </summary>
    Task<IReadOnlyList<SessionStoreListEntry>> ListSessionsAsync(string projectKey, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>Return incrementally-maintained summaries for all sessions in one call. Optional.</summary>
    Task<IReadOnlyList<SessionSummaryEntry>> ListSessionSummariesAsync(string projectKey, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>Delete a session. Optional — no-op semantics for WORM/append-only backends.</summary>
    Task DeleteAsync(SessionKey key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>List all subpath keys under a session. Optional.</summary>
    Task<IReadOnlyList<string>> ListSubkeysAsync(SessionListSubkeysKey key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

#endregion
