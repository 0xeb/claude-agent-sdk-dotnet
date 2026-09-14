// Demonstrates the session-resume materialization workflow:
//   1. Seed transcript entries in an ISessionStore.
//   2. Call SessionResume.MaterializeResumeSessionAsync — the SDK writes a
//      temporary CLAUDE_CONFIG_DIR layout that the CLI can pick up.
//   3. Inspect the materialized JSONL on disk.
//   4. Clean up.
//
// .NET-only example (non-mirrored). In Python the equivalent helper lives at
// `claude_agent_sdk._internal.session_resume.materialize_resume_session`.
//
// Runs without a `claude` CLI binary on PATH — we only exercise the
// materialization step.

using Claude.AgentSdk;
using Claude.AgentSdk.Sessions;

const string Sid = "12345678-1234-1234-1234-1234567890ab";

var store = new InMemorySessionStore();
var projectKey = SessionPaths.ProjectKeyForDirectory(null);
var key = new SessionKey { ProjectKey = projectKey, SessionId = Sid };

await store.AppendAsync(key, new[]
{
    new SessionStoreEntry { Type = "user", Uuid = "u1", Timestamp = "2024-01-01T00:00:00Z" },
    new SessionStoreEntry { Type = "assistant", Uuid = "u2", Timestamp = "2024-01-01T00:00:01Z" },
});

var options = new ClaudeAgentOptions
{
    SessionStore = store,
    Resume = Sid,
};

var materialized = await Claude.AgentSdk.Sessions.SessionResume.MaterializeResumeSessionAsync(options);
if (materialized is null)
{
    Console.WriteLine("Nothing to materialize.");
    return;
}

try
{
    Console.WriteLine($"ConfigDir       : {materialized.ConfigDir}");
    Console.WriteLine($"ResumeSessionId : {materialized.ResumeSessionId}");

    var jsonl = Path.Combine(materialized.ConfigDir, "projects", projectKey, Sid + ".jsonl");
    Console.WriteLine($"JSONL exists    : {File.Exists(jsonl)}");
    Console.WriteLine("--- begin JSONL ---");
    Console.WriteLine(File.ReadAllText(jsonl));
    Console.WriteLine("--- end JSONL ---");

    await using var batcher = Claude.AgentSdk.Sessions.SessionResume.BuildMirrorBatcher(
        store, materialized, env: null,
        onError: (_, _, _) => Task.CompletedTask,
        flushMode: SessionStoreFlushMode.Eager);
    Console.WriteLine($"MirrorBatcher   : entries={batcher.MaxPendingEntries} bytes={batcher.MaxPendingBytes}");
}
finally
{
    await materialized.CleanupAsync(default);
    Console.WriteLine($"Cleaned up      : {!Directory.Exists(materialized.ConfigDir)}");
}
