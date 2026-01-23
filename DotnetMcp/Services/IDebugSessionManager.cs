using DotnetMcp.Models;

namespace DotnetMcp.Services;

/// <summary>
/// Manages the lifecycle of debug sessions.
/// </summary>
public interface IDebugSessionManager
{
    /// <summary>
    /// Gets the current debug session, or null if disconnected.
    /// </summary>
    DebugSession? CurrentSession { get; }

    /// <summary>
    /// Creates a new debug session by attaching to a running process.
    /// </summary>
    /// <param name="pid">Process ID to attach to.</param>
    /// <param name="timeout">Timeout for the attach operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created debug session.</returns>
    Task<DebugSession> AttachAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new debug session by launching a process under debugger control.
    /// </summary>
    /// <param name="program">Path to the executable or DLL to debug.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="cwd">Working directory.</param>
    /// <param name="env">Environment variables.</param>
    /// <param name="stopAtEntry">Whether to pause at entry point.</param>
    /// <param name="timeout">Timeout for the launch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created debug session.</returns>
    Task<DebugSession> LaunchAsync(
        string program,
        string[]? args = null,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        bool stopAtEntry = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of the debug session.
    /// </summary>
    /// <returns>Current session state with context, or disconnected state if no session.</returns>
    SessionState GetCurrentState();

    /// <summary>
    /// Ends the current debug session.
    /// </summary>
    /// <param name="terminateProcess">Whether to terminate the process (only for launched processes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(bool terminateProcess = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues execution of the paused process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated debug session.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or process is not paused.</exception>
    Task<DebugSession> ContinueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Steps through code in the specified mode.
    /// </summary>
    /// <param name="mode">The stepping mode (In, Over, Out).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated debug session.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or process is not paused.</exception>
    Task<DebugSession> StepAsync(StepMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a step operation to complete.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for step completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Step completion info, or null if timeout occurred.</returns>
    Task<StepCompleteEventArgs?> WaitForStepCompleteAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for the session to reach a specific state.
    /// </summary>
    /// <param name="targetState">The state to wait for.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if state was reached, false if timeout occurred.</returns>
    Task<bool> WaitForStateAsync(SessionState targetState, TimeSpan timeout, CancellationToken cancellationToken = default);
}
