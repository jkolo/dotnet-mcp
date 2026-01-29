namespace DebugMcp.Models;

/// <summary>
/// Enumeration of possible debug session states.
/// </summary>
public enum SessionState
{
    /// <summary>No active debug session.</summary>
    Disconnected,

    /// <summary>Process is executing.</summary>
    Running,

    /// <summary>Process is stopped (breakpoint, step, exception, or user pause).</summary>
    Paused
}
