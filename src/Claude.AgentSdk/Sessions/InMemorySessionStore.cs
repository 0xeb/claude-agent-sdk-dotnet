// Claude Agent SDK for .NET — In-memory reference implementation of ISessionStore.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_store.py
// (InMemorySessionStore @ 35)

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// In-memory <see cref="ISessionStore"/> implementation for testing and
/// development. Stores entries in a dictionary keyed by a composite
/// <c>project_key/session_id</c> string (with an optional <c>/subpath</c>
/// suffix). Not suitable for production — data is lost when the process
/// exits.
/// <para>Thread-safe: all mutators serialize on an internal lock.</para>
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<SessionStoreEntry>> _store = new();
    private readonly Dictionary<string, long> _mtimes = new();
    private readonly Dictionary<(string ProjectKey, string SessionId), SessionSummaryEntry> _summaries = new();
    private long _lastMtime;

    private static string KeyToString(SessionKey key)
    {
        return string.IsNullOrEmpty(key.Subpath)
            ? $"{key.ProjectKey}/{key.SessionId}"
            : $"{key.ProjectKey}/{key.SessionId}/{key.Subpath}";
    }

    private long NextMtime()
    {
        // Storage write time, strictly monotonic per process (mirrors Python).
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs <= _lastMtime) nowMs = _lastMtime + 1;
        _lastMtime = nowMs;
        return nowMs;
    }

    /// <inheritdoc />
    public Task AppendAsync(SessionKey key, IReadOnlyList<SessionStoreEntry> entries, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var k = KeyToString(key);
            if (!_store.TryGetValue(k, out var list))
            {
                list = new List<SessionStoreEntry>();
                _store[k] = list;
            }
            list.AddRange(entries);
            var nowMs = NextMtime();

            if (string.IsNullOrEmpty(key.Subpath))
            {
                var sk = (key.ProjectKey, key.SessionId);
                _summaries.TryGetValue(sk, out var prev);
                var folded = SessionSummary.FoldSessionSummary(prev, key, entries);
                _summaries[sk] = folded with { Mtime = nowMs };
            }
            _mtimes[k] = nowMs;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionStoreEntry>?> LoadAsync(SessionKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_store.TryGetValue(KeyToString(key), out var list))
                return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(list.ToList());
            return Task.FromResult<IReadOnlyList<SessionStoreEntry>?>(null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionStoreListEntry>> ListSessionsAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var results = new List<SessionStoreListEntry>();
            var prefix = projectKey + "/";
            foreach (var (k, _) in _store)
            {
                if (!k.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = k[prefix.Length..];
                if (rest.Contains('/')) continue; // skip subpaths
                _mtimes.TryGetValue(k, out var mt);
                results.Add(new SessionStoreListEntry(rest, mt));
            }
            return Task.FromResult<IReadOnlyList<SessionStoreListEntry>>(results);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSummaryEntry>> ListSessionSummariesAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var results = new List<SessionSummaryEntry>();
            foreach (var ((pk, _), s) in _summaries)
            {
                if (pk == projectKey) results.Add(s);
            }
            return Task.FromResult<IReadOnlyList<SessionSummaryEntry>>(results);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(SessionKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var k = KeyToString(key);
            _store.Remove(k);
            _mtimes.Remove(k);
            if (string.IsNullOrEmpty(key.Subpath))
            {
                _summaries.Remove((key.ProjectKey, key.SessionId));
                var prefix = $"{key.ProjectKey}/{key.SessionId}/";
                var toRemove = _store.Keys.Where(sk => sk.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                foreach (var sk in toRemove)
                {
                    _store.Remove(sk);
                    _mtimes.Remove(sk);
                }
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSubkeysAsync(SessionListSubkeysKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var prefix = $"{key.ProjectKey}/{key.SessionId}/";
            var results = new List<string>();
            foreach (var k in _store.Keys)
            {
                if (k.StartsWith(prefix, StringComparison.Ordinal))
                    results.Add(k[prefix.Length..]);
            }
            return Task.FromResult<IReadOnlyList<string>>(results);
        }
    }

    // ---- Test helpers ------------------------------------------------------

    /// <summary>Test helper — get a snapshot of all entries for a key.</summary>
    public IReadOnlyList<SessionStoreEntry> GetEntries(SessionKey key)
    {
        lock (_gate)
        {
            return _store.TryGetValue(KeyToString(key), out var l) ? l.ToList() : new List<SessionStoreEntry>();
        }
    }

    /// <summary>Test helper — number of stored main-transcript sessions.</summary>
    public int Size
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (var k in _store.Keys)
                {
                    var firstSlash = k.IndexOf('/');
                    if (firstSlash != -1 && k.IndexOf('/', firstSlash + 1) == -1)
                        count++;
                }
                return count;
            }
        }
    }

    /// <summary>Test helper — drop all stored data.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _store.Clear();
            _mtimes.Clear();
            _summaries.Clear();
            _lastMtime = 0;
        }
    }
}
