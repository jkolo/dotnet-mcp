namespace DebugMcp.Models;

/// <summary>
/// Represents an active debugging connection to a .NET process.
/// </summary>
public sealed class DebugSession
{
    /// <summary>OS process ID of the debuggee.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Name of the executable (e.g., "MyApp").</summary>
    public required string ProcessName { get; init; }

    /// <summary>Full path to the executable.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>.NET runtime version (e.g., ".NET 8.0.1").</summary>
    public required string RuntimeVersion { get; init; }

    /// <summary>UTC timestamp when session started.</summary>
    public required DateTime AttachedAt { get; init; }

    /// <summary>Current execution state.</summary>
    public SessionState State { get; set; } = SessionState.Running;

    /// <summary>How the session was started.</summary>
    public required LaunchMode LaunchMode { get; init; }

    /// <summary>Arguments passed (launch mode only).</summary>
    public string[]? CommandLineArgs { get; init; }

    /// <summary>Working directory (launch mode only).</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Why execution paused (only valid when State is Paused).</summary>
    public PauseReason? PauseReason { get; set; }

    /// <summary>Where execution stopped (only valid when State is Paused).</summary>
    public SourceLocation? CurrentLocation { get; set; }

    /// <summary>Thread that caused the pause (only valid when State is Paused).</summary>
    public int? ActiveThreadId { get; set; }
}
