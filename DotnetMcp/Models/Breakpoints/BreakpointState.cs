namespace DotnetMcp.Models.Breakpoints;

/// <summary>
/// Enumeration of breakpoint lifecycle states.
/// </summary>
public enum BreakpointState
{
    /// <summary>Location specified but module not loaded yet.</summary>
    Pending,

    /// <summary>Successfully bound to IL code.</summary>
    Bound,

    /// <summary>Explicitly disabled by user.</summary>
    Disabled
}
