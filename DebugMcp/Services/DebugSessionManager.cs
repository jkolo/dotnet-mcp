using DebugMcp.Infrastructure;
using DebugMcp.Models;
using Microsoft.Extensions.Logging;

namespace DebugMcp.Services;

/// <summary>
/// Manages the lifecycle of debug sessions.
/// </summary>
public sealed class DebugSessionManager : IDebugSessionManager
{
    private readonly IProcessDebugger _processDebugger;
    private readonly ILogger<DebugSessionManager> _logger;
    private DebugSession? _currentSession;
    private readonly object _lock = new();

    // Step completion signaling
    private readonly SemaphoreSlim _stepCompleteSemaphore = new(0);
    private readonly System.Collections.Concurrent.ConcurrentQueue<StepCompleteEventArgs> _stepCompleteQueue = new();

    // State waiting signaling
    private readonly SemaphoreSlim _stateChangeSemaphore = new(0);
    private SessionState? _awaitedState;

    public DebugSessionManager(IProcessDebugger processDebugger, ILogger<DebugSessionManager> logger)
    {
        _processDebugger = processDebugger;
        _logger = logger;

        // Subscribe to state changes from the process debugger
        _processDebugger.StateChanged += OnStateChanged;
        _processDebugger.StepCompleted += OnStepCompleted;
    }

    /// <inheritdoc />
    public DebugSession? CurrentSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession;
            }
        }
    }

    /// <inheritdoc />
    public async Task<DebugSession> AttachAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession != null)
            {
                throw new InvalidOperationException("A debug session is already active. Disconnect first.");
            }
        }

        _logger.AttachingToProcess(pid);

        var processInfo = await _processDebugger.AttachAsync(pid, timeout, cancellationToken);

        var session = new DebugSession
        {
            ProcessId = processInfo.Pid,
            ProcessName = processInfo.Name,
            ExecutablePath = processInfo.ExecutablePath,
            RuntimeVersion = processInfo.RuntimeVersion ?? "Unknown",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running,
            LaunchMode = LaunchMode.Attach
        };

        lock (_lock)
        {
            _currentSession = session;
        }

        _logger.AttachedToProcess(session.ProcessId, session.ProcessName, session.RuntimeVersion);

        return session;
    }

    /// <inheritdoc />
    public async Task<DebugSession> LaunchAsync(
        string program,
        string[]? args = null,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        bool stopAtEntry = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession != null)
            {
                throw new InvalidOperationException("A debug session is already active. Disconnect first.");
            }
        }

        _logger.LaunchingProcess(program);

        var processInfo = await _processDebugger.LaunchAsync(
            program, args, cwd, env, stopAtEntry, timeout, cancellationToken);

        var session = new DebugSession
        {
            ProcessId = processInfo.Pid,
            ProcessName = processInfo.Name,
            ExecutablePath = processInfo.ExecutablePath,
            RuntimeVersion = processInfo.RuntimeVersion ?? "Unknown",
            AttachedAt = DateTime.UtcNow,
            State = stopAtEntry ? SessionState.Paused : SessionState.Running,
            LaunchMode = LaunchMode.Launch,
            CommandLineArgs = args,
            WorkingDirectory = cwd,
            PauseReason = stopAtEntry ? Models.PauseReason.Entry : null
        };

        lock (_lock)
        {
            _currentSession = session;
        }

        _logger.LaunchedProcess(session.ProcessId, session.ProcessName);

        return session;
    }

    /// <inheritdoc />
    public SessionState GetCurrentState()
    {
        lock (_lock)
        {
            return _currentSession?.State ?? SessionState.Disconnected;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(bool terminateProcess = false, CancellationToken cancellationToken = default)
    {
        DebugSession? session;
        lock (_lock)
        {
            session = _currentSession;
            if (session == null)
            {
                return; // Already disconnected
            }
        }

        _logger.DisconnectingFromProcess(session.ProcessId, terminateProcess);

        if (terminateProcess && session.LaunchMode == LaunchMode.Launch)
        {
            await _processDebugger.TerminateAsync(cancellationToken);
        }
        else
        {
            await _processDebugger.DetachAsync(cancellationToken);
        }

        lock (_lock)
        {
            _currentSession = null;
        }

        _logger.DisconnectedFromProcess(session.ProcessId);
    }

    /// <inheritdoc />
    public async Task<DebugSession> ContinueAsync(CancellationToken cancellationToken = default)
    {
        DebugSession session;
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }

            session = _currentSession;
        }

        _logger.LogInformation("Continuing execution for process {ProcessId}", session.ProcessId);

        await _processDebugger.ContinueAsync(cancellationToken);

        lock (_lock)
        {
            return _currentSession!;
        }
    }

    /// <inheritdoc />
    public async Task<DebugSession> StepAsync(StepMode mode, CancellationToken cancellationToken = default)
    {
        DebugSession session;
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }

            session = _currentSession;
        }

        _logger.LogInformation("Stepping {Mode} for process {ProcessId}", mode, session.ProcessId);

        // Initiate the step
        await _processDebugger.StepAsync(mode, cancellationToken);

        // Wait for step to complete (state returns to Paused)
        var stepComplete = await WaitForStepCompleteAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (stepComplete == null)
        {
            _logger.LogWarning("Step operation timed out waiting for completion");
        }

        lock (_lock)
        {
            return _currentSession!;
        }
    }

    /// <inheritdoc />
    public async Task<StepCompleteEventArgs?> WaitForStepCompleteAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await _stepCompleteSemaphore.WaitAsync(timeoutCts.Token);

            if (_stepCompleteQueue.TryDequeue(out var result))
            {
                return result;
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> WaitForStateAsync(SessionState targetState, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Check if already in target state
        lock (_lock)
        {
            if (_currentSession?.State == targetState)
            {
                return true;
            }
            _awaitedState = targetState;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await _stateChangeSemaphore.WaitAsync(timeoutCts.Token);

                lock (_lock)
                {
                    if (_currentSession?.State == targetState)
                    {
                        _awaitedState = null;
                        return true;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred
        }

        lock (_lock)
        {
            _awaitedState = null;
        }
        return false;
    }

    private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        lock (_lock)
        {
            if (_currentSession == null) return;

            var oldState = _currentSession.State.ToString();
            _currentSession.State = e.NewState;
            _currentSession.PauseReason = e.PauseReason;
            _currentSession.CurrentLocation = e.Location;
            _currentSession.ActiveThreadId = e.ThreadId;

            _logger.SessionStateChanged(oldState, e.NewState.ToString());

            if (e.NewState == SessionState.Paused && e.Location != null)
            {
                _logger.ProcessPaused(
                    $"{e.Location.File}:{e.Location.Line}",
                    e.PauseReason?.ToString() ?? "Unknown",
                    e.ThreadId ?? 0);
            }

            // Signal state waiters if target state reached
            if (_awaitedState.HasValue && e.NewState == _awaitedState.Value)
            {
                _stateChangeSemaphore.Release();
            }
        }
    }

    private void OnStepCompleted(object? sender, StepCompleteEventArgs e)
    {
        _stepCompleteQueue.Enqueue(e);
        _stepCompleteSemaphore.Release();

        _logger.LogDebug("Step completed: thread={ThreadId}, mode={Mode}, reason={Reason}",
            e.ThreadId, e.StepMode, e.Reason);
    }

    /// <inheritdoc />
    public (IReadOnlyList<Models.Inspection.StackFrame> Frames, int TotalFrames) GetStackFrames(int? threadId = null, int startFrame = 0, int maxFrames = 20)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return _processDebugger.GetStackFrames(threadId, startFrame, maxFrames);
    }

    /// <inheritdoc />
    public IReadOnlyList<Models.Inspection.ThreadInfo> GetThreads()
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }
        }

        return _processDebugger.GetThreads();
    }

    /// <inheritdoc />
    public IReadOnlyList<Models.Inspection.Variable> GetVariables(int? threadId = null, int frameIndex = 0, string scope = "all", string? expandPath = null)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return _processDebugger.GetVariables(threadId, frameIndex, scope, expandPath);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Models.Inspection.ThreadInfo>> PauseAsync(CancellationToken cancellationToken = default)
    {
        DebugSession session;
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            session = _currentSession;
        }

        _logger.LogInformation("Pausing process {ProcessId}", session.ProcessId);

        return await _processDebugger.PauseAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Models.Inspection.EvaluationResult> EvaluateAsync(
        string expression,
        int? threadId = null,
        int frameIndex = 0,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return await _processDebugger.EvaluateAsync(expression, threadId, frameIndex, timeoutMs, cancellationToken);
    }

    // Memory inspection operations

    /// <inheritdoc />
    public async Task<Models.Memory.ObjectInspection> InspectObjectAsync(
        string objectRef,
        int depth = 1,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return await _processDebugger.InspectObjectAsync(objectRef, depth, threadId, frameIndex, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Models.Memory.MemoryRegion> ReadMemoryAsync(
        string address,
        int size = 256,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return await _processDebugger.ReadMemoryAsync(address, size, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Models.Memory.ReferencesResult> GetOutboundReferencesAsync(
        string objectRef,
        bool includeArrays = true,
        int maxResults = 50,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return await _processDebugger.GetOutboundReferencesAsync(objectRef, includeArrays, maxResults, threadId, frameIndex, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Models.Memory.TypeLayout> GetTypeLayoutAsync(
        string typeName,
        bool includeInherited = true,
        bool includePadding = true,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                throw new InvalidOperationException("No active debug session");
            }

            if (_currentSession.State != SessionState.Paused)
            {
                throw new InvalidOperationException($"Process is not paused (current state: {_currentSession.State})");
            }
        }

        return await _processDebugger.GetTypeLayoutAsync(typeName, includeInherited, includePadding, threadId, frameIndex, cancellationToken);
    }
}
