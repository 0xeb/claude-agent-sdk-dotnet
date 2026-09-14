// Reference ISessionStore adapter — JSONL part-file backed.
// Mirrors the S3 part-file pattern from
// reference/claude-agent-sdk-python/examples/session_stores/s3_session_store.py
// using the local filesystem so the example needs no external dependencies.
//
// Layout (analogous to s3://{bucket}/{prefix}{project_key}/{session_id}/part-{epoch}-{rand}.jsonl):
//
//     {root}/{project_key}/{session_id}/part-{epochMs13}-{rand6}.jsonl
//     {root}/{project_key}/{session_id}/{subpath}/part-{epochMs13}-{rand6}.jsonl
//     {root}/{project_key}/{session_id}/.summary.json
//
// Each AppendAsync() writes a fresh part file; LoadAsync() lists, sorts,
// concatenates. This is intentionally naive — the goal is to demonstrate
// "how to satisfy the ISessionStore contract", not to ship a production
// store.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Claude.AgentSdk;
using Claude.AgentSdk.Sessions;

namespace Claude.AgentSdk.Examples.SessionStores.JsonlPartFileStore;

/// <summary>
/// Part-file-backed <see cref="ISessionStore"/> reference adapter. Mirrors
/// the S3 reference adapter shape from the Python SDK.
/// </summary>
public sealed class JsonlPartFileSessionStore : ISessionStore
{
    private readonly string _root;
    private readonly object _gate = new();
    private readonly Random _random = new();

    /// <summary>Construct a store rooted at <paramref name="rootDirectory"/>.</summary>
    public JsonlPartFileSessionStore(string rootDirectory)
    {
        _root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Root directory where part files live.</summary>
    public string RootDirectory => _root;

    private string SessionDir(SessionKey key)
    {
        var baseDir = Path.Combine(_root, key.ProjectKey, key.SessionId);
        if (string.IsNullOrEmpty(key.Subpath)) return baseDir;
        var sub = key.Subpath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(baseDir, sub);
    }

    private string SummaryPath(string projectKey, string sessionId)
        => Path.Combine(_root, projectKey, sessionId, ".summary.json");

    /// <inheritdoc />
    public Task AppendAsync(SessionKey key, IReadOnlyList<SessionStoreEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return Task.CompletedTask;
        lock (_gate)
        {
            var dir = SessionDir(key);
            Directory.CreateDirectory(dir);

            var epochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var rand = _random.Next(0, 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);
            var partName = $"part-{epochMs.ToString("D13", CultureInfo.InvariantCulture)}-{rand}.jsonl";
            var partPath = Path.Combine(dir, partName);

            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                sb.Append(JsonSerializer.Serialize(e)).Append('\n');
            }
            File.WriteAllText(partPath, sb.ToString(), Encoding.UTF8);

            if (string.IsNullOrEmpty(key.Subpath))
            {
                SessionSummaryEntry? prev = null;
                var sumPath = SummaryPath(key.ProjectKey, key.SessionId);
                if (File.Exists(sumPath))
                {
                    try { prev = JsonSerializer.Deserialize<SessionSummaryEntry>(File.ReadAllText(sumPath)); }
                    catch { /* corrupted summary — refold from scratch */ }
                }
                var folded = SessionSummary.FoldSessionSummary(prev, key, entries) with { Mtime = epochMs };
                Directory.CreateDirectory(Path.GetDirectoryName(sumPath)!);
                File.WriteAllText(sumPath, JsonSerializer.Serialize(folded));
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionStoreEntry>?> LoadAsync(SessionKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var dir = SessionDir(key);
            if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(null);

            var parts = Directory.GetFiles(dir, "part-*.jsonl", SearchOption.TopDirectoryOnly);
            if (parts.Length == 0) return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(null);

            Array.Sort(parts, StringComparer.Ordinal);

            var entries = new List<SessionStoreEntry>();
            foreach (var p in parts)
            {
                foreach (var line in File.ReadAllLines(p))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = JsonSerializer.Deserialize<SessionStoreEntry>(line);
                    if (entry is not null) entries.Add(entry);
                }
            }
            return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(entries);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionStoreListEntry>> ListSessionsAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var projDir = Path.Combine(_root, projectKey);
            var result = new List<SessionStoreListEntry>();
            if (!Directory.Exists(projDir))
                return Task.FromResult<IReadOnlyList<SessionStoreListEntry>>(result);

            foreach (var sessDir in Directory.EnumerateDirectories(projDir))
            {
                var sessionId = Path.GetFileName(sessDir);
                var parts = Directory.GetFiles(sessDir, "part-*.jsonl", SearchOption.TopDirectoryOnly);
                if (parts.Length == 0) continue;
                long mtime = 0;
                foreach (var p in parts)
                {
                    var mt = new DateTimeOffset(File.GetLastWriteTimeUtc(p)).ToUnixTimeMilliseconds();
                    if (mt > mtime) mtime = mt;
                }
                result.Add(new SessionStoreListEntry(sessionId, mtime));
            }
            return Task.FromResult<IReadOnlyList<SessionStoreListEntry>>(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSummaryEntry>> ListSessionSummariesAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var projDir = Path.Combine(_root, projectKey);
            var result = new List<SessionSummaryEntry>();
            if (!Directory.Exists(projDir))
                return Task.FromResult<IReadOnlyList<SessionSummaryEntry>>(result);
            foreach (var sessDir in Directory.EnumerateDirectories(projDir))
            {
                var sumPath = Path.Combine(sessDir, ".summary.json");
                if (!File.Exists(sumPath)) continue;
                try
                {
                    var s = JsonSerializer.Deserialize<SessionSummaryEntry>(File.ReadAllText(sumPath));
                    if (s is not null) result.Add(s);
                }
                catch { /* skip corrupted summaries */ }
            }
            return Task.FromResult<IReadOnlyList<SessionSummaryEntry>>(result);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(SessionKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var dir = SessionDir(key);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSubkeysAsync(SessionListSubkeysKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var baseDir = Path.Combine(_root, key.ProjectKey, key.SessionId);
            var result = new List<string>();
            if (!Directory.Exists(baseDir))
                return Task.FromResult<IReadOnlyList<string>>(result);

            var stack = new Stack<(string Path, string Rel)>();
            foreach (var d in Directory.EnumerateDirectories(baseDir))
                stack.Push((d, Path.GetFileName(d)));
            while (stack.Count > 0)
            {
                var (p, rel) = stack.Pop();
                if (Directory.GetFiles(p, "part-*.jsonl", SearchOption.TopDirectoryOnly).Length > 0)
                    result.Add(rel.Replace(Path.DirectorySeparatorChar, '/'));
                foreach (var child in Directory.EnumerateDirectories(p))
                    stack.Push((child, rel + Path.DirectorySeparatorChar + Path.GetFileName(child)));
            }
            return Task.FromResult<IReadOnlyList<string>>(result);
        }
    }
}
