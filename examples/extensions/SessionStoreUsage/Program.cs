// Demonstrates the top-level Claude.AgentSdk.ClaudeSessions free-function
// surface against an InMemorySessionStore. No CLI subprocess required.
//
// .NET-only example (non-mirrored). The Python SDK exposes the equivalent
// surface as module-level functions: `list_sessions_from_store`,
// `get_session_messages_from_store`, `rename_session_via_store`,
// `tag_session_via_store`, `delete_session_via_store`.

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

Console.WriteLine("=== ListSessionsAsync ===");
foreach (var info in await ClaudeSessions.ListSessionsAsync(store))
{
    var mtime = DateTimeOffset.FromUnixTimeMilliseconds(info.LastModified);
    Console.WriteLine($"  session_id={info.SessionId} mtime={mtime:o} summary='{info.Summary}'");
}

Console.WriteLine("\n=== GetSessionInfoAsync ===");
var single = await ClaudeSessions.GetSessionInfoAsync(store, Sid);
Console.WriteLine(single is null ? "  (not found)" : $"  {single.SessionId} title={single.CustomTitle ?? "(none)"}");

Console.WriteLine("\n=== RenameSessionAsync ===");
await ClaudeSessions.RenameSessionAsync(store, Sid, "Custom title");
single = await ClaudeSessions.GetSessionInfoAsync(store, Sid);
Console.WriteLine($"  title={single?.CustomTitle}");

Console.WriteLine("\n=== TagSessionAsync ===");
await ClaudeSessions.TagSessionAsync(store, Sid, "demo");
single = await ClaudeSessions.GetSessionInfoAsync(store, Sid);
Console.WriteLine($"  tag={single?.Tag}");

Console.WriteLine("\n=== GetSessionMessagesAsync ===");
var msgs = await ClaudeSessions.GetSessionMessagesAsync(store, Sid);
foreach (var m in msgs) Console.WriteLine($"  {m.Type} uuid={m.Uuid}");

Console.WriteLine("\n=== DeleteSessionAsync ===");
await ClaudeSessions.DeleteSessionAsync(store, Sid);
Console.WriteLine($"  remaining sessions: {(await ClaudeSessions.ListSessionsAsync(store)).Count}");
