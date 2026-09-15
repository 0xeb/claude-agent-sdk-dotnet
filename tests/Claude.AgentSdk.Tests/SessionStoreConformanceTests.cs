// Runs the shipped SessionStore conformance suite against the shipped stores.
//
// Claude.AgentSdk.Testing.SessionStoreConformance has been part of the public API
// since v0.2.82 so third-party ISessionStore implementations can verify themselves
// against the same contracts the SDK's own stores must satisfy. Nothing invoked it
// -- the harness shipped, and both FileSessionStore and InMemorySessionStore went
// out with no coverage at all.
//
// RunAsync calls the factory once per contract, so contracts cannot leak state
// into one another.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Claude.AgentSdk;
using Claude.AgentSdk.Sessions;
using Claude.AgentSdk.Testing;
using Xunit;

namespace Claude.AgentSdk.Tests;

public class SessionStoreConformanceTests
{
    /// Report every failing contract by name. Asserting on a bare count would say
    /// "3 != 0" and leave you to guess which contracts broke.
    private static void AssertAllContractsPass(ConformanceResult result, string storeName)
    {
        Assert.True(
            result.Failures.Count == 0,
            $"{storeName}: {result.Failures.Count} contract(s) failed:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.Failures.ConvertAll(f => $"  {f.Contract}: {f.Message}")));

        // Guards against a harness that silently exercises nothing -- an
        // all-skipped result would otherwise satisfy the assertion above.
        Assert.True(result.Passed.Count > 0, $"{storeName}: no contract actually ran");
    }

    /// Unique directory per store instance, so FileSessionStore contracts are
    /// isolated from each other and from previous runs.
    private static string FreshStoreRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "claude_sdk_conformance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task InMemorySessionStore_SatisfiesAllContracts()
    {
        var result = await SessionStoreConformance.RunAsync(
            () => Task.FromResult<ISessionStore>(new InMemorySessionStore()));

        AssertAllContractsPass(result, nameof(InMemorySessionStore));
    }

    [Fact]
    public async Task FileSessionStore_SatisfiesAllContracts()
    {
        var result = await SessionStoreConformance.RunAsync(
            () => Task.FromResult<ISessionStore>(new FileSessionStore(FreshStoreRoot())));

        AssertAllContractsPass(result, nameof(FileSessionStore));
    }

    // The harness has never been observed failing, so this pins the shape of its
    // output: a runner that reported success unconditionally would satisfy both
    // tests above and prove nothing.
    [Fact]
    public async Task Harness_ReportsPerContractResults()
    {
        var result = await SessionStoreConformance.RunAsync(
            () => Task.FromResult<ISessionStore>(new InMemorySessionStore()));

        Assert.NotEmpty(result.Passed);
        Assert.All(result.Passed, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.All(result.Skipped, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    // skipOptional is validated rather than ignored, so a typo in a caller's skip
    // list fails loudly instead of silently disabling a contract.
    [Fact]
    public async Task Harness_RejectsUnknownOptionalMethodName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            SessionStoreConformance.RunAsync(
                () => Task.FromResult<ISessionStore>(new InMemorySessionStore()),
                new HashSet<string> { "NoSuchMethodAsync" }));
    }
}
