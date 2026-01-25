namespace DotnetMcp.Models.Inspection;

/// <summary>
/// Represents a managed thread in the debuggee process.
/// </summary>
/// <param name="Id">OS thread ID.</param>
/// <param name="Name">Thread name (may be null for unnamed threads).</param>
/// <param name="State">Current thread state.</param>
/// <param name="IsCurrent">True if this is the active/current thread.</param>
/// <param name="Location">Current location if thread is stopped.</param>
public sealed record ThreadInfo(
    int Id,
    string? Name,
    ThreadState State,
    bool IsCurrent,
    SourceLocation? Location = null);
