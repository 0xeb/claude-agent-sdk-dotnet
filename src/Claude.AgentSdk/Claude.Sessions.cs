// Claude Agent SDK for .NET — Top-level session API surface.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/__init__.py
// (re-exports list_sessions, get_session_messages, list_subagents,
// get_subagent_messages, delete_session, rename_session, tag_session,
// get_session_info, list_sessions_from_store, import_session_to_store, etc.)

using System.Text.Json;
using Claude.AgentSdk.Sessions;

namespace Claude.AgentSdk;

/// <summary>
/// Top-level session API. Mirrors the free functions Python re-exports from
/// <c>claude_agent_sdk</c>. The store-backed async methods are the primary
/// SDK surface; disk-only helpers are not yet ported (use a
/// <see cref="Sessions.FileSessionStore"/> rooted at
/// <see cref="Sessions.SessionPaths.GetProjectsDir"/> if you want the on-disk
/// layout).
/// </summary>
public static class ClaudeSessions
{
    /// <summary>
    /// List sessions for the current project (or <paramref name="directory"/>)
    /// from a <see cref="ISessionStore"/>. Mirrors Python
    /// <c>list_sessions_from_store</c>.
    /// </summary>
    public static async Task<IReadOnlyList<SDKSessionInfo>> ListSessionsAsync(
        ISessionStore store,
        string? directory = null,
        int? limit = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var projectKey = SessionPaths.ProjectKeyForDirectory(directory);
        IReadOnlyList<SDKSessionInfo> infos;

        if (SessionStoreValidation.StoreImplements(store, nameof(ISessionStore.ListSessionSummariesAsync)))
        {
            var summaries = await store.ListSessionSummariesAsync(projectKey, cancellationToken).ConfigureAwait(false);
            infos = summaries
                .Select(s => SessionSummary.SummaryEntryToSdkInfo(s, directory))
                .OfType<SDKSessionInfo>()
                .OrderByDescending(i => i.LastModified)
                .ToList();
        }
        else if (SessionStoreValidation.StoreImplements(store, nameof(ISessionStore.ListSessionsAsync)))
        {
            // Fall back: load each session to derive summary.
            var listing = await store.ListSessionsAsync(projectKey, cancellationToken).ConfigureAwait(false);
            var derived = new List<SDKSessionInfo>();
            foreach (var entry in listing.OrderByDescending(e => e.Mtime))
            {
                var loaded = await store.LoadAsync(new SessionKey { ProjectKey = projectKey, SessionId = entry.SessionId }, cancellationToken).ConfigureAwait(false);
                if (loaded is null || loaded.Count == 0) continue;
                var folded = SessionSummary.FoldSessionSummary(null,
                    new SessionKey { ProjectKey = projectKey, SessionId = entry.SessionId },
                    loaded) with { Mtime = entry.Mtime };
                var info = SessionSummary.SummaryEntryToSdkInfo(folded, directory);
                if (info is not null) derived.Add(info);
            }
            infos = derived;
        }
        else
        {
            throw new NotSupportedException(
                "Store does not implement ListSessionsAsync or ListSessionSummariesAsync.");
        }

        if (offset > 0) infos = infos.Skip(offset).ToList();
        if (limit is int lim) infos = infos.Take(lim).ToList();
        return infos;
    }

    /// <summary>
    /// Fetch session info for one session from a <see cref="ISessionStore"/>.
    /// </summary>
    public static async Task<SDKSessionInfo?> GetSessionInfoAsync(
        ISessionStore store,
        string sessionId,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        var projectKey = SessionPaths.ProjectKeyForDirectory(directory);
        var key = new SessionKey { ProjectKey = projectKey, SessionId = sessionId };
        var entries = await store.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        if (entries is null || entries.Count == 0) return null;
        var folded = SessionSummary.FoldSessionSummary(null, key, entries);
        return SessionSummary.SummaryEntryToSdkInfo(folded, directory);
    }

    /// <summary>
    /// Load user/assistant messages from a session in a
    /// <see cref="ISessionStore"/>. Mirrors Python
    /// <c>get_session_messages_from_store</c>.
    /// </summary>
    public static async Task<IReadOnlyList<SessionMessage>> GetSessionMessagesAsync(
        ISessionStore store,
        string sessionId,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        var projectKey = SessionPaths.ProjectKeyForDirectory(directory);
        var entries = await store.LoadAsync(new SessionKey { ProjectKey = projectKey, SessionId = sessionId }, cancellationToken).ConfigureAwait(false);
        if (entries is null) throw new SessionNotFoundException(sessionId);
        return EntriesToSessionMessages(entries, sessionId);
    }

    /// <summary>
    /// List subagent identifiers (sub-keys) for a session in a
    /// <see cref="ISessionStore"/>.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ListSubagentsAsync(
        ISessionStore store,
        string sessionId,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        if (!SessionStoreValidation.StoreImplements(store, nameof(ISessionStore.ListSubkeysAsync)))
            return Array.Empty<string>();
        var projectKey = SessionPaths.ProjectKeyForDirectory(directory);
        return await store.ListSubkeysAsync(new SessionListSubkeysKey(projectKey, sessionId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Load messages for a single subagent transcript from a
    /// <see cref="ISessionStore"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SessionMessage>> GetSubagentMessagesAsync(
        ISessionStore store,
        string sessionId,
        string subpath,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        var projectKey = SessionPaths.ProjectKeyForDirectory(directory);
        var entries = await store.LoadAsync(new SessionKey
        {
            ProjectKey = projectKey,
            SessionId = sessionId,
            Subpath = subpath,
        }, cancellationToken).ConfigureAwait(false);
        if (entries is null) throw new SessionNotFoundException(sessionId);
        return EntriesToSessionMessages(entries, sessionId);
    }

    /// <summary>
    /// Delete a session via <paramref name="store"/>. No-op if the store
    /// does not implement <see cref="ISessionStore.DeleteAsync"/>.
    /// </summary>
    public static Task DeleteSessionAsync(
        ISessionStore store,
        string sessionId,
        string? directory = null,
        CancellationToken cancellationToken = default)
        => SessionMutations.DeleteSessionViaStoreAsync(store, sessionId, directory, cancellationToken);

    /// <summary>Rename a session by appending a custom-title entry.</summary>
    public static Task RenameSessionAsync(
        ISessionStore store,
        string sessionId,
        string title,
        string? directory = null,
        CancellationToken cancellationToken = default)
        => SessionMutations.RenameSessionViaStoreAsync(store, sessionId, title, directory, cancellationToken);

    /// <summary>Tag a session. Pass <c>null</c> to clear.</summary>
    public static Task TagSessionAsync(
        ISessionStore store,
        string sessionId,
        string? tag,
        string? directory = null,
        CancellationToken cancellationToken = default)
        => SessionMutations.TagSessionViaStoreAsync(store, sessionId, tag, directory, cancellationToken);

    /// <summary>
    /// Return summaries for all sessions in a project. Mirrors Python
    /// <c>list_session_summaries</c> on the store directly.
    /// </summary>
    public static async Task<IReadOnlyList<SessionSummaryEntry>> ListSessionSummariesAsync(
        ISessionStore store,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        var projectKey = SessionPaths.ProjectKeyForDirectory(directory);
        if (!SessionStoreValidation.StoreImplements(store, nameof(ISessionStore.ListSessionSummariesAsync)))
            return Array.Empty<SessionSummaryEntry>();
        return await store.ListSessionSummariesAsync(projectKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replay a local on-disk session JSONL into a
    /// <see cref="ISessionStore"/>. Mirrors Python <c>import_session_to_store</c>.
    /// </summary>
    public static Task ImportSessionToStoreAsync(
        string sessionId,
        ISessionStore store,
        string? directory = null,
        bool includeSubagents = true,
        int batchSize = TranscriptMirrorBatcher.DefaultMaxPendingEntries,
        CancellationToken cancellationToken = default)
        => SessionImport.ImportSessionToStoreAsync(sessionId, store, directory, includeSubagents, batchSize, cancellationToken);

    private static IReadOnlyList<SessionMessage> EntriesToSessionMessages(
        IReadOnlyList<SessionStoreEntry> entries,
        string sessionId)
    {
        var result = new List<SessionMessage>();
        foreach (var e in entries)
        {
            if (e.Type != "user" && e.Type != "assistant") continue;
            if (string.IsNullOrEmpty(e.Uuid)) continue;
            var obj = SessionSummary.EntryToJsonObject(e);
            JsonElement messageData;
            if (obj.TryGetPropertyValue("message", out var msg) && msg is not null)
            {
                using var doc = JsonDocument.Parse(msg.ToJsonString());
                messageData = doc.RootElement.Clone();
            }
            else
            {
                using var doc = JsonDocument.Parse("{}");
                messageData = doc.RootElement.Clone();
            }
            string? parentToolUseId = null;
            if (obj.TryGetPropertyValue("parent_tool_use_id", out var p)
                && p is System.Text.Json.Nodes.JsonValue pv && pv.TryGetValue<string>(out var ps))
                parentToolUseId = ps;

            result.Add(new SessionMessage(
                Type: e.Type,
                Uuid: e.Uuid!,
                SessionId: sessionId,
                MessageData: messageData,
                ParentToolUseId: parentToolUseId));
        }
        return result;
    }
}
