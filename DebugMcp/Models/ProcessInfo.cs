namespace DebugMcp.Models;

/// <summary>
/// Information about a debuggable .NET process.
/// </summary>
/// <param name="Pid">Process ID.</param>
/// <param name="Name">Process name.</param>
/// <param name="ExecutablePath">Path to executable.</param>
/// <param name="IsManaged">True if .NET process detected.</param>
/// <param name="CommandLine">Full command line (optional).</param>
/// <param name="RuntimeVersion">Detected .NET version (optional).</param>
public record ProcessInfo(
    int Pid,
    string Name,
    string ExecutablePath,
    bool IsManaged,
    string? CommandLine = null,
    string? RuntimeVersion = null);
