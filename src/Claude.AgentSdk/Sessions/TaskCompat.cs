// Claude Agent SDK for .NET — async/cancellation primitives.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/_task_compat.py
// (Python uses asyncio/trio; .NET uses Task + CancellationToken natively.)

using System.Diagnostics.CodeAnalysis;

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Helpers that approximate the Python <c>_task_compat</c> utilities used by
/// the sessions subsystem. .NET's <see cref="Task"/> + <see cref="CancellationToken"/>
/// already provides the equivalent of <c>spawn_detached</c> via
/// <see cref="Task.Run(Func{Task})"/>; this class only adds the timeout helper
/// that <c>session_resume._with_timeout</c> relies on.
/// </summary>
internal static class TaskCompat
{
    /// <summary>
    /// Await <paramref name="work"/> with a millisecond timeout, wrapping
    /// timeouts and exceptions as <see cref="SessionStoreOperationException"/>
    /// with descriptive context. Mirrors session_resume.py
    /// <c>_with_timeout</c>.
    /// </summary>
    public static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> work,
        TimeSpan timeout,
        string what,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            return await work(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SessionStoreOperationException(
                $"{what} timed out after {(int)timeout.TotalMilliseconds}ms during resume materialization");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not SessionStoreOperationException)
        {
            throw new SessionStoreOperationException(
                $"{what} failed during resume materialization: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Suppress only OperationCanceledException originating from
    /// <paramref name="token"/>; rethrow other cancellations.
    /// </summary>
    public static bool ShouldSwallowCancellation(OperationCanceledException ex, CancellationToken token)
        => token.IsCancellationRequested && ex.CancellationToken == token;

    /// <summary>
    /// Reflection check: does <paramref name="instance"/>'s runtime type
    /// override <paramref name="methodName"/> from <paramref name="declaringType"/>?
    /// Mirrors Python's <c>_store_implements</c> default-interface detection.
    /// </summary>
    public static bool OverridesMethod(
        object instance,
        Type declaringType,
        string methodName)
    {
        var t = instance.GetType();
        var iface = declaringType;
        if (!iface.IsAssignableFrom(t)) return false;
        try
        {
            var map = t.GetInterfaceMap(iface);
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name == methodName)
                {
                    // Default interface method: TargetMethods[i] == InterfaceMethods[i]
                    // and DeclaringType is the interface itself.
                    var target = map.TargetMethods[i];
                    return target != null && target.DeclaringType != iface;
                }
            }
        }
        catch (ArgumentException)
        {
            // Type does not implement the interface explicitly — fall through.
        }
        return false;
    }
}
