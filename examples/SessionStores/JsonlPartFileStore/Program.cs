// Runs the conformance harness against the JSONL part-file reference adapter.
// Mirrors what `pytest tests/test_example_s3_session_store.py` does for the
// Python S3 reference adapter — proves the adapter satisfies every behavioral
// contract.

using Claude.AgentSdk;
using Claude.AgentSdk.Examples.SessionStores.JsonlPartFileStore;
using Claude.AgentSdk.Sessions;
using Claude.AgentSdk.Testing;

var root = Path.Combine(Path.GetTempPath(), "jsonl-partfile-demo-" + Guid.NewGuid().ToString("N"));
Console.WriteLine($"Conformance run against: {root}");

try
{
    var result = await SessionStoreConformance.RunAsync(() =>
    {
        var sub = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sub);
        return Task.FromResult<ISessionStore>(new JsonlPartFileSessionStore(sub));
    });
    Console.WriteLine(result.ToString());
    if (!result.Ok)
    {
        Environment.ExitCode = 1;
    }
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
}
