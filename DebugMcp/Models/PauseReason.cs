namespace DebugMcp.Models;

/// <summary>
/// Enumeration of reasons why execution paused.
/// </summary>
public enum PauseReason
{
    /// <summary>Hit a breakpoint.</summary>
    Breakpoint,

    /// <summary>Completed a step operation.</summary>
    Step,

    /// <summary>Exception was thrown.</summary>
    Exception,

    /// <summary>User requested pause.</summary>
    Pause,

    /// <summary>Stopped at entry point (launch with stopAtEntry).</summary>
    Entry
}
