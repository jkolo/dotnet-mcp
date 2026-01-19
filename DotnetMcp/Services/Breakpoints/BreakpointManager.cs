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

        if (_processDebugger.IsAttached)
        {
            try
            {
                var bindResult = await TryBindBreakpointAsync(file, line, column, cancellationToken);
                if (bindResult.Success)
                {
                    state = BreakpointState.Bound;
                    verified = true;
                    resolvedLocation = bindResult.ResolvedLocation ?? location;
                    _logger.LogDebug("Breakpoint {Id} bound at IL offset {Offset}",
                        id, bindResult.ILOffset);
                }
                else
                {
                    message = bindResult.ErrorMessage ?? "Unable to bind breakpoint";
                    _logger.LogDebug("Breakpoint {Id} pending: {Message}", id, message);
                }
            }
            catch (Exception ex)
            {
                message = $"Failed to bind: {ex.Message}";
                _logger.LogWarning(ex, "Failed to bind breakpoint {Id}", id);
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

    private async Task<BindResult> TryBindBreakpointAsync(
        string file,
        int line,
        int? column,
        CancellationToken cancellationToken)
    {
        // TODO: Get loaded modules from ProcessDebugger and find the one containing this file
        // For now, return a pending result
        // Implementation will:
        // 1. Find module containing source file
        // 2. Use PdbSymbolReader to get IL offset
        // 3. Create ICorDebugFunctionBreakpoint
        // 4. Activate it

        // Placeholder - real implementation needs module enumeration
        await Task.CompletedTask;

        return new BindResult
        {
            Success = false,
            ErrorMessage = "Module not loaded; breakpoint will bind when module loads"
        };
    }

    private sealed class BindResult
    {
        public bool Success { get; init; }
        public int ILOffset { get; init; }
        public BreakpointLocation? ResolvedLocation { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
