namespace DotnetMcp.Models.Breakpoints;

/// <summary>
/// Represents a debugging pause point in source code.
/// </summary>
/// <param name="Id">Unique identifier (UUID format).</param>
/// <param name="Location">Source location (file, line, optional column).</param>
/// <param name="State">Current lifecycle state (pending/bound/disabled).</param>
/// <param name="Enabled">User-controlled enable flag.</param>
/// <param name="Verified">True if bound to executable code.</param>
/// <param name="HitCount">Number of times breakpoint has been hit.</param>
/// <param name="Condition">Optional condition expression (C# syntax).</param>
/// <param name="Message">Status message (e.g., why unverified).</param>
public record Breakpoint(
    string Id,
    BreakpointLocation Location,
    BreakpointState State,
    bool Enabled,
    bool Verified,
    int HitCount,
    string? Condition = null,
    string? Message = null);
