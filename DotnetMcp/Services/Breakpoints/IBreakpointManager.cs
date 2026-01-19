using DotnetMcp.Models.Breakpoints;

namespace DotnetMcp.Services.Breakpoints;

/// <summary>
/// Manages breakpoint lifecycle and operations.
/// </summary>
public interface IBreakpointManager
{
    /// <summary>
    /// Sets a breakpoint at the specified source location.
    /// </summary>
    /// <param name="file">Absolute path to source file.</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="column">Optional 1-based column for targeting lambdas/inline statements.</param>
    /// <param name="condition">Optional condition expression (C# syntax).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or existing breakpoint.</returns>
    Task<Breakpoint> SetBreakpointAsync(
        string file,
        int line,
        int? column = null,
        string? condition = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a breakpoint by ID.
    /// </summary>
    /// <param name="breakpointId">ID of the breakpoint to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if breakpoint was found and removed.</returns>
    Task<bool> RemoveBreakpointAsync(
        string breakpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all breakpoints in the current session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all breakpoints.</returns>
    Task<IReadOnlyList<Breakpoint>> GetBreakpointsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific breakpoint by ID.
    /// </summary>
    /// <param name="breakpointId">ID of the breakpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The breakpoint if found, null otherwise.</returns>
    Task<Breakpoint?> GetBreakpointAsync(
        string breakpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for any breakpoint to be hit.
    /// </summary>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hit information if a breakpoint was hit, null on timeout.</returns>
    Task<BreakpointHit?> WaitForBreakpointAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a breakpoint.
    /// </summary>
    /// <param name="breakpointId">ID of the breakpoint.</param>
    /// <param name="enabled">True to enable, false to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated breakpoint if found.</returns>
    Task<Breakpoint?> SetBreakpointEnabledAsync(
        string breakpointId,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an exception breakpoint.
    /// </summary>
    /// <param name="exceptionType">Full type name of exception.</param>
    /// <param name="breakOnFirstChance">Break when thrown.</param>
    /// <param name="breakOnSecondChance">Break when unhandled.</param>
    /// <param name="includeSubtypes">Match derived exception types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created exception breakpoint.</returns>
    Task<ExceptionBreakpoint> SetExceptionBreakpointAsync(
        string exceptionType,
        bool breakOnFirstChance = true,
        bool breakOnSecondChance = true,
        bool includeSubtypes = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an exception breakpoint.
    /// </summary>
    /// <param name="breakpointId">ID of the exception breakpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if found and removed.</returns>
    Task<bool> RemoveExceptionBreakpointAsync(
        string breakpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all breakpoints (called when session disconnects).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAllBreakpointsAsync(CancellationToken cancellationToken = default);
}
