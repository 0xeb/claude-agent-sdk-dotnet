// Claude Agent SDK for .NET — Public conformance harness for ISessionStore adapters.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/testing/session_store_conformance.py

using System.Text.Json;
using System.Text.Json.Nodes;
using Claude.AgentSdk.Sessions;

namespace Claude.AgentSdk.Testing;

/// <summary>
/// Result of a conformance run. Inspect <see cref="Failures"/> to see which
/// individual contracts failed.
/// </summary>
public sealed class ConformanceResult
{
    /// <summary>Names of contracts that passed.</summary>
    public List<string> Passed { get; } = new();

    /// <summary>(Contract name, failure message) for each failing contract.</summary>
    public List<(string Contract, string Message)> Failures { get; } = new();

    /// <summary>Names of optional contracts skipped (either via
    /// <paramref name="skipOptional"/> in <see cref="SessionStoreConformance.RunAsync"/>
    /// or because the store does not override the optional method).</summary>
    public List<string> Skipped { get; } = new();

    /// <summary>True if no failures occurred.</summary>
    public bool Ok => Failures.Count == 0;

    /// <summary>Render a human-readable summary suitable for assertion messages.</summary>
    public override string ToString()
    {
        if (Ok) return $"OK ({Passed.Count} passed, {Skipped.Count} skipped)";
        var lines = new List<string>
        {
            $"FAIL ({Failures.Count} failed, {Passed.Count} passed, {Skipped.Count} skipped)",
        };
        foreach (var (c, m) in Failures) lines.Add($"  - {c}: {m}");
        return string.Join('\n', lines);
    }
}

/// <summary>
/// Shared conformance test suite for <see cref="ISessionStore"/> adapters.
/// Call <see cref="RunAsync"/> from an async test to assert the 14
/// behavioral contracts every adapter must satisfy. Tests for optional
/// methods (<c>list_sessions</c>, <c>list_session_summaries</c>,
/// <c>delete</c>, <c>list_subkeys</c>) are skipped when named in
/// <paramref name="skipOptional"/> or when the store does not override
/// that method.
/// </summary>
public static class SessionStoreConformance
{
    /// <summary>Optional methods exposed for selective skipping.</summary>
    public static readonly IReadOnlySet<string> OptionalMethods = new HashSet<string>
    {
        nameof(ISessionStore.ListSessionsAsync),
        nameof(ISessionStore.ListSessionSummariesAsync),
        nameof(ISessionStore.DeleteAsync),
        nameof(ISessionStore.ListSubkeysAsync),
    };

    private static readonly SessionKey Key = new()
    {
        ProjectKey = "proj",
        SessionId = "sess",
    };

    /// <summary>
    /// Run the conformance suite. <paramref name="makeStore"/> is invoked
    /// once per contract to provide isolation. Returns a
    /// <see cref="ConformanceResult"/> describing pass/fail/skip status.
    /// </summary>
    /// <param name="makeStore">Async factory producing a fresh store per contract.</param>
    /// <param name="skipOptional">Optional method names to skip entirely.</param>
    public static async Task<ConformanceResult> RunAsync(
        Func<Task<ISessionStore>> makeStore,
        ISet<string>? skipOptional = null,
        CancellationToken cancellationToken = default)
    {
        skipOptional ??= new HashSet<string>();
        foreach (var s in skipOptional)
        {
            if (!OptionalMethods.Contains(s))
                throw new ArgumentException($"Unknown optional method in skipOptional: {s}", nameof(skipOptional));
        }
        var result = new ConformanceResult();

        var probe = await makeStore().ConfigureAwait(false);
        var hasListSessions = HasOptional(probe, nameof(ISessionStore.ListSessionsAsync), skipOptional);
        var hasListSummaries = HasOptional(probe, nameof(ISessionStore.ListSessionSummariesAsync), skipOptional);
        var hasDelete = HasOptional(probe, nameof(ISessionStore.DeleteAsync), skipOptional);
        var hasListSubkeys = HasOptional(probe, nameof(ISessionStore.ListSubkeysAsync), skipOptional);

        async Task Run(string name, Func<Task> body)
        {
            try { await body().ConfigureAwait(false); result.Passed.Add(name); }
            catch (Exception ex) { result.Failures.Add((name, ex.Message)); }
        }

        void Skip(string name) => result.Skipped.Add(name);

        // 1. append → load returns same entries in same order
        await Run("01_append_then_load", async () =>
        {
            var store = await makeStore().ConfigureAwait(false);
            await store.AppendAsync(Key, new[] { E("b", 1), E("a", 2) }, cancellationToken).ConfigureAwait(false);
            var loaded = await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false);
            AssertEntries(new[] { E("b", 1), E("a", 2) }, loaded, "append/load mismatch");
        }).ConfigureAwait(false);

        // 2. load unknown key returns null
        await Run("02_load_unknown_returns_null", async () =>
        {
            var store = await makeStore().ConfigureAwait(false);
            var miss = await store.LoadAsync(new SessionKey { ProjectKey = "proj", SessionId = "nope" }, cancellationToken).ConfigureAwait(false);
            if (miss is not null) throw new Exception("expected null for unknown session_id");
            await store.AppendAsync(Key, new[] { E("x", 1) }, cancellationToken).ConfigureAwait(false);
            var miss2 = await store.LoadAsync(Key with { Subpath = "nope" }, cancellationToken).ConfigureAwait(false);
            if (miss2 is not null) throw new Exception("expected null for unknown subpath");
        }).ConfigureAwait(false);

        // 3. multiple append calls preserve order
        await Run("03_multiple_appends_preserve_order", async () =>
        {
            var store = await makeStore().ConfigureAwait(false);
            await store.AppendAsync(Key, new[] { E("z", 1) }, cancellationToken).ConfigureAwait(false);
            await store.AppendAsync(Key, new[] { E("a", 2), E("m", 3) }, cancellationToken).ConfigureAwait(false);
            await store.AppendAsync(Key, new[] { E("b", 4) }, cancellationToken).ConfigureAwait(false);
            var loaded = await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false);
            AssertEntries(new[] { E("z", 1), E("a", 2), E("m", 3), E("b", 4) }, loaded, "order mismatch");
        }).ConfigureAwait(false);

        // 4. append([]) is a no-op
        await Run("04_empty_append_noop", async () =>
        {
            var store = await makeStore().ConfigureAwait(false);
            await store.AppendAsync(Key, new[] { E("a", 1) }, cancellationToken).ConfigureAwait(false);
            await store.AppendAsync(Key, Array.Empty<SessionStoreEntry>(), cancellationToken).ConfigureAwait(false);
            var loaded = await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false);
            AssertEntries(new[] { E("a", 1) }, loaded, "empty append disturbed transcript");
        }).ConfigureAwait(false);

        // 5. subpath keys stored independently
        await Run("05_subpath_independent", async () =>
        {
            var store = await makeStore().ConfigureAwait(false);
            var sub = Key with { Subpath = "subagents/agent-1" };
            await store.AppendAsync(Key, new[] { Em("m", 1) }, cancellationToken).ConfigureAwait(false);
            await store.AppendAsync(sub, new[] { Em("s", 1) }, cancellationToken).ConfigureAwait(false);
            AssertEntries(new[] { Em("m", 1) }, await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false), "main contaminated");
            AssertEntries(new[] { Em("s", 1) }, await store.LoadAsync(sub, cancellationToken).ConfigureAwait(false), "sub contaminated");
        }).ConfigureAwait(false);

        // 6. project_key isolation
        await Run("06_project_isolation", async () =>
        {
            var store = await makeStore().ConfigureAwait(false);
            await store.AppendAsync(new SessionKey { ProjectKey = "A", SessionId = "s1" }, new[] { Ek("from", "A") }, cancellationToken).ConfigureAwait(false);
            await store.AppendAsync(new SessionKey { ProjectKey = "B", SessionId = "s1" }, new[] { Ek("from", "B") }, cancellationToken).ConfigureAwait(false);
            AssertEntries(new[] { Ek("from", "A") }, await store.LoadAsync(new SessionKey { ProjectKey = "A", SessionId = "s1" }, cancellationToken).ConfigureAwait(false), "A bled into B");
            AssertEntries(new[] { Ek("from", "B") }, await store.LoadAsync(new SessionKey { ProjectKey = "B", SessionId = "s1" }, cancellationToken).ConfigureAwait(false), "B bled into A");
            if (hasListSessions)
            {
                if ((await store.ListSessionsAsync("A", cancellationToken).ConfigureAwait(false)).Count != 1) throw new Exception("project A list != 1");
                if ((await store.ListSessionsAsync("B", cancellationToken).ConfigureAwait(false)).Count != 1) throw new Exception("project B list != 1");
            }
        }).ConfigureAwait(false);

        if (hasListSessions)
        {
            await Run("07_list_sessions_returns_for_project", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                await store.AppendAsync(new SessionKey { ProjectKey = "proj", SessionId = "a" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(new SessionKey { ProjectKey = "proj", SessionId = "b" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(new SessionKey { ProjectKey = "other", SessionId = "c" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                var sessions = (await store.ListSessionsAsync("proj", cancellationToken).ConfigureAwait(false)).OrderBy(s => s.SessionId).ToList();
                if (sessions.Count != 2 || sessions[0].SessionId != "a" || sessions[1].SessionId != "b") throw new Exception("list_sessions returned wrong IDs");
                foreach (var s in sessions)
                    if (s.Mtime <= 1_000_000_000_000L) throw new Exception($"mtime {s.Mtime} not epoch-ms");
                var empty = await store.ListSessionsAsync("never-appended-project", cancellationToken).ConfigureAwait(false);
                if (empty.Count != 0) throw new Exception("expected empty list for unknown project");
            }).ConfigureAwait(false);

            await Run("08_list_sessions_excludes_subagents", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                await store.AppendAsync(new SessionKey { ProjectKey = "proj", SessionId = "main" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(new SessionKey { ProjectKey = "proj", SessionId = "main", Subpath = "subagents/agent-1" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                var sessions = await store.ListSessionsAsync("proj", cancellationToken).ConfigureAwait(false);
                if (sessions.Count != 1 || sessions[0].SessionId != "main") throw new Exception("subagent leaked into list_sessions");
            }).ConfigureAwait(false);
        }
        else
        {
            Skip("07_list_sessions_returns_for_project");
            Skip("08_list_sessions_excludes_subagents");
        }

        if (hasListSummaries)
        {
            await Run("14_list_session_summaries_round_trips", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                var k = new SessionKey { ProjectKey = "proj", SessionId = "summ-sess" };
                await store.AppendAsync(k, new[]
                {
                    EtCustom("2024-01-01T00:00:00.000Z", "first"),
                    EtOnly("2024-01-01T00:00:01.000Z"),
                }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(k, new[] { EtCustom("2024-01-01T00:00:02.000Z", "second") }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(new SessionKey { ProjectKey = "other", SessionId = "elsewhere" },
                    new[] { EtOnly("2024-01-01T00:00:00.000Z") }, cancellationToken).ConfigureAwait(false);

                var summaries = await store.ListSessionSummariesAsync("proj", cancellationToken).ConfigureAwait(false);
                var byId = summaries.ToDictionary(s => s.SessionId);
                if (!byId.ContainsKey("summ-sess") || byId.Count != 1) throw new Exception("summaries wrong session set");
                var summ = byId["summ-sess"];
                if (summ.Mtime <= 1_000_000_000_000L) throw new Exception($"summary mtime {summ.Mtime} not epoch-ms");

                if (hasListSessions)
                {
                    var ls = (await store.ListSessionsAsync("proj", cancellationToken).ConfigureAwait(false)).ToDictionary(e => e.SessionId, e => e.Mtime);
                    if (summ.Mtime < ls["summ-sess"]) throw new Exception("summary mtime < list_sessions mtime");
                }

                // data is opaque; verify it round-trips into fold_session_summary.
                if (summ.Data.ValueKind != JsonValueKind.Object) throw new Exception("summary data not an object");
                var refolded = SessionSummary.FoldSessionSummary(summ, k, new[] { EtOnly("2024-01-01T00:00:03.000Z") });
                if (refolded.SessionId != "summ-sess") throw new Exception("refold session_id mismatch");
                if (refolded.Mtime != summ.Mtime) throw new Exception("fold must not change mtime");

                // Subagent appends must NOT affect the main summary.
                await store.AppendAsync(k with { Subpath = "subagents/agent-1" },
                    new[] { EtCustom("2024-01-01T00:00:09.000Z", "subagent") }, cancellationToken).ConfigureAwait(false);
                var after = (await store.ListSessionSummariesAsync("proj", cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.SessionId);
                if (after["summ-sess"].Data.GetRawText() != summ.Data.GetRawText())
                    throw new Exception("subagent append leaked into main summary");

                var none = await store.ListSessionSummariesAsync("never-appended-project", cancellationToken).ConfigureAwait(false);
                if (none.Count != 0) throw new Exception("expected empty summaries for unknown project");

                if (hasDelete)
                {
                    await store.DeleteAsync(k, cancellationToken).ConfigureAwait(false);
                    var post = await store.ListSessionSummariesAsync("proj", cancellationToken).ConfigureAwait(false);
                    if (post.Count != 0) throw new Exception("delete did not remove summary");
                }
            }).ConfigureAwait(false);
        }
        else
        {
            Skip("14_list_session_summaries_round_trips");
        }

        if (hasDelete)
        {
            await Run("09_delete_main_returns_null", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                await store.DeleteAsync(new SessionKey { ProjectKey = "proj", SessionId = "never-written" }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(Key, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.DeleteAsync(Key, cancellationToken).ConfigureAwait(false);
                if (await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false) is not null) throw new Exception("delete failed");
            }).ConfigureAwait(false);

            await Run("10_delete_main_cascades", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                var sub1 = Key with { Subpath = "subagents/agent-1" };
                var sub2 = Key with { Subpath = "subagents/agent-2" };
                var other = new SessionKey { ProjectKey = "proj", SessionId = "sess2" };
                var otherProj = new SessionKey { ProjectKey = "other-proj", SessionId = Key.SessionId };
                await store.AppendAsync(Key, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(sub1, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(sub2, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(other, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(otherProj, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);

                await store.DeleteAsync(Key, cancellationToken).ConfigureAwait(false);

                if (await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false) is not null) throw new Exception("main not deleted");
                if (await store.LoadAsync(sub1, cancellationToken).ConfigureAwait(false) is not null) throw new Exception("sub1 not cascaded");
                if (await store.LoadAsync(sub2, cancellationToken).ConfigureAwait(false) is not null) throw new Exception("sub2 not cascaded");
                var lo = await store.LoadAsync(other, cancellationToken).ConfigureAwait(false);
                if (lo is null || lo.Count != 1) throw new Exception("same-project sibling collateral damage");
                var lop = await store.LoadAsync(otherProj, cancellationToken).ConfigureAwait(false);
                if (lop is null || lop.Count != 1) throw new Exception("other-project session collateral damage");

                if (hasListSubkeys)
                {
                    var sk = await store.ListSubkeysAsync(new SessionListSubkeysKey(Key.ProjectKey, Key.SessionId), cancellationToken).ConfigureAwait(false);
                    if (sk.Count != 0) throw new Exception("subkeys not cleared");
                }
            }).ConfigureAwait(false);

            await Run("11_delete_subpath_only_removes_subkey", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                var sub1 = Key with { Subpath = "subagents/agent-1" };
                var sub2 = Key with { Subpath = "subagents/agent-2" };
                await store.AppendAsync(Key, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(sub1, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(sub2, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);

                await store.DeleteAsync(sub1, cancellationToken).ConfigureAwait(false);

                if (await store.LoadAsync(sub1, cancellationToken).ConfigureAwait(false) is not null) throw new Exception("sub1 not deleted");
                var ls2 = await store.LoadAsync(sub2, cancellationToken).ConfigureAwait(false);
                if (ls2 is null || ls2.Count != 1) throw new Exception("sub2 collateral");
                var lm = await store.LoadAsync(Key, cancellationToken).ConfigureAwait(false);
                if (lm is null || lm.Count != 1) throw new Exception("main collateral");
                if (hasListSubkeys)
                {
                    var sk = (await store.ListSubkeysAsync(new SessionListSubkeysKey(Key.ProjectKey, Key.SessionId), cancellationToken).ConfigureAwait(false)).ToList();
                    if (sk.Count != 1 || sk[0] != "subagents/agent-2") throw new Exception("subkey list wrong after partial delete");
                }
            }).ConfigureAwait(false);
        }
        else
        {
            Skip("09_delete_main_returns_null");
            Skip("10_delete_main_cascades");
            Skip("11_delete_subpath_only_removes_subkey");
        }

        if (hasListSubkeys)
        {
            await Run("12_list_subkeys_returns_subpaths", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                await store.AppendAsync(Key, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(Key with { Subpath = "subagents/agent-1" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(Key with { Subpath = "subagents/agent-2" }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(new SessionKey
                {
                    ProjectKey = Key.ProjectKey,
                    SessionId = "other-sess",
                    Subpath = "subagents/agent-x",
                }, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);

                var sk = (await store.ListSubkeysAsync(new SessionListSubkeysKey(Key.ProjectKey, Key.SessionId), cancellationToken).ConfigureAwait(false)).OrderBy(s => s, StringComparer.Ordinal).ToList();
                if (sk.Count != 2 || sk[0] != "subagents/agent-1" || sk[1] != "subagents/agent-2") throw new Exception("subkey list wrong");
                if (sk.Contains("subagents/agent-x")) throw new Exception("other-session subkey leaked");
            }).ConfigureAwait(false);

            await Run("13_list_subkeys_excludes_main", async () =>
            {
                var store = await makeStore().ConfigureAwait(false);
                await store.AppendAsync(Key, new[] { Em("n", 1) }, cancellationToken).ConfigureAwait(false);
                var sk = await store.ListSubkeysAsync(new SessionListSubkeysKey(Key.ProjectKey, Key.SessionId), cancellationToken).ConfigureAwait(false);
                if (sk.Count != 0) throw new Exception("main appeared in subkey list");
                var sk2 = await store.ListSubkeysAsync(new SessionListSubkeysKey("proj", "never-appended"), cancellationToken).ConfigureAwait(false);
                if (sk2.Count != 0) throw new Exception("subkey list nonempty for unknown session");
            }).ConfigureAwait(false);
        }
        else
        {
            Skip("12_list_subkeys_returns_subpaths");
            Skip("13_list_subkeys_excludes_main");
        }

        return result;
    }

    private static bool HasOptional(ISessionStore store, string method, ISet<string> skipOptional)
    {
        if (skipOptional.Contains(method)) return false;
        return SessionStoreValidation.StoreImplements(store, method);
    }

    private static SessionStoreEntry E(string uuid, int n)
        => new()
        {
            Type = "x",
            Uuid = uuid,
            Extras = new Dictionary<string, JsonElement>
            {
                ["n"] = JsonDocument.Parse(n.ToString()).RootElement.Clone(),
            },
        };

    private static SessionStoreEntry Em(string field, int v)
        => new()
        {
            Type = "x",
            Extras = new Dictionary<string, JsonElement>
            {
                [field] = JsonDocument.Parse(v.ToString()).RootElement.Clone(),
            },
        };

    private static SessionStoreEntry Ek(string field, string v)
        => new()
        {
            Type = "x",
            Extras = new Dictionary<string, JsonElement>
            {
                [field] = JsonDocument.Parse(JsonSerializer.Serialize(v)).RootElement.Clone(),
            },
        };

    private static SessionStoreEntry EtOnly(string timestamp)
        => new() { Type = "x", Timestamp = timestamp };

    private static SessionStoreEntry EtCustom(string timestamp, string customTitle)
        => new()
        {
            Type = "x",
            Timestamp = timestamp,
            Extras = new Dictionary<string, JsonElement>
            {
                ["customTitle"] = JsonDocument.Parse(JsonSerializer.Serialize(customTitle)).RootElement.Clone(),
            },
        };

    private static void AssertEntries(IReadOnlyList<SessionStoreEntry> expected, IReadOnlyList<SessionStoreEntry>? actual, string message)
    {
        if (actual is null) throw new Exception(message + " (actual is null)");
        if (actual.Count != expected.Count) throw new Exception($"{message} (count {actual.Count} != {expected.Count})");
        for (int i = 0; i < expected.Count; i++)
        {
            var e = SessionSummary.EntryToJsonObject(expected[i]);
            var a = SessionSummary.EntryToJsonObject(actual[i]);
            if (e.ToJsonString() != a.ToJsonString())
                throw new Exception($"{message} [#{i}]: expected={e.ToJsonString()} actual={a.ToJsonString()}");
        }
    }
}
