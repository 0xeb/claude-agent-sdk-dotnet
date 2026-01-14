using Xunit;

namespace Claude.AgentSdk.Tests;

/// <summary>
/// Marks a test as requiring a live Claude Code CLI environment.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
#if !RUN_INTEGRATION_TESTS
        Skip = "Integration tests are disabled. Set CLAUDE_AGENT_SDK_RUN_INTEGRATION_TESTS=1 when running `dotnet test` to enable.";
#endif
    }
}

