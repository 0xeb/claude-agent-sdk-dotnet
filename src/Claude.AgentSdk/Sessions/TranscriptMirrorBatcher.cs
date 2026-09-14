// Claude Agent SDK for .NET — TranscriptMirrorBatcher.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/transcript_mirror_batcher.py

using System.Text.Json;
using System.Threading.Channels;

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Delegate invoked when a batch flush fails after all retries. The
/// <paramref name="key"/> argument may be <c>null</c> if the file path could
/// not be mapped to a <see cref="SessionKey"/>. Never raises.
/// </summary>
public delegate Task MirrorErrorCallback(SessionKey? key, string message, CancellationToken cancellationToken);

/// <summary>
/// Accumulates <c>transcript_mirror</c> frames and flushes them to a
/// <see cref="ISessionStore"/>. <see cref="Enqueue"/> is fire-and-forget;
/// <see cref="FlushAsync"/> is asynchronous. The pending queue is bounded —
/// when it exceeds <see cref="MaxPendingEntries"/> or
/// <see cref="MaxPendingBytes"/>, an eager flush fires in the background.
/// <para>Adapter failures are retried (<see cref="MirrorAppendMaxAttempts"/>
/// attempts) with short backoff; timeouts are not retried. Only after the
/// final attempt fails is the batch dropped and reported via
/// <c>OnError</c>. Failures never raise — the local-disk transcript is
/// already durable so the session must continue unaffected.</para>
/// </summary>
public sealed class TranscriptMirrorBatcher : IAsyncDisposable
{
    /// <summary>Default eager-flush threshold (entry count).</summary>
    public const int DefaultMaxPendingEntries = 500;
    /// <summary>Default eager-flush threshold (bytes).</summary>
    public const int DefaultMaxPendingBytes = 1 << 20;
    /// <summary>Default per-append timeout.</summary>
    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Total attempts (including the first) for a batch.</summary>
    public const int MirrorAppendMaxAttempts = 3;

    /// <summary>Backoff delays between attempts. Length == MAX_ATTEMPTS - 1.</summary>
    public static readonly TimeSpan[] MirrorAppendBackoff =
    {
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(800),
    };

    private readonly ISessionStore _store;
    private readonly string _projectsDir;
    private readonly MirrorErrorCallback _onError;
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    private record struct MirrorEntry(string FilePath, IReadOnlyList<SessionStoreEntry> Entries, int Bytes);

    private readonly object _gate = new();
    private List<MirrorEntry> _pending = new();
    private int _pendingEntries;
    private int _pendingBytes;
    private Task? _flushTask;
    private int _disposed;

    /// <summary>Per-append timeout (default 60s).</summary>
    public TimeSpan SendTimeout { get; init; } = DefaultSendTimeout;

    /// <summary>Eager-flush threshold (entry count). Zero ⇒ eager mode.</summary>
    public int MaxPendingEntries { get; init; } = DefaultMaxPendingEntries;

    /// <summary>Eager-flush threshold (bytes). Zero ⇒ eager mode.</summary>
    public int MaxPendingBytes { get; init; } = DefaultMaxPendingBytes;

    /// <summary>Construct a batcher.</summary>
    /// <param name="store">Destination session store.</param>
    /// <param name="projectsDir">Resolves transcript file paths to
    /// <see cref="SessionKey"/>s via
    /// <see cref="SessionPaths.FilePathToSessionKey"/>.</param>
    /// <param name="onError">Invoked with retry-exhausted batches. Never
    /// raises from the batcher's perspective.</param>
    public TranscriptMirrorBatcher(ISessionStore store, string projectsDir, MirrorErrorCallback onError)
    {
        _store = store;
        _projectsDir = projectsDir;
        _onError = onError;
    }

    /// <summary>
    /// Buffer a frame; schedule an eager flush if thresholds are exceeded.
    /// </summary>
    public void Enqueue(string filePath, IReadOnlyList<SessionStoreEntry> entries)
    {
        if (entries.Count == 0) return;
        int size = ApproxJsonSize(entries);
        bool overflow;
        lock (_gate)
        {
            _pending.Add(new MirrorEntry(filePath, entries, size));
            _pendingEntries += entries.Count;
            _pendingBytes += size;
            overflow = _pendingEntries > MaxPendingEntries || _pendingBytes > MaxPendingBytes;
        }
        if (overflow)
        {
            // Fire-and-forget; the lock inside DrainAsync serializes against any in-flight flush.
            _flushTask = Task.Run(() => DrainAsync());
        }
    }

    /// <summary>Flush all pending entries. Awaits any in-flight eager flush first.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var t = Task.Run(() => DrainAsync(cancellationToken));
        _flushTask = t;
        await t.ConfigureAwait(false);
        if (ReferenceEquals(_flushTask, t)) _flushTask = null;
    }

    /// <summary>Final flush before teardown. Never raises.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        try { await FlushAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Cancellation during shutdown is expected — drop without noise (commit 9d2c650).
        }
        catch
        {
            // Defensive: FlushAsync already wraps everything.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await CloseAsync().ConfigureAwait(false);
        _flushLock.Dispose();
    }

    private async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        List<MirrorEntry> items;
        lock (_gate)
        {
            items = _pending;
            _pending = new List<MirrorEntry>();
            _pendingEntries = 0;
            _pendingBytes = 0;
        }
        if (items.Count == 0) return;

        var errors = new List<(SessionKey Key, string Message)>();
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DoFlushAsync(items, errors, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Quiet on shutdown cancellation (commit 9d2c650).
            return;
        }
        catch
        {
            // DoFlushAsync already wraps store.AppendAsync; this guards any
            // remaining unguarded path so the "never raises" contract holds.
            return;
        }
        finally
        {
            _flushLock.Release();
        }

        foreach (var (key, msg) in errors)
        {
            try { await _onError(key, msg, cancellationToken).ConfigureAwait(false); }
            catch { /* defensive — never raise */ }
        }
    }

    private async Task DoFlushAsync(
        List<MirrorEntry> items,
        List<(SessionKey, string)> errors,
        CancellationToken cancellationToken)
    {
        // Coalesce by file_path so each unique file gets one append per flush.
        var byPath = new Dictionary<string, List<SessionStoreEntry>>();
        var pathOrder = new List<string>();
        foreach (var item in items)
        {
            if (!byPath.TryGetValue(item.FilePath, out var bucket))
            {
                bucket = new List<SessionStoreEntry>();
                byPath[item.FilePath] = bucket;
                pathOrder.Add(item.FilePath);
            }
            bucket.AddRange(item.Entries);
        }

        foreach (var filePath in pathOrder)
        {
            var entries = byPath[filePath];
            if (entries.Count == 0) continue;

            var key = SessionPaths.FilePathToSessionKey(filePath, _projectsDir);
            if (key is null)
            {
                // filePath not under projects dir — drop silently (Python logs a warning here).
                continue;
            }

            Exception? lastErr = null;
            bool succeeded = false;
            for (int attempt = 0; attempt < MirrorAppendMaxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    try { await Task.Delay(MirrorAppendBackoff[attempt - 1], cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(SendTimeout);
                    await _store.AppendAsync(key, entries, cts.Token).ConfigureAwait(false);
                    succeeded = true;
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException ex)
                {
                    // Timeout — do not retry (in-flight call may still land).
                    lastErr = ex;
                    break;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                }
            }
            if (!succeeded)
            {
                errors.Add((key, lastErr?.Message ?? "unknown error"));
            }
        }
    }

    private static int ApproxJsonSize(IReadOnlyList<SessionStoreEntry> entries)
    {
        try
        {
            return JsonSerializer.Serialize(entries).Length;
        }
        catch
        {
            // Fallback estimate; never block enqueue on serialization quirks.
            return entries.Count * 64;
        }
    }
}
