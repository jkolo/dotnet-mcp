using DotnetMcp.Models;
using DotnetMcp.Models.Inspection;
using DotnetMcp.Models.Memory;

namespace DotnetMcp.Services;

/// <summary>
/// Low-level process debugging operations using ICorDebug.
/// </summary>
public interface IProcessDebugger
{
    /// <summary>
    /// Event raised when the session state changes.
    /// </summary>
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when a breakpoint is hit.
    /// </summary>
    event EventHandler<BreakpointHitEventArgs>? BreakpointHit;

    /// <summary>
    /// Event raised when a module is loaded.
    /// </summary>
    event EventHandler<ModuleLoadedEventArgs>? ModuleLoaded;

    /// <summary>
    /// Event raised when a module is unloaded.
    /// </summary>
    event EventHandler<ModuleUnloadedEventArgs>? ModuleUnloaded;

    /// <summary>
    /// Event raised when a step operation completes.
    /// </summary>
    event EventHandler<StepCompleteEventArgs>? StepCompleted;

    /// <summary>
    /// Event raised when an exception is thrown (first-chance or unhandled).
    /// </summary>
    event EventHandler<ExceptionHitEventArgs>? ExceptionHit;

    /// <summary>
    /// Gets whether a debug session is active.
    /// </summary>
    bool IsAttached { get; }

    /// <summary>
    /// Gets the current session state.
    /// </summary>
    SessionState CurrentState { get; }

    /// <summary>
    /// Gets the pause reason when in paused state.
    /// </summary>
    PauseReason? CurrentPauseReason { get; }

    /// <summary>
    /// Gets the current source location when paused.
    /// </summary>
    SourceLocation? CurrentLocation { get; }

    /// <summary>
    /// Gets the active thread ID when paused.
    /// </summary>
    int? ActiveThreadId { get; }

    /// <summary>
    /// Checks if a process is a .NET process.
    /// </summary>
    /// <param name="pid">Process ID to check.</param>
    /// <returns>True if the process is running .NET code.</returns>
    bool IsNetProcess(int pid);

    /// <summary>
    /// Gets information about a process.
    /// </summary>
    /// <param name="pid">Process ID.</param>
    /// <returns>Process information, or null if process not found.</returns>
    ProcessInfo? GetProcessInfo(int pid);

    /// <summary>
    /// Attaches to a running .NET process.
    /// </summary>
    /// <param name="pid">Process ID to attach to.</param>
    /// <param name="timeout">Timeout for the attach operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process information.</returns>
    Task<ProcessInfo> AttachAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches a process under debugger control.
    /// </summary>
    /// <param name="program">Path to the executable or DLL.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="cwd">Working directory.</param>
    /// <param name="env">Environment variables.</param>
    /// <param name="stopAtEntry">Whether to pause at entry point.</param>
    /// <param name="timeout">Timeout for the launch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process information.</returns>
    Task<ProcessInfo> LaunchAsync(
        string program,
        string[]? args = null,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        bool stopAtEntry = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches from the current process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DetachAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates the debuggee process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task TerminateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates all currently loaded modules in the debuggee.
    /// </summary>
    /// <returns>List of loaded modules.</returns>
    IReadOnlyList<LoadedModuleInfo> GetLoadedModules();

    /// <summary>
    /// Continues execution of the paused debuggee.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
    Task ContinueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Steps through code in the specified mode.
    /// </summary>
    /// <param name="mode">The stepping mode (In, Over, Out).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
    Task StepAsync(StepMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses execution of a running debuggee process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of threads with their locations after pause.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not attached.</exception>
    Task<IReadOnlyList<ThreadInfo>> PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all managed threads in the debuggee process.
    /// </summary>
    /// <returns>List of thread information.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not attached.</exception>
    IReadOnlyList<ThreadInfo> GetThreads();

    /// <summary>
    /// Gets the stack frames for a specified thread.
    /// </summary>
    /// <param name="threadId">Thread ID (default: current thread).</param>
    /// <param name="startFrame">Start from frame N (for pagination).</param>
    /// <param name="maxFrames">Maximum frames to return.</param>
    /// <returns>Stack frames and total count.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
    (IReadOnlyList<Models.Inspection.StackFrame> Frames, int TotalFrames) GetStackFrames(int? threadId = null, int startFrame = 0, int maxFrames = 20);

    /// <summary>
    /// Gets variables for a specified stack frame.
    /// </summary>
    /// <param name="threadId">Thread ID (default: current thread).</param>
    /// <param name="frameIndex">Frame index (0 = top of stack).</param>
    /// <param name="scope">Which variables to return (all, locals, arguments, this).</param>
    /// <param name="expandPath">Variable path to expand children (e.g., "user.Address").</param>
    /// <returns>List of variables.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
    IReadOnlyList<Variable> GetVariables(int? threadId = null, int frameIndex = 0, string scope = "all", string? expandPath = null);

    /// <summary>
    /// Evaluates a C# expression in the debuggee context.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="threadId">Thread context (null = current thread).</param>
    /// <param name="frameIndex">Stack frame context (0 = top).</param>
    /// <param name="timeoutMs">Evaluation timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation result with value or error.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
    Task<EvaluationResult> EvaluateAsync(
        string expression,
        int? threadId = null,
        int frameIndex = 0,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default);

    // Memory inspection operations

    /// <summary>
    /// Inspects a heap object's contents including all fields.
    /// </summary>
    /// <param name="objectRef">Object reference (variable name or expression result).</param>
    /// <param name="depth">Maximum depth for nested object expansion (default: 1).</param>
    /// <param name="threadId">Thread context (null = current thread).</param>
    /// <param name="frameIndex">Stack frame context (0 = top).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Object inspection result with fields and values.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
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
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
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
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
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
    /// <exception cref="InvalidOperationException">Thrown when not attached or not paused.</exception>
    Task<TypeLayout> GetTypeLayoutAsync(
        string typeName,
        bool includeInherited = true,
        bool includePadding = true,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a loaded module.
/// </summary>
public sealed class LoadedModuleInfo
{
    /// <summary>Path to the module file.</summary>
    public required string ModulePath { get; init; }

    /// <summary>Whether the module is dynamic.</summary>
    public required bool IsDynamic { get; init; }

    /// <summary>Whether the module is in-memory.</summary>
    public required bool IsInMemory { get; init; }

    /// <summary>The native ICorDebugModule handle.</summary>
    public required object NativeModule { get; init; }
}

/// <summary>
/// Event args for session state changes.
/// </summary>
public sealed class SessionStateChangedEventArgs : EventArgs
{
    /// <summary>The new session state.</summary>
    public required SessionState NewState { get; init; }

    /// <summary>The previous session state.</summary>
    public required SessionState OldState { get; init; }

    /// <summary>The reason for pause (if NewState is Paused).</summary>
    public PauseReason? PauseReason { get; init; }

    /// <summary>The current location (if NewState is Paused).</summary>
    public SourceLocation? Location { get; init; }

    /// <summary>The active thread ID (if NewState is Paused).</summary>
    public int? ThreadId { get; init; }
}

/// <summary>
/// Event args for breakpoint hit events.
/// </summary>
public sealed class BreakpointHitEventArgs : EventArgs
{
    /// <summary>The thread that hit the breakpoint.</summary>
    public required int ThreadId { get; init; }

    /// <summary>The source location where breakpoint was hit (may be partial without PDB).</summary>
    public required SourceLocation? Location { get; init; }

    /// <summary>Timestamp of the hit.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Metadata token of the method where breakpoint was hit.</summary>
    public int? MethodToken { get; init; }

    /// <summary>IL offset within the method.</summary>
    public int? ILOffset { get; init; }

    /// <summary>Path to the module (assembly) containing the breakpoint.</summary>
    public string? ModulePath { get; init; }
}

/// <summary>
/// Event args for module loaded events.
/// </summary>
public sealed class ModuleLoadedEventArgs : EventArgs
{
    /// <summary>Path to the loaded module (assembly).</summary>
    public required string ModulePath { get; init; }

    /// <summary>Base address of the module in memory.</summary>
    public required ulong BaseAddress { get; init; }

    /// <summary>Size of the module in bytes.</summary>
    public required uint Size { get; init; }

    /// <summary>Whether the module is dynamic (e.g., Reflection.Emit).</summary>
    public required bool IsDynamic { get; init; }

    /// <summary>Whether the module is in-memory (no file on disk).</summary>
    public required bool IsInMemory { get; init; }

    /// <summary>The native ICorDebugModule handle for breakpoint binding.</summary>
    public required object NativeModule { get; init; }
}

/// <summary>
/// Event args for module unloaded events.
/// </summary>
public sealed class ModuleUnloadedEventArgs : EventArgs
{
    /// <summary>Path to the unloaded module (assembly).</summary>
    public required string ModulePath { get; init; }
}

/// <summary>
/// Event args for step complete events.
/// </summary>
public sealed class StepCompleteEventArgs : EventArgs
{
    /// <summary>The thread that completed the step.</summary>
    public required int ThreadId { get; init; }

    /// <summary>The source location after the step (may be null without PDB).</summary>
    public required SourceLocation? Location { get; init; }

    /// <summary>The step mode that was executed.</summary>
    public required StepMode StepMode { get; init; }

    /// <summary>The reason the step completed (normal, breakpoint, exception, etc.).</summary>
    public required StepCompleteReason Reason { get; init; }

    /// <summary>Timestamp when step completed.</summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Reason why a step operation completed.
/// </summary>
public enum StepCompleteReason
{
    /// <summary>Step completed normally at the next instruction.</summary>
    Normal,

    /// <summary>Step completed because a breakpoint was hit.</summary>
    Breakpoint,

    /// <summary>Step completed because an exception was thrown.</summary>
    Exception,

    /// <summary>Step was interrupted/cancelled.</summary>
    Interrupted
}

/// <summary>
/// Event args for exception hit events.
/// </summary>
public sealed class ExceptionHitEventArgs : EventArgs
{
    /// <summary>The thread that threw the exception.</summary>
    public required int ThreadId { get; init; }

    /// <summary>The source location where exception was thrown.</summary>
    public required SourceLocation? Location { get; init; }

    /// <summary>Timestamp when exception was thrown.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Full type name of the exception (e.g., System.NullReferenceException).</summary>
    public required string ExceptionType { get; init; }

    /// <summary>Exception message (may be empty if extraction failed).</summary>
    public required string ExceptionMessage { get; init; }

    /// <summary>True if first-chance exception, false if unhandled.</summary>
    public required bool IsFirstChance { get; init; }

    /// <summary>True if the exception is unhandled.</summary>
    public required bool IsUnhandled { get; init; }
}
