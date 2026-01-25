using System.Text.Json.Serialization;

namespace DotnetMcp.Models;

/// <summary>
/// Standard error response structure for MCP tools.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>Error code for programmatic handling.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Additional context about the error.</summary>
    [JsonPropertyName("details")]
    public object? Details { get; init; }
}

/// <summary>
/// Error codes for debug session operations.
/// </summary>
public static class ErrorCodes
{
    /// <summary>PID does not exist.</summary>
    public const string ProcessNotFound = "PROCESS_NOT_FOUND";

    /// <summary>Process is not a .NET application.</summary>
    public const string NotDotNetProcess = "NOT_DOTNET_PROCESS";

    /// <summary>Insufficient privileges to debug.</summary>
    public const string PermissionDenied = "PERMISSION_DENIED";

    /// <summary>A debug session is already active.</summary>
    public const string SessionActive = "SESSION_ACTIVE";

    /// <summary>Already attached to a process.</summary>
    public const string AlreadyAttached = "ALREADY_ATTACHED";

    /// <summary>No active session to operate on.</summary>
    public const string NoSession = "NO_SESSION";

    /// <summary>ICorDebug attach failed.</summary>
    public const string AttachFailed = "ATTACH_FAILED";

    /// <summary>Process launch failed.</summary>
    public const string LaunchFailed = "LAUNCH_FAILED";

    /// <summary>Executable path invalid or not found.</summary>
    public const string InvalidPath = "INVALID_PATH";

    /// <summary>Operation timed out.</summary>
    public const string Timeout = "TIMEOUT";

    // Breakpoint-specific error codes

    /// <summary>Source file not found in any loaded module.</summary>
    public const string InvalidFile = "INVALID_FILE";

    /// <summary>Line number does not contain executable code.</summary>
    public const string InvalidLine = "INVALID_LINE";

    /// <summary>Column position invalid for breakpoint targeting.</summary>
    public const string InvalidColumn = "INVALID_COLUMN";

    /// <summary>Condition expression has syntax error.</summary>
    public const string InvalidCondition = "INVALID_CONDITION";

    /// <summary>Breakpoint ID does not exist.</summary>
    public const string BreakpointNotFound = "BREAKPOINT_NOT_FOUND";

    /// <summary>Breakpoint already exists at this location.</summary>
    public const string BreakpointExists = "BREAKPOINT_EXISTS";

    /// <summary>Condition evaluation failed at runtime.</summary>
    public const string EvalFailed = "EVAL_FAILED";

    // Execution control error codes

    /// <summary>Process is not paused (required for continue/step).</summary>
    public const string NotPaused = "NOT_PAUSED";

    /// <summary>Invalid parameter value.</summary>
    public const string InvalidParameter = "INVALID_PARAMETER";

    /// <summary>Step operation failed.</summary>
    public const string StepFailed = "STEP_FAILED";

    // Inspection error codes

    /// <summary>Specified thread ID is invalid or does not exist.</summary>
    public const string InvalidThread = "INVALID_THREAD";

    /// <summary>Specified frame index is out of range.</summary>
    public const string InvalidFrame = "INVALID_FRAME";

    /// <summary>Stack trace retrieval failed.</summary>
    public const string StackTraceFailed = "STACKTRACE_FAILED";

    /// <summary>Variable inspection failed.</summary>
    public const string VariablesFailed = "VARIABLES_FAILED";
}
