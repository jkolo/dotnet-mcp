using DotnetMcp.Models;
using DotnetMcp.Models.Memory;

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

    /// <summary>
    /// Gets stack frames for a specified thread.
    /// </summary>
    /// <param name="threadId">Thread ID (null = current thread).</param>
    /// <param name="startFrame">Start from frame N (for pagination).</param>
    /// <param name="maxFrames">Maximum frames to return.</param>
    /// <returns>Stack frames and total count.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    (IReadOnlyList<Models.Inspection.StackFrame> Frames, int TotalFrames) GetStackFrames(int? threadId = null, int startFrame = 0, int maxFrames = 20);

    /// <summary>
    /// Gets all managed threads in the debuggee process.
    /// </summary>
    /// <returns>List of thread information.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active.</exception>
    IReadOnlyList<Models.Inspection.ThreadInfo> GetThreads();

    /// <summary>
    /// Gets variables for a specified stack frame.
    /// </summary>
    /// <param name="threadId">Thread ID (null = current thread).</param>
    /// <param name="frameIndex">Frame index (0 = top of stack).</param>
    /// <param name="scope">Which variables to return (all, locals, arguments, this).</param>
    /// <param name="expandPath">Variable path to expand children.</param>
    /// <returns>List of variables.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    IReadOnlyList<Models.Inspection.Variable> GetVariables(int? threadId = null, int frameIndex = 0, string scope = "all", string? expandPath = null);

    /// <summary>
    /// Pauses execution of a running process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of threads with their locations after pause.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active.</exception>
    Task<IReadOnlyList<Models.Inspection.ThreadInfo>> PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a C# expression in the debuggee context.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="threadId">Thread context (null = current thread).</param>
    /// <param name="frameIndex">Stack frame context (0 = top).</param>
    /// <param name="timeoutMs">Evaluation timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation result with value or error.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    Task<Models.Inspection.EvaluationResult> EvaluateAsync(
        string expression,
        int? threadId = null,
        int frameIndex = 0,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default);

    // Memory inspection operations

    /// <summary>
    /// Inspects a heap object's contents including all fields.
    /// </summary>
    /// <param name="objectRef">Object reference (variable name or expression).</param>
    /// <param name="depth">Maximum depth for nested object expansion (default: 1).</param>
    /// <param name="threadId">Thread context (null = current thread).</param>
    /// <param name="frameIndex">Stack frame context (0 = top).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Object inspection result with fields and values.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    Task<ObjectInspection> InspectObjectAsync(
        string objectRef,
        int depth = 1,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads raw memory bytes from the debuggee process.
    /// </summary>
    /// <param name="address">Memory address (hex string or decimal).</param>
    /// <param name="size">Number of bytes to read (max 65536).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory region with bytes and ASCII representation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    Task<MemoryRegion> ReadMemoryAsync(
        string address,
        int size = 256,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets outbound object references (objects this object references).
    /// </summary>
    /// <param name="objectRef">Object reference to analyze.</param>
    /// <param name="includeArrays">Include array element references (default: true).</param>
    /// <param name="maxResults">Maximum references to return (max: 100).</param>
    /// <param name="threadId">Thread context (null = current thread).</param>
    /// <param name="frameIndex">Stack frame context (0 = top).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reference analysis result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    Task<ReferencesResult> GetOutboundReferencesAsync(
        string objectRef,
        bool includeArrays = true,
        int maxResults = 50,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the memory layout of a type including field offsets and padding.
    /// </summary>
    /// <param name="typeName">Full type name or object reference.</param>
    /// <param name="includeInherited">Include inherited fields (default: true).</param>
    /// <param name="includePadding">Include padding analysis (default: true).</param>
    /// <param name="threadId">Thread context (null = current thread).</param>
    /// <param name="frameIndex">Stack frame context (0 = top).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Type layout with fields, offsets, and padding.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active or not paused.</exception>
    Task<TypeLayout> GetTypeLayoutAsync(
        string typeName,
        bool includeInherited = true,
        bool includePadding = true,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default);
}
