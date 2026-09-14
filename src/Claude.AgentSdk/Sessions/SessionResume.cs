// Claude Agent SDK for .NET — Materialize a SessionStore-backed resume into a temp CLAUDE_CONFIG_DIR.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_resume.py
// (materialize_resume_session @ 122, build_mirror_batcher @ 89,
// apply_materialized_options @ 69)

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claude.AgentSdk.Sessions;

/// <summary>Result of <see cref="SessionResume.MaterializeResumeSessionAsync"/>.</summary>
/// <remarks>
/// <para><c>ConfigDir</c> is a temporary directory laid out like
/// <c>~/.claude</c>. Point the subprocess at it via
/// <c>CLAUDE_CONFIG_DIR</c>.</para>
/// <para><c>ResumeSessionId</c> should be passed as <c>--resume</c>.</para>
/// <para><c>CleanupAsync</c> removes <c>ConfigDir</c> (best-effort) and must
/// be called after the subprocess exits.</para>
/// </remarks>
public sealed record MaterializedResume(
    string ConfigDir,
    string ResumeSessionId,
    Func<CancellationToken, Task> CleanupAsync);

/// <summary>
/// Session-resume helpers: materialize a session from a
/// <see cref="ISessionStore"/> to a temporary <c>CLAUDE_CONFIG_DIR</c> so the
/// CLI subprocess (which only knows how to resume from a local file) can pick
/// it up. Mirrors Python <c>session_resume</c>.
/// </summary>
public static class SessionResume
{
    /// <summary>Default load timeout (mirrors Python's 30s default).</summary>
    public static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Construct the <see cref="TranscriptMirrorBatcher"/> for a session.
    /// <paramref name="flushMode"/> <see cref="SessionStoreFlushMode.Eager"/>
    /// zeroes the batcher thresholds so every enqueued frame schedules a
    /// background flush.
    /// </summary>
    public static TranscriptMirrorBatcher BuildMirrorBatcher(
        ISessionStore store,
        MaterializedResume? materialized,
        IReadOnlyDictionary<string, string>? env,
        MirrorErrorCallback onError,
        SessionStoreFlushMode flushMode = SessionStoreFlushMode.Batched)
    {
        var projectsDir = materialized is not null
            ? Path.Combine(materialized.ConfigDir, "projects")
            : SessionPaths.GetProjectsDir(env);

        var eager = flushMode == SessionStoreFlushMode.Eager;
        return new TranscriptMirrorBatcher(store, projectsDir, onError)
        {
            MaxPendingEntries = eager ? 0 : TranscriptMirrorBatcher.DefaultMaxPendingEntries,
            MaxPendingBytes = eager ? 0 : TranscriptMirrorBatcher.DefaultMaxPendingBytes,
        };
    }

    /// <summary>
    /// Load a session from <paramref name="options"/>.SessionStore and write
    /// it to a temp dir. Returns <c>null</c> when no materialization is
    /// needed. Mirrors Python <c>materialize_resume_session</c>.
    /// </summary>
    public static async Task<MaterializedResume?> MaterializeResumeSessionAsync(
        ClaudeAgentOptions options,
        CancellationToken cancellationToken = default)
    {
        var store = options.SessionStore;
        if (store is null) return null;
        if (options.Resume is null && !options.ContinueConversation) return null;

        var timeout = DefaultLoadTimeout;
        var projectKey = SessionPaths.ProjectKeyForDirectory(options.Cwd);

        (string SessionId, IReadOnlyList<SessionStoreEntry> Entries)? resolved;
        if (options.Resume is not null)
        {
            if (!SessionPaths.ValidateUuid(options.Resume)) return null;
            resolved = await LoadCandidateAsync(store, projectKey, options.Resume, timeout, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            resolved = await ResolveContinueCandidateAsync(store, projectKey, timeout, cancellationToken).ConfigureAwait(false);
        }
        if (resolved is null) return null;

        var sessionId = resolved.Value.SessionId;
        var entries = resolved.Value.Entries;

        var tmpBase = Path.Combine(Path.GetTempPath(), "claude-resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpBase);
        try
        {
            var projectDir = Path.Combine(tmpBase, "projects", projectKey);
            Directory.CreateDirectory(projectDir);
            WriteJsonl(Path.Combine(projectDir, sessionId + ".jsonl"), entries);

            // Materialize subagent transcripts if the store can enumerate them.
            if (SessionStoreValidation.StoreImplements(store, nameof(ISessionStore.ListSubkeysAsync)))
            {
                await MaterializeSubkeysAsync(store, projectDir, projectKey, sessionId, timeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await RmTreeWithRetryAsync(tmpBase).ConfigureAwait(false);
            throw;
        }

        Func<CancellationToken, Task> cleanup = ct => RmTreeWithRetryAsync(tmpBase, ct);
        return new MaterializedResume(tmpBase, sessionId, cleanup);
    }

    private static async Task<(string SessionId, IReadOnlyList<SessionStoreEntry> Entries)?> LoadCandidateAsync(
        ISessionStore store, string projectKey, string sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var entries = await TaskCompat.WithTimeoutAsync(
            t => store.LoadAsync(new SessionKey { ProjectKey = projectKey, SessionId = sessionId }, t),
            timeout,
            $"SessionStore.LoadAsync() for session {sessionId}",
            ct).ConfigureAwait(false);
        if (entries is null || entries.Count == 0) return null;
        return (sessionId, entries);
    }

    private static async Task<(string SessionId, IReadOnlyList<SessionStoreEntry> Entries)?> ResolveContinueCandidateAsync(
        ISessionStore store, string projectKey, TimeSpan timeout, CancellationToken ct)
    {
        var sessions = await TaskCompat.WithTimeoutAsync(
            t => store.ListSessionsAsync(projectKey, t),
            timeout,
            "SessionStore.ListSessionsAsync()",
            ct).ConfigureAwait(false);
        if (sessions.Count == 0) return null;
        foreach (var cand in sessions.OrderByDescending(s => s.Mtime))
        {
            if (!SessionPaths.ValidateUuid(cand.SessionId)) continue;
            var loaded = await LoadCandidateAsync(store, projectKey, cand.SessionId, timeout, ct).ConfigureAwait(false);
            if (loaded is null) continue;
            // Skip sidechains.
            var firstObj = SessionSummary.EntryToJsonObject(loaded.Value.Entries[0]);
            if (firstObj.TryGetPropertyValue("isSidechain", out var s)
                && s is JsonValue jv && jv.TryGetValue<bool>(out var b) && b)
                continue;
            return loaded;
        }
        return null;
    }

    private static async Task MaterializeSubkeysAsync(
        ISessionStore store,
        string projectDir,
        string projectKey,
        string sessionId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var sessionSubDir = Path.Combine(projectDir, sessionId);
        var subkeys = await TaskCompat.WithTimeoutAsync(
            t => store.ListSubkeysAsync(new SessionListSubkeysKey(projectKey, sessionId), t),
            timeout,
            $"SessionStore.ListSubkeysAsync() for session {sessionId}",
            ct).ConfigureAwait(false);

        foreach (var subpath in subkeys)
        {
            if (!IsSafeSubpath(subpath, sessionSubDir)) continue;

            var subKey = new SessionKey { ProjectKey = projectKey, SessionId = sessionId, Subpath = subpath };
            var subEntries = await TaskCompat.WithTimeoutAsync(
                t => store.LoadAsync(subKey, t),
                timeout,
                $"SessionStore.LoadAsync() for session {sessionId} subpath {subpath}",
                ct).ConfigureAwait(false);
            if (subEntries is null || subEntries.Count == 0) continue;

            var metadata = new List<JsonObject>();
            var transcript = new List<SessionStoreEntry>();
            foreach (var e in subEntries)
            {
                if (e.Type == "agent_metadata") metadata.Add(SessionSummary.EntryToJsonObject(e));
                else transcript.Add(e);
            }

            var subOsPath = subpath.Replace('/', Path.DirectorySeparatorChar);
            var subFile = Path.Combine(sessionSubDir, subOsPath + ".jsonl");
            if (transcript.Count > 0) WriteJsonl(subFile, transcript);
            if (metadata.Count > 0)
            {
                var last = metadata[^1];
                last.Remove("type");
                var metaPath = Path.Combine(Path.GetDirectoryName(subFile)!,
                    Path.GetFileNameWithoutExtension(subFile) + ".meta.json");
                Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
                File.WriteAllText(metaPath, last.ToJsonString(), Encoding.UTF8);
            }
        }
    }

    private static bool IsSafeSubpath(string subpath, string sessionDir)
    {
        if (string.IsNullOrEmpty(subpath)) return false;
        if (subpath.Contains('\0')) return false;
        if (subpath.StartsWith('/') || subpath.StartsWith('\\')) return false;
        if (Path.IsPathRooted(subpath)) return false;
        // Reject drive-prefixed (C:foo) regardless of host OS.
        if (subpath.Length >= 2 && char.IsLetter(subpath[0]) && subpath[1] == ':') return false;
        foreach (var p in subpath.Split('/', '\\'))
        {
            if (p == "." || p == "..") return false;
        }
        try
        {
            var target = Path.GetFullPath(Path.Combine(sessionDir, subpath) + ".jsonl");
            var root = Path.GetFullPath(sessionDir);
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && target != root)
            {
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteJsonl(string path, IReadOnlyList<SessionStoreEntry> entries)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var sw = new StreamWriter(path, append: false, Encoding.UTF8);
        foreach (var e in entries)
        {
            sw.Write(SessionSummary.EntryToJsonObject(e).ToJsonString());
            sw.Write('\n');
        }
    }

    private static async Task RmTreeWithRetryAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) return;
        const int retries = 4;
        var delay = TimeSpan.FromMilliseconds(100);
        for (int i = 0; i < retries; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        try { Directory.Delete(path, recursive: true); } catch { /* ignore */ }
    }
}
