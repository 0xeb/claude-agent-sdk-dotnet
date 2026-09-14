// Claude Agent SDK for .NET — Session subsystem errors.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/sessions.py,
// session_mutations.py, session_resume.py

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Raised by session helpers when a session ID is malformed (must be a UUID).
/// Mirrors Python's <c>ValueError("Invalid session_id: ...")</c> from
/// <c>_validate_uuid</c> call sites.
/// </summary>
public sealed class InvalidSessionIdException : ClaudeSDKException
{
    public string SessionId { get; }

    public InvalidSessionIdException(string sessionId)
        : base($"Invalid session_id: {sessionId}")
    {
        SessionId = sessionId;
    }
}

/// <summary>
/// Raised when a session transcript cannot be located on disk or in the store.
/// Mirrors Python's <c>FileNotFoundError("Session ... not found")</c>.
/// </summary>
public sealed class SessionNotFoundException : ClaudeSDKException
{
    public string SessionId { get; }

    public SessionNotFoundException(string sessionId)
        : base($"Session {sessionId} not found")
    {
        SessionId = sessionId;
    }

    public SessionNotFoundException(string sessionId, string message)
        : base(message)
    {
        SessionId = sessionId;
    }
}

/// <summary>
/// Wraps an adapter failure or timeout encountered while materializing a
/// resume from a <see cref="ISessionStore"/>. Reference: session_resume.py
/// <c>_with_timeout</c>.
/// </summary>
public sealed class SessionStoreOperationException : ClaudeSDKException
{
    public SessionStoreOperationException(string message)
        : base(message)
    {
    }

    public SessionStoreOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
