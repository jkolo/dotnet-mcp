using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for launching a .NET process under debugger control.
/// </summary>
[McpServerToolType]
public sealed class DebugLaunchTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<DebugLaunchTool> _logger;

    public DebugLaunchTool(IDebugSessionManager sessionManager, ILogger<DebugLaunchTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Launch a .NET executable under debugger control.
    /// </summary>
    /// <param name="program">Path to the .NET executable or DLL to debug.</param>
    /// <param name="args">Command-line arguments to pass to the program.</param>
    /// <param name="cwd">Working directory for the launched process.</param>
    /// <param name="env">Environment variables to set for the process (JSON object).</param>
    /// <param name="stopAtEntry">Pause at entry point before executing user code.</param>
    /// <param name="timeout">Maximum time to wait for launch in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Debug session information or error response.</returns>
    [McpServerTool(Name = "debug_launch")]
    [Description("Launch a .NET executable under debugger control")]
    public async Task<string> LaunchAsync(
        [Description("Path to the .NET executable or DLL to debug")] string program,
        [Description("Command-line arguments to pass to the program")] string[]? args = null,
        [Description("Working directory for the launched process")] string? cwd = null,
        [Description("Environment variables to set for the process (JSON object)")] string? env = null,
        [Description("Pause at entry point before executing user code")] bool stopAtEntry = true,
        [Description("Maximum time to wait for launch in milliseconds")] int timeout = 30000,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("debug_launch", JsonSerializer.Serialize(new { program, args, cwd, stopAtEntry, timeout }));

        try
        {
            // Validate program path
            if (string.IsNullOrWhiteSpace(program))
            {
                _logger.ToolError("debug_launch", ErrorCodes.InvalidPath);
                return CreateErrorResponse(ErrorCodes.InvalidPath, "Program path is required");
            }

            // Validate timeout bounds
            if (timeout < 1000 || timeout > 300000)
            {
                _logger.ToolError("debug_launch", ErrorCodes.Timeout);
                return CreateErrorResponse(ErrorCodes.Timeout, $"Timeout must be between 1000 and 300000 milliseconds, got: {timeout}");
            }

            // Check if already attached
            if (_sessionManager.CurrentSession != null)
            {
                _logger.ToolError("debug_launch", ErrorCodes.AlreadyAttached);
                return CreateErrorResponse(
                    ErrorCodes.AlreadyAttached,
                    $"Already attached to process {_sessionManager.CurrentSession.ProcessId}. Disconnect first.",
                    new { currentPid = _sessionManager.CurrentSession.ProcessId });
            }

            // Parse environment variables if provided
            Dictionary<string, string>? envDict = null;
            if (!string.IsNullOrWhiteSpace(env))
            {
                try
                {
                    envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(env);
                }
                catch (JsonException ex)
                {
                    _logger.ToolError("debug_launch", "INVALID_ENV");
                    return CreateErrorResponse("INVALID_ENV", $"Invalid environment variables JSON: {ex.Message}");
                }
            }

            // Create cancellation token with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Attempt to launch
            var session = await _sessionManager.LaunchAsync(
                program,
                args,
                cwd,
                envDict,
                stopAtEntry,
                TimeSpan.FromMilliseconds(timeout),
                linkedCts.Token);

            stopwatch.Stop();
            _logger.ToolCompleted("debug_launch", stopwatch.ElapsedMilliseconds);

            // Return success response
            var response = new
            {
                success = true,
                session = new
                {
                    processId = session.ProcessId,
                    processName = session.ProcessName,
                    executablePath = session.ExecutablePath,
                    runtimeVersion = session.RuntimeVersion,
                    state = session.State.ToString().ToLowerInvariant(),
                    launchMode = session.LaunchMode.ToString().ToLowerInvariant(),
                    attachedAt = session.AttachedAt.ToString("O"),
                    pauseReason = session.PauseReason?.ToString().ToLowerInvariant(),
                    commandLineArgs = session.CommandLineArgs,
                    workingDirectory = session.WorkingDirectory
                }
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (FileNotFoundException)
        {
            _logger.ToolError("debug_launch", ErrorCodes.InvalidPath);
            return CreateErrorResponse(ErrorCodes.InvalidPath, $"Program not found: {program}", new { program });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already active"))
        {
            _logger.ToolError("debug_launch", ErrorCodes.AlreadyAttached);
            return CreateErrorResponse(ErrorCodes.AlreadyAttached, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("dbgshim"))
        {
            _logger.ToolError("debug_launch", ErrorCodes.LaunchFailed);
            return CreateErrorResponse(
                ErrorCodes.LaunchFailed,
                "Could not find dbgshim library. Ensure .NET SDK is installed.",
                new { program, originalError = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.ToolError("debug_launch", ErrorCodes.PermissionDenied);
            return CreateErrorResponse(
                ErrorCodes.PermissionDenied,
                $"Insufficient privileges to launch process. You may need elevated permissions.",
                new { program, originalError = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.ToolError("debug_launch", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("debug_launch", ErrorCodes.Timeout);
            _logger.OperationTimeout(timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, $"Launch operation timed out after {timeout}ms", new { program, timeout });
        }
        catch (Exception ex)
        {
            _logger.ToolError("debug_launch", ErrorCodes.LaunchFailed);
            return CreateErrorResponse(
                ErrorCodes.LaunchFailed,
                $"Failed to launch process: {ex.Message}",
                new { program, exceptionType = ex.GetType().Name });
        }
    }

    private static string CreateErrorResponse(string code, string message, object? details = null)
    {
        var response = new
        {
            success = false,
            error = new
            {
                code,
                message,
                details
            }
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
