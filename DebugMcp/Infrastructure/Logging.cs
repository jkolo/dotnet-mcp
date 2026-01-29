using Microsoft.Extensions.Logging;

namespace DebugMcp.Infrastructure;

/// <summary>
/// Logging categories and extension methods for structured logging.
/// </summary>
public static partial class Logging
{
    // Category names
    public const string DebugSession = "DebugMcp.DebugSession";
    public const string ProcessDebugger = "DebugMcp.ProcessDebugger";
    public const string Tools = "DebugMcp.Tools";

    // Session lifecycle events
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Attaching to process {ProcessId}")]
    public static partial void AttachingToProcess(this ILogger logger, int processId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Successfully attached to process {ProcessId} ({ProcessName}, {RuntimeVersion})")]
    public static partial void AttachedToProcess(this ILogger logger, int processId, string processName, string runtimeVersion);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Launching process: {Program}")]
    public static partial void LaunchingProcess(this ILogger logger, string program);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Successfully launched process {ProcessId} ({ProcessName})")]
    public static partial void LaunchedProcess(this ILogger logger, int processId, string processName);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Disconnecting from process {ProcessId} (terminate: {Terminate})")]
    public static partial void DisconnectingFromProcess(this ILogger logger, int processId, bool terminate);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Disconnected from process {ProcessId}")]
    public static partial void DisconnectedFromProcess(this ILogger logger, int processId);

    // State events
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Session state changed: {OldState} -> {NewState}")]
    public static partial void SessionStateChanged(this ILogger logger, string oldState, string newState);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Process paused at {Location} (reason: {PauseReason}, thread: {ThreadId})")]
    public static partial void ProcessPaused(this ILogger logger, string location, string pauseReason, int threadId);

    // Error events
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Failed to attach to process {ProcessId}: {ErrorCode} - {ErrorMessage}")]
    public static partial void AttachFailed(this ILogger logger, int processId, string errorCode, string errorMessage);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Error,
        Message = "Failed to launch process {Program}: {ErrorCode} - {ErrorMessage}")]
    public static partial void LaunchFailed(this ILogger logger, string program, string errorCode, string errorMessage);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Error,
        Message = "Failed to disconnect from process {ProcessId}: {ErrorMessage}")]
    public static partial void DisconnectFailed(this ILogger logger, int processId, string errorMessage);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Warning,
        Message = "Operation timed out after {TimeoutMs}ms")]
    public static partial void OperationTimeout(this ILogger logger, int timeoutMs);

    // Tool invocation events
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "Tool invoked: {ToolName} with parameters: {Parameters}")]
    public static partial void ToolInvoked(this ILogger logger, string toolName, string parameters);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "Tool {ToolName} completed in {ElapsedMs}ms")]
    public static partial void ToolCompleted(this ILogger logger, string toolName, long elapsedMs);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "Tool {ToolName} returned error: {ErrorCode}")]
    public static partial void ToolError(this ILogger logger, string toolName, string errorCode);
}
