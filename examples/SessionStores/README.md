# SessionStore reference adapters (.NET)

> Mirrors `reference/claude-agent-sdk-python/examples/session_stores/`.

Reference [`ISessionStore`](../../src/Claude.AgentSdk/Types.Sessions.cs)
implementations — copy into your project, install any backend client, and
validate with [`SessionStoreConformance.RunAsync`](../../src/Claude.AgentSdk/Testing/SessionStoreConformance.cs).

These adapters live in `examples/` (not `src/`) so the SDK package stays free
of heavyweight optional dependencies. They are exercised by the test suite to
prove the `ISessionStore` protocol generalizes beyond the in-memory and
on-disk defaults.

## Validating your own adapter

When you write a new adapter, assert it satisfies the protocol's behavioral
contracts with the shipped conformance harness:

```csharp
using Claude.AgentSdk.Sessions;
using Claude.AgentSdk.Testing;
using Xunit;

[Fact]
public async Task MyStore_passes_conformance()
{
    var result = await SessionStoreConformance.RunAsync(
        () => Task.FromResult<ISessionStore>(new MyStore(/* ... */)));
    Assert.True(result.Ok, result.ToString());
}
```

## What ships in this folder

| Project                     | Purpose                                                                                                           |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `JsonlPartFileStore/`       | Reference adapter that mirrors the S3 "part-files" pattern using a local directory. Smallest possible example.    |

The Python sibling folder ships full S3 (`boto3`), Redis (`redis-py`), and
Postgres (`asyncpg`) reference adapters. The .NET surface keeps a single
file-based reference adapter; production users should follow the same pattern
with their preferred .NET SDK (AWS SDK for .NET, StackExchange.Redis, Npgsql,
etc.).

## Production checklist

- `SessionStoreConformance.RunAsync` proves *correctness*, not *resilience* —
  load-test your adapter under your expected throughput.
- `AppendAsync` failures bubble up through the `MirrorErrorCallback` you pass
  to the SDK; never block the conversation. Monitor for these so silent mirror
  gaps don't go unnoticed.
- Implement retention either through the backend's TTL/lifecycle features or
  a scheduled sweep. The SDK never auto-deletes.
