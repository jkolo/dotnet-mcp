namespace DebugMcp.Models.Breakpoints;

/// <summary>
/// Information about a triggered breakpoint event.
/// </summary>
/// <param name="BreakpointId">ID of the hit breakpoint.</param>
/// <param name="ThreadId">Thread that hit the breakpoint.</param>
/// <param name="Timestamp">UTC time when breakpoint was hit.</param>
/// <param name="Location">Exact location of the hit.</param>
/// <param name="HitCount">Hit count at time of hit.</param>
/// <param name="ExceptionInfo">Exception information if this was an exception breakpoint hit.</param>
public record BreakpointHit(
    string BreakpointId,
    int ThreadId,
    DateTime Timestamp,
    BreakpointLocation Location,
    int HitCount,
    ExceptionInfo? ExceptionInfo = null);
