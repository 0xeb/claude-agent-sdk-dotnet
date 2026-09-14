// Claude Agent SDK for .NET — Pre-flight validation for SessionStore option combinations.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_store_validation.py

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Pre-flight validation for <see cref="ClaudeAgentOptions.SessionStore"/>
/// combinations. Called before subprocess spawn so misconfiguration fails
/// fast instead of surfacing as a confusing runtime error mid-session.
/// </summary>
public static class SessionStoreValidation
{
    /// <summary>
    /// Internal helper — true if <paramref name="store"/> overrides
    /// <paramref name="methodName"/> rather than inheriting the
    /// <see cref="ISessionStore"/> default that throws
    /// <see cref="NotImplementedException"/>.
    /// </summary>
    public static bool StoreImplements(ISessionStore store, string methodName)
        => TaskCompat.OverridesMethod(store, typeof(ISessionStore), methodName);

    /// <summary>
    /// Throw <see cref="ArgumentException"/> for invalid
    /// <c>SessionStore</c> option combinations. Mirrors Python
    /// <c>validate_session_store_options</c>.
    /// </summary>
    public static void Validate(ClaudeAgentOptions options)
    {
        var store = options.SessionStore;
        if (store is null) return;

        if (options.ContinueConversation
            && options.Resume is null
            && !StoreImplements(store, nameof(ISessionStore.ListSessionsAsync)))
        {
            throw new ArgumentException(
                "ContinueConversation with SessionStore requires the store to implement ListSessionsAsync()",
                nameof(options));
        }

        if (options.EnableFileCheckpointing)
        {
            throw new ArgumentException(
                "SessionStore cannot be combined with EnableFileCheckpointing "
                + "(checkpoints are local-disk only and would diverge from the mirrored transcript)",
                nameof(options));
        }
    }
}
