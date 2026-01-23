using System.Collections.Concurrent;
using ClrDebug;
using DotnetMcp.Models.Breakpoints;
using Microsoft.Extensions.Logging;

namespace DotnetMcp.Services.Breakpoints;

/// <summary>
/// Manages breakpoint lifecycle and operations using ICorDebug APIs.
/// </summary>
public sealed class BreakpointManager : IBreakpointManager
{
    private readonly BreakpointRegistry _registry;
    private readonly IPdbSymbolReader _pdbReader;
    private readonly IProcessDebugger _processDebugger;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly ILogger<BreakpointManager> _logger;

    private readonly ConcurrentQueue<BreakpointHit> _hitQueue = new();
    private readonly SemaphoreSlim _hitSemaphore = new(0);

    /// <summary>
    /// Event raised when a breakpoint's state changes (Pending→Bound or Bound→Pending).
    /// </summary>
    public event EventHandler<BreakpointStateChangedEventArgs>? BreakpointStateChanged;

    public BreakpointManager(
        BreakpointRegistry registry,
        IPdbSymbolReader pdbReader,
        IProcessDebugger processDebugger,
        IConditionEvaluator conditionEvaluator,
        ILogger<BreakpointManager> logger)
    {
        _registry = registry;
        _pdbReader = pdbReader;
        _processDebugger = processDebugger;
        _conditionEvaluator = conditionEvaluator;
        _logger = logger;

        // Subscribe to debugger events
        _processDebugger.BreakpointHit += OnDebuggerBreakpointHit;
        _processDebugger.ModuleLoaded += OnModuleLoaded;
        _processDebugger.ModuleUnloaded += OnModuleUnloaded;
        _processDebugger.ExceptionHit += OnExceptionHit;
    }

    private void OnDebuggerBreakpointHit(object? sender, BreakpointHitEventArgs e)
    {
        Breakpoint? breakpoint = null;
        BreakpointLocation? resolvedLocation = null;

        // Try to resolve source location using PDB if we have IL info
        if (e.MethodToken.HasValue && e.ILOffset.HasValue && !string.IsNullOrEmpty(e.ModulePath))
        {
            var sourceLocation = _pdbReader.FindSourceLocationAsync(
                e.ModulePath,
                e.MethodToken.Value,
                e.ILOffset.Value,
                CancellationToken.None).GetAwaiter().GetResult();

            if (sourceLocation != null)
            {
                resolvedLocation = new BreakpointLocation(
                    File: sourceLocation.FilePath,
                    Line: sourceLocation.Line,
                    Column: sourceLocation.Column);

                breakpoint = _registry.FindByLocation(sourceLocation.FilePath, sourceLocation.Line);
            }
        }

        // Fallback: try to find by original location
        if (breakpoint == null && e.Location != null)
        {
            breakpoint = _registry.FindByLocation(e.Location.File, e.Location.Line);
            if (breakpoint != null)
            {
                resolvedLocation = breakpoint.Location;
            }
        }

        if (breakpoint == null)
        {
            _logger.LogWarning("Breakpoint hit but not found in registry. MethodToken={MethodToken}, ILOffset={ILOffset}",
                e.MethodToken, e.ILOffset);
            return;
        }

        // Create hit record and process it
        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: e.ThreadId,
            Timestamp: e.Timestamp,
            Location: resolvedLocation ?? breakpoint.Location,
            HitCount: breakpoint.HitCount + 1,
            ExceptionInfo: null);

        OnBreakpointHit(hit);
    }

    /// <inheritdoc />
    public async Task<Breakpoint> SetBreakpointAsync(
        string file,
        int line,
        int? column = null,
        string? condition = null,
        CancellationToken cancellationToken = default)
    {
        // Validate condition if provided
        if (!string.IsNullOrWhiteSpace(condition))
        {
            var validation = _conditionEvaluator.ValidateCondition(condition);
            if (!validation.IsValid)
            {
                var errorMsg = validation.ErrorPosition.HasValue
                    ? $"Invalid condition at position {validation.ErrorPosition}: {validation.ErrorMessage}"
                    : $"Invalid condition: {validation.ErrorMessage}";
                throw new ArgumentException(errorMsg, nameof(condition));
            }
        }

        // Check for duplicate breakpoint at same location
        var existing = _registry.FindByLocation(file, line);
        if (existing != null)
        {
            _logger.LogDebug("Breakpoint already exists at {File}:{Line}, returning existing ID {Id}",
                file, line, existing.Id);

            // Update condition if different
            if (condition != existing.Condition)
            {
                var updated = existing with { Condition = condition };
                _registry.Update(updated);
                return updated;
            }

            return existing;
        }

        // Generate unique ID
        var id = $"bp-{Guid.NewGuid()}";

        // Create location
        var location = new BreakpointLocation(
            File: Path.GetFullPath(file),
            Line: line,
            Column: column);

        // Try to bind the breakpoint if we have an active session
        var state = BreakpointState.Pending;
        var verified = false;
        string? message = null;
        BreakpointLocation? resolvedLocation = location;
        string? boundModulePath = null;
        object? nativeBreakpoint = null;

        if (_processDebugger.IsAttached)
        {
            // Try to bind to already loaded modules
            var loadedModules = _processDebugger.GetLoadedModules();
            _logger.LogDebug("Searching for source file in {Count} loaded modules", loadedModules.Count);

            foreach (var moduleInfo in loadedModules)
            {
                // Skip dynamic and in-memory modules
                if (moduleInfo.IsDynamic || moduleInfo.IsInMemory)
                {
                    continue;
                }

                try
                {
                    // Check if this module contains the source file
                    var containsFile = await _pdbReader.ContainsSourceFileAsync(
                        moduleInfo.ModulePath,
                        file,
                        cancellationToken);

                    if (!containsFile)
                    {
                        continue;
                    }

                    _logger.LogDebug("Found source file {File} in module {Module}",
                        file, moduleInfo.ModulePath);

                    // Try to bind the breakpoint
                    var bindResult = await TryBindBreakpointInModuleAsync(
                        moduleInfo.NativeModule as CorDebugModule,
                        moduleInfo.ModulePath,
                        file,
                        line,
                        column,
                        cancellationToken);

                    if (bindResult.Success)
                    {
                        state = BreakpointState.Bound;
                        verified = true;
                        resolvedLocation = bindResult.ResolvedLocation ?? location;
                        boundModulePath = moduleInfo.ModulePath;
                        nativeBreakpoint = bindResult.NativeBreakpoint;
                        _logger.LogDebug("Breakpoint {Id} bound at IL offset {Offset}",
                            id, bindResult.ILOffset);
                        break;
                    }
                    else
                    {
                        message = bindResult.ErrorMessage;
                        _logger.LogDebug("Failed to bind in module {Module}: {Error}",
                            moduleInfo.ModulePath, bindResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking module {Module} for source file",
                        moduleInfo.ModulePath);
                }
            }

            if (state == BreakpointState.Pending)
            {
                message = message ?? "Module not loaded; breakpoint will bind when module loads";
                _logger.LogDebug("Breakpoint {Id} pending: {Message}", id, message);
            }
        }
        else
        {
            message = "No active debug session; breakpoint will bind when session starts";
        }

        var breakpoint = new Breakpoint(
            Id: id,
            Location: resolvedLocation,
            State: state,
            Enabled: true,
            Verified: verified,
            HitCount: 0,
            Condition: condition,
            Message: message);

        _registry.Add(breakpoint);

        // Store native breakpoint handle if bound
        if (nativeBreakpoint != null)
        {
            _registry.SetNativeBreakpoint(id, nativeBreakpoint, boundModulePath);
        }

        _logger.LogInformation("Created breakpoint {Id} at {File}:{Line} (state: {State})",
            id, file, line, state);

        return breakpoint;
    }

    /// <inheritdoc />
    public Task<bool> RemoveBreakpointAsync(
        string breakpointId,
        CancellationToken cancellationToken = default)
    {
        var breakpoint = _registry.Remove(breakpointId);
        if (breakpoint == null)
        {
            _logger.LogDebug("Breakpoint {Id} not found for removal", breakpointId);
            return Task.FromResult(false);
        }

        // TODO: Deactivate the ICorDebugFunctionBreakpoint if bound
        _logger.LogInformation("Removed breakpoint {Id}", breakpointId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Breakpoint>> GetBreakpointsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_registry.GetAll());
    }

    /// <inheritdoc />
    public Task<Breakpoint?> GetBreakpointAsync(
        string breakpointId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_registry.Get(breakpointId));
    }

    /// <inheritdoc />
    public async Task<BreakpointHit?> WaitForBreakpointAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await _hitSemaphore.WaitAsync(timeoutCts.Token);

            if (_hitQueue.TryDequeue(out var hit))
            {
                return hit;
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
    public Task<Breakpoint?> SetBreakpointEnabledAsync(
        string breakpointId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var breakpoint = _registry.Get(breakpointId);
        if (breakpoint == null)
        {
            return Task.FromResult<Breakpoint?>(null);
        }

        var newState = enabled ? BreakpointState.Bound : BreakpointState.Disabled;
        if (breakpoint.State == BreakpointState.Pending && enabled)
        {
            newState = BreakpointState.Pending; // Keep as pending if not yet bound
        }

        var updated = breakpoint with
        {
            Enabled = enabled,
            State = breakpoint.State == BreakpointState.Pending ? breakpoint.State : newState
        };

        _registry.Update(updated);

        // TODO: Call ICorDebugFunctionBreakpoint.Activate(enabled) if bound
        _logger.LogInformation("Breakpoint {Id} {Action}", breakpointId, enabled ? "enabled" : "disabled");

        return Task.FromResult<Breakpoint?>(updated);
    }

    /// <inheritdoc />
    public Task<ExceptionBreakpoint> SetExceptionBreakpointAsync(
        string exceptionType,
        bool breakOnFirstChance = true,
        bool breakOnSecondChance = true,
        bool includeSubtypes = true,
        CancellationToken cancellationToken = default)
    {
        var id = $"ebp-{Guid.NewGuid()}";

        var exceptionBreakpoint = new ExceptionBreakpoint(
            Id: id,
            ExceptionType: exceptionType,
            BreakOnFirstChance: breakOnFirstChance,
            BreakOnSecondChance: breakOnSecondChance,
            IncludeSubtypes: includeSubtypes,
            Enabled: true,
            Verified: true, // Exception breakpoints are always "verified" - type check happens at runtime
            HitCount: 0);

        _registry.AddException(exceptionBreakpoint);
        _logger.LogInformation("Created exception breakpoint {Id} for {Type}", id, exceptionType);

        return Task.FromResult(exceptionBreakpoint);
    }

    /// <inheritdoc />
    public Task<bool> RemoveExceptionBreakpointAsync(
        string breakpointId,
        CancellationToken cancellationToken = default)
    {
        var removed = _registry.RemoveException(breakpointId);
        if (removed == null)
        {
            return Task.FromResult(false);
        }

        _logger.LogInformation("Removed exception breakpoint {Id}", breakpointId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task ClearAllBreakpointsAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Deactivate all ICorDebugFunctionBreakpoints
        _registry.Clear();

        // Clear hit queue
        while (_hitQueue.TryDequeue(out _)) { }

        _logger.LogInformation("Cleared all breakpoints");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by debugger callback when a breakpoint is hit.
    /// Returns true if execution should pause, false if condition is false (silent continue).
    /// </summary>
    internal bool OnBreakpointHit(BreakpointHit hit)
    {
        // Increment hit count in registry
        var breakpoint = _registry.Get(hit.BreakpointId);
        if (breakpoint != null)
        {
            var newHitCount = breakpoint.HitCount + 1;
            var updated = breakpoint with { HitCount = newHitCount };
            _registry.Update(updated);

            // Evaluate condition if present
            if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
            {
                var context = new ConditionContext
                {
                    HitCount = newHitCount,
                    ThreadId = hit.ThreadId
                };

                var result = _conditionEvaluator.Evaluate(breakpoint.Condition, context);

                if (!result.Success)
                {
                    // Condition evaluation failed - log error and continue
                    _logger.LogWarning("Condition evaluation failed for breakpoint {Id}: {Error}",
                        hit.BreakpointId, result.ErrorMessage);
                    // Still break so user can see the error
                }
                else if (!result.Value)
                {
                    // Condition is false - silent continue
                    _logger.LogDebug("Breakpoint {Id} condition is false (hitCount={HitCount}), continuing",
                        hit.BreakpointId, newHitCount);
                    return false; // Don't pause
                }
            }
        }

        // Queue the hit for waiting clients
        _hitQueue.Enqueue(hit);
        _hitSemaphore.Release();

        _logger.LogDebug("Breakpoint {Id} hit on thread {ThreadId}", hit.BreakpointId, hit.ThreadId);
        return true; // Pause execution
    }

    /// <summary>
    /// Called by debugger callback when an exception is thrown.
    /// Matches against registered exception breakpoints and queues hit if matched.
    /// </summary>
    private void OnExceptionHit(object? sender, ExceptionHitEventArgs e)
    {
        // Find matching exception breakpoints
        var matchingBreakpoints = _registry.FindMatchingExceptionBreakpoints(
            e.ExceptionType,
            e.IsFirstChance);

        foreach (var exBp in matchingBreakpoints)
        {
            // Increment hit count
            var updated = exBp with { HitCount = exBp.HitCount + 1 };
            _registry.UpdateException(updated);

            // Create exception info
            var exceptionInfo = new ExceptionInfo(
                Type: e.ExceptionType,
                Message: e.ExceptionMessage,
                IsFirstChance: e.IsFirstChance,
                StackTrace: null);

            // Create hit with resolved location
            var location = e.Location != null
                ? new BreakpointLocation(e.Location.File, e.Location.Line, null)
                : new BreakpointLocation("Unknown", 0, null);

            var hit = new BreakpointHit(
                BreakpointId: exBp.Id,
                ThreadId: e.ThreadId,
                Timestamp: e.Timestamp,
                Location: location,
                HitCount: updated.HitCount,
                ExceptionInfo: exceptionInfo);

            // Queue the hit for waiting clients
            _hitQueue.Enqueue(hit);
            _hitSemaphore.Release();

            _logger.LogInformation("Exception breakpoint {Id} hit: {ExceptionType} on thread {ThreadId}",
                exBp.Id, e.ExceptionType, e.ThreadId);
        }
    }

    private void OnModuleLoaded(object? sender, ModuleLoadedEventArgs e)
    {
        // Skip dynamic and in-memory modules as they don't have PDB files
        if (e.IsDynamic || e.IsInMemory)
        {
            _logger.LogDebug("Skipping pending breakpoint binding for dynamic/in-memory module {Module}",
                e.ModulePath);
            return;
        }

        // Get all pending breakpoints and try to bind them to this module
        var pendingBreakpoints = _registry.GetPending();
        if (pendingBreakpoints.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Module {Module} loaded, checking {Count} pending breakpoints",
            e.ModulePath, pendingBreakpoints.Count);

        foreach (var breakpoint in pendingBreakpoints)
        {
            if (!breakpoint.Enabled)
            {
                continue;
            }

            try
            {
                // Check if this module contains the source file
                var containsFile = _pdbReader.ContainsSourceFileAsync(
                    e.ModulePath,
                    breakpoint.Location.File,
                    CancellationToken.None).GetAwaiter().GetResult();

                if (!containsFile)
                {
                    continue;
                }

                _logger.LogDebug("Attempting to bind breakpoint {Id} at {File}:{Line} to module {Module}",
                    breakpoint.Id, breakpoint.Location.File, breakpoint.Location.Line, e.ModulePath);

                // Try to bind the breakpoint
                var bindResult = TryBindBreakpointInModuleAsync(
                    e.NativeModule as CorDebugModule,
                    e.ModulePath,
                    breakpoint.Location.File,
                    breakpoint.Location.Line,
                    breakpoint.Location.Column,
                    CancellationToken.None).GetAwaiter().GetResult();

                if (bindResult.Success)
                {
                    // Update breakpoint state to Bound
                    var oldState = breakpoint.State;
                    var updated = breakpoint with
                    {
                        State = BreakpointState.Bound,
                        Verified = true,
                        Message = null,
                        Location = bindResult.ResolvedLocation ?? breakpoint.Location
                    };

                    _registry.Update(updated);
                    _registry.SetNativeBreakpoint(breakpoint.Id, bindResult.NativeBreakpoint, e.ModulePath);

                    // T082: Log state transition
                    _logger.LogInformation(
                        "Breakpoint {Id} bound to {Module} at IL offset {Offset} (Pending→Bound)",
                        breakpoint.Id, e.ModulePath, bindResult.ILOffset);

                    // T081: Fire state change event
                    BreakpointStateChanged?.Invoke(this, new BreakpointStateChangedEventArgs
                    {
                        BreakpointId = breakpoint.Id,
                        OldState = oldState,
                        NewState = BreakpointState.Bound,
                        ModulePath = e.ModulePath,
                        ILOffset = bindResult.ILOffset
                    });
                }
                else
                {
                    _logger.LogDebug("Failed to bind breakpoint {Id}: {Error}",
                        breakpoint.Id, bindResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error binding breakpoint {Id} to module {Module}",
                    breakpoint.Id, e.ModulePath);
            }
        }
    }

    private void OnModuleUnloaded(object? sender, ModuleUnloadedEventArgs e)
    {
        // Get all bound breakpoints for this module and transition them back to Pending
        var boundBreakpoints = _registry.GetBoundForModule(e.ModulePath);
        if (boundBreakpoints.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Module {Module} unloaded, unbinding {Count} breakpoints",
            e.ModulePath, boundBreakpoints.Count);

        foreach (var breakpoint in boundBreakpoints)
        {
            try
            {
                var oldState = breakpoint.State;

                // Deactivate native breakpoint if it exists
                var nativeBreakpoint = _registry.GetNativeBreakpoint(breakpoint.Id);
                if (nativeBreakpoint is CorDebugFunctionBreakpoint corBp)
                {
                    try
                    {
                        corBp.Activate(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to deactivate breakpoint {Id} (module already unloaded)",
                            breakpoint.Id);
                    }
                }

                // Update breakpoint state to Pending
                var updated = breakpoint with
                {
                    State = BreakpointState.Pending,
                    Verified = false,
                    Message = $"Module {Path.GetFileName(e.ModulePath)} unloaded; breakpoint will bind when module loads"
                };

                _registry.Update(updated);
                _registry.SetNativeBreakpoint(breakpoint.Id, null, null);

                // T082: Log state transition
                _logger.LogInformation(
                    "Breakpoint {Id} unbound, module {Module} unloaded (Bound→Pending)",
                    breakpoint.Id, e.ModulePath);

                // T081: Fire state change event
                BreakpointStateChanged?.Invoke(this, new BreakpointStateChangedEventArgs
                {
                    BreakpointId = breakpoint.Id,
                    OldState = oldState,
                    NewState = BreakpointState.Pending,
                    ModulePath = e.ModulePath,
                    ILOffset = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unbinding breakpoint {Id} from module {Module}",
                    breakpoint.Id, e.ModulePath);
            }
        }
    }

    private Task<BindResult> TryBindBreakpointAsync(
        string file,
        int line,
        int? column,
        CancellationToken cancellationToken)
    {
        // This method is called when setting a breakpoint before we know the module.
        // Since we don't have the module yet, return pending status.
        // The actual binding will happen in OnModuleLoaded.
        return Task.FromResult(new BindResult
        {
            Success = false,
            ErrorMessage = "Module not loaded; breakpoint will bind when module loads"
        });
    }

    private async Task<BindResult> TryBindBreakpointInModuleAsync(
        CorDebugModule? module,
        string modulePath,
        string file,
        int line,
        int? column,
        CancellationToken cancellationToken)
    {
        if (module == null)
        {
            return new BindResult
            {
                Success = false,
                ErrorMessage = "Invalid module reference"
            };
        }

        try
        {
            // Step 1: Use PdbSymbolReader to get IL offset for the source location
            var ilResult = await _pdbReader.FindILOffsetAsync(
                modulePath,
                file,
                line,
                column,
                cancellationToken);

            if (ilResult == null)
            {
                return new BindResult
                {
                    Success = false,
                    ErrorMessage = $"No executable code at {Path.GetFileName(file)}:{line}"
                };
            }

            // Step 2: Get ICorDebugFunction from the module using method token
            var methodToken = (uint)ilResult.MethodToken;
            var function = module.GetFunctionFromToken(methodToken);

            // Step 3: Get ICorDebugCode and create breakpoint at IL offset
            var code = function.ILCode;
            var nativeBreakpoint = code.CreateBreakpoint(ilResult.ILOffset);

            // Step 4: Activate the breakpoint
            nativeBreakpoint.Activate(true);

            _logger.LogDebug(
                "Created ICorDebugFunctionBreakpoint at method 0x{Token:X8}, IL offset {Offset}",
                methodToken, ilResult.ILOffset);

            // Return success with resolved location
            var resolvedLocation = new BreakpointLocation(
                File: file,
                Line: ilResult.StartLine,
                Column: ilResult.StartColumn);

            return new BindResult
            {
                Success = true,
                ILOffset = ilResult.ILOffset,
                ResolvedLocation = resolvedLocation,
                NativeBreakpoint = nativeBreakpoint
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bind breakpoint at {File}:{Line} in module {Module}",
                file, line, modulePath);

            return new BindResult
            {
                Success = false,
                ErrorMessage = $"Binding failed: {ex.Message}"
            };
        }
    }

    private sealed class BindResult
    {
        public bool Success { get; init; }
        public int ILOffset { get; init; }
        public BreakpointLocation? ResolvedLocation { get; init; }
        public string? ErrorMessage { get; init; }
        public object? NativeBreakpoint { get; init; }
    }
}

/// <summary>
/// Event args for breakpoint state changes.
/// </summary>
public sealed class BreakpointStateChangedEventArgs : EventArgs
{
    /// <summary>The breakpoint ID.</summary>
    public required string BreakpointId { get; init; }

    /// <summary>The previous state.</summary>
    public required BreakpointState OldState { get; init; }

    /// <summary>The new state.</summary>
    public required BreakpointState NewState { get; init; }

    /// <summary>The module path (if transitioning to/from Bound).</summary>
    public string? ModulePath { get; init; }

    /// <summary>The IL offset (if bound).</summary>
    public int? ILOffset { get; init; }
}
