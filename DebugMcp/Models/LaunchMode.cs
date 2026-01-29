namespace DebugMcp.Models;

/// <summary>
/// How the debug session was initiated.
/// </summary>
public enum LaunchMode
{
    /// <summary>Connected to an existing running process.</summary>
    Attach,

    /// <summary>Started process under debugger control.</summary>
    Launch
}
