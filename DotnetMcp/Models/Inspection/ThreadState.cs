namespace DotnetMcp.Models.Inspection;

/// <summary>
/// Current state of a managed thread.
/// </summary>
public enum ThreadState
{
    /// <summary>Thread is actively executing.</summary>
    Running,

    /// <summary>Thread is paused (breakpoint, step, pause).</summary>
    Stopped,

    /// <summary>Thread is in wait/sleep/join state.</summary>
    Waiting,

    /// <summary>Thread created but not yet started.</summary>
    NotStarted,

    /// <summary>Thread has exited.</summary>
    Terminated
}
