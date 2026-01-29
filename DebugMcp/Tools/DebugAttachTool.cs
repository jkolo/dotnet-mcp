using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for attaching the debugger to a running .NET process.
/// </summary>
[McpServerToolType]
public sealed class DebugAttachTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<DebugAttachTool> _logger;

    public DebugAttachTool(IDebugSessionManager sessionManager, ILogger<DebugAttachTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Attach debugger to a running .NET process by process ID.
    /// </summary>
    /// <param name="pid">Process ID of the .NET application to debug.</param>
    /// <param name="timeout">Maximum time to wait for attachment in milliseconds (default: 30000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Debug session information or error response.</returns>
    [McpServerTool(Name = "debug_attach")]
    [Description("Attach debugger to a running .NET process by process ID")]
    public async Task<string> AttachAsync(
        [Description("Process ID of the .NET application to debug")] int pid,
        [Description("Maximum time to wait for attachment in milliseconds")] int timeout = 30000,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("debug_attach", JsonSerializer.Serialize(new { pid, timeout }));

        try
        {
            // Validate input parameters
            if (pid <= 0)
            {
                _logger.ToolError("debug_attach", ErrorCodes.ProcessNotFound);
                return CreateErrorResponse(ErrorCodes.ProcessNotFound, $"Invalid process ID: {pid}");
            }

            if (timeout < 1000 || timeout > 300000)
            {
                _logger.ToolError("debug_attach", ErrorCodes.Timeout);
                return CreateErrorResponse(ErrorCodes.Timeout, $"Timeout must be between 1000 and 300000 milliseconds, got: {timeout}");
            }

            // Check if already attached
            if (_sessionManager.CurrentSession != null)
            {
                _logger.ToolError("debug_attach", ErrorCodes.AlreadyAttached);
                return CreateErrorResponse(
                    ErrorCodes.AlreadyAttached,
                    $"Already attached to process {_sessionManager.CurrentSession.ProcessId}. Disconnect first.",
                    new { currentPid = _sessionManager.CurrentSession.ProcessId });
            }

            // Create cancellation token with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Attempt to attach
            var session = await _sessionManager.AttachAsync(pid, TimeSpan.FromMilliseconds(timeout), linkedCts.Token);

            stopwatch.Stop();
            _logger.ToolCompleted("debug_attach", stopwatch.ElapsedMilliseconds);

            // Return success response
            return JsonSerializer.Serialize(new
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
                    attachedAt = session.AttachedAt.ToString("O")
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.ToolError("debug_attach", ErrorCodes.ProcessNotFound);
            return CreateErrorResponse(ErrorCodes.ProcessNotFound, ex.Message, new { pid });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not a .NET"))
        {
            _logger.ToolError("debug_attach", ErrorCodes.NotDotNetProcess);
            return CreateErrorResponse(ErrorCodes.NotDotNetProcess, ex.Message, new { pid });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("dbgshim"))
        {
            _logger.ToolError("debug_attach", ErrorCodes.AttachFailed);
            return CreateErrorResponse(
                ErrorCodes.AttachFailed,
                "Could not find dbgshim library. Ensure .NET SDK is installed.",
                new { pid, originalError = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already active"))
        {
            _logger.ToolError("debug_attach", ErrorCodes.AlreadyAttached);
            return CreateErrorResponse(ErrorCodes.AlreadyAttached, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.ToolError("debug_attach", ErrorCodes.PermissionDenied);
            return CreateErrorResponse(
                ErrorCodes.PermissionDenied,
                $"Insufficient privileges to attach to process {pid}. You may need to run with elevated permissions.",
                new { pid, originalError = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.ToolError("debug_attach", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("debug_attach", ErrorCodes.Timeout);
            _logger.OperationTimeout(timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, $"Attach operation timed out after {timeout}ms", new { pid, timeout });
        }
        catch (Exception ex)
        {
            _logger.ToolError("debug_attach", ErrorCodes.AttachFailed);
            return CreateErrorResponse(
                ErrorCodes.AttachFailed,
                $"Failed to attach to process {pid}: {ex.Message}",
                new { pid, exceptionType = ex.GetType().Name });
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
