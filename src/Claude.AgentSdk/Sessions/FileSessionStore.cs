// Claude Agent SDK for .NET — JSONL file-based ISessionStore.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/sessions.py
// (on-disk transcript layout under <projects_dir>/<project_key>/<session_id>.jsonl
// plus subagents/<...>.jsonl). Pure .NET implementation — there is no direct
// Python counterpart, but the on-disk layout matches what the CLI writes and
// what file_path_to_session_key() decodes.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// JSONL file-based <see cref="ISessionStore"/>. Stores transcripts on disk
/// using the same layout the CLI uses:
/// <list type="bullet">
///   <item><c>{root}/{project_key}/{session_id}.jsonl</c></item>
///   <item><c>{root}/{project_key}/{session_id}/{subpath}.jsonl</c></item>
///   <item><c>{root}/{project_key}/.summaries/{session_id}.json</c> (summary sidecar)</item>
/// </list>
/// <para>Thread-safe: each operation serializes on an internal lock so
/// interleaved appends from concurrent tasks remain ordered.</para>
/// </summary>
public sealed class FileSessionStore : ISessionStore
{
    private readonly string _root;
    private readonly object _gate = new();

    /// <summary>Construct a store rooted at <paramref name="rootDirectory"/>.
    /// The directory is created if it does not exist.</summary>
    public FileSessionStore(string rootDirectory)
    {
        _root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Root directory under which transcripts and summary sidecars are stored.</summary>
    public string RootDirectory => _root;

    private string ProjectDir(string projectKey) => Path.Combine(_root, projectKey);

    private string TranscriptPath(SessionKey key)
    {
        var pdir = ProjectDir(key.ProjectKey);
        if (string.IsNullOrEmpty(key.Subpath))
            return Path.Combine(pdir, key.SessionId + ".jsonl");
        // Subpath is always '/'-joined in the wire format; translate to OS sep.
        var sub = key.Subpath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(pdir, key.SessionId, sub + ".jsonl");
    }

    private string SummariesDir(string projectKey) => Path.Combine(ProjectDir(projectKey), ".summaries");
    private string SummaryPath(string projectKey, string sessionId)
        => Path.Combine(SummariesDir(projectKey), sessionId + ".json");

    /// <inheritdoc />
    public async Task AppendAsync(SessionKey key, IReadOnlyList<SessionStoreEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return;
        var path = TranscriptPath(key);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(SerializeEntry(e));
            sb.Append('\n');
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());

        long mtime;
        SessionSummaryEntry? folded = null;
        lock (_gate)
        {
            // Append synchronously while holding the lock to preserve ordering
            // between concurrent appends (mirrors Python adapters where the
            // event loop serializes writes).
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
            }
            mtime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try { File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(mtime).UtcDateTime); }
            catch { /* best-effort */ }

            if (string.IsNullOrEmpty(key.Subpath))
            {
                var prev = TryReadSummary(key.ProjectKey, key.SessionId);
                folded = SessionSummary.FoldSessionSummary(prev, key, entries) with { Mtime = mtime };
                WriteSummary(key.ProjectKey, folded);
            }
        }
        await Task.CompletedTask.ConfigureAwait(false);
        _ = folded; // silence analyzer when summary path isn't taken
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionStoreEntry>?> LoadAsync(SessionKey key, CancellationToken cancellationToken = default)
    {
        var path = TranscriptPath(key);
        if (!File.Exists(path)) return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(null);

        var results = new List<SessionStoreEntry>();
        lock (_gate)
        {
            using var sr = new StreamReader(path, Encoding.UTF8);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                results.Add(ParseEntry(line));
            }
        }
        return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionStoreListEntry>> ListSessionsAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        var pdir = ProjectDir(projectKey);
        var results = new List<SessionStoreListEntry>();
        if (!Directory.Exists(pdir))
            return Task.FromResult<IReadOnlyList<SessionStoreListEntry>>(results);

        foreach (var file in Directory.EnumerateFiles(pdir, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Use real file mtime in ms — shares the same clock as AppendAsync().
            long mtimeMs;
            try { mtimeMs = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero).ToUnixTimeMilliseconds(); }
            catch { mtimeMs = 0; }
            results.Add(new SessionStoreListEntry(name, mtimeMs));
        }
        return Task.FromResult<IReadOnlyList<SessionStoreListEntry>>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSummaryEntry>> ListSessionSummariesAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        var sdir = SummariesDir(projectKey);
        var results = new List<SessionSummaryEntry>();
        if (!Directory.Exists(sdir))
            return Task.FromResult<IReadOnlyList<SessionSummaryEntry>>(results);

        foreach (var file in Directory.EnumerateFiles(sdir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var summ = JsonSerializer.Deserialize<SessionSummaryEntry>(text);
                if (summ is not null) results.Add(summ);
            }
            catch
            {
                // Skip corrupt sidecars — they will be regenerated on the next append.
            }
        }
        return Task.FromResult<IReadOnlyList<SessionSummaryEntry>>(results);
    }

    /// <inheritdoc />
    public Task DeleteAsync(SessionKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var path = TranscriptPath(key);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }

            if (string.IsNullOrEmpty(key.Subpath))
            {
                var subRoot = Path.Combine(ProjectDir(key.ProjectKey), key.SessionId);
                if (Directory.Exists(subRoot))
                {
                    try { Directory.Delete(subRoot, recursive: true); } catch { /* ignore */ }
                }
                var summary = SummaryPath(key.ProjectKey, key.SessionId);
                if (File.Exists(summary))
                {
                    try { File.Delete(summary); } catch { /* ignore */ }
                }
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSubkeysAsync(SessionListSubkeysKey key, CancellationToken cancellationToken = default)
    {
        var subRoot = Path.Combine(ProjectDir(key.ProjectKey), key.SessionId);
        var results = new List<string>();
        if (!Directory.Exists(subRoot))
            return Task.FromResult<IReadOnlyList<string>>(results);

        foreach (var file in Directory.EnumerateFiles(subRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(subRoot, file);
            // Strip .jsonl and normalize to '/'.
            rel = rel[..^".jsonl".Length];
            rel = rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            results.Add(rel);
        }
        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    // ---- Helpers ----------------------------------------------------------

    private SessionSummaryEntry? TryReadSummary(string projectKey, string sessionId)
    {
        var path = SummaryPath(projectKey, sessionId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SessionSummaryEntry>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    private void WriteSummary(string projectKey, SessionSummaryEntry summary)
    {
        var dir = SummariesDir(projectKey);
        Directory.CreateDirectory(dir);
        var path = SummaryPath(projectKey, summary.SessionId);
        File.WriteAllText(path, JsonSerializer.Serialize(summary), Encoding.UTF8);
    }

    private static string SerializeEntry(SessionStoreEntry e)
    {
        var obj = SessionSummary.EntryToJsonObject(e);
        return obj.ToJsonString();
    }

    private static SessionStoreEntry ParseEntry(string line)
    {
        var node = JsonNode.Parse(line);
        if (node is JsonObject obj)
            return SessionSummary.JsonObjectToEntry(obj);
        // Fallback for non-object lines: pass through opaque.
        return new SessionStoreEntry { Type = "" };
    }
}
