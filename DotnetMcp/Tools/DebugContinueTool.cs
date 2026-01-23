using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for continuing execution of a paused debug session.
/// </summary>
[McpServerToolType]
public sealed class DebugContinueTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<DebugContinueTool> _logger;

    public DebugContinueTool(IDebugSessionManager sessionManager, ILogger<DebugContinueTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Continue execution of the paused process.
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds (default: 30000, min: 1000, max: 300000).</param>
    /// <returns>Updated session state after continuing.</returns>
    [McpServerTool(Name = "debug_continue")]
    [Description("Continue execution of the paused process")]
    public async Task<string> ContinueAsync(
        [Description("Timeout in milliseconds")] int timeout = 30000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("debug_continue", $"{{\"timeout\": {timeout}}}");

        try
        {
            // Validate timeout bounds
            if (timeout < 1000 || timeout > 300000)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    $"Timeout must be between 1000 and 300000 milliseconds (got {timeout})",
                    new { parameter = "timeout", value = timeout });
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("debug_continue", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("debug_continue", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot continue: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
            var updatedSession = await _sessionManager.ContinueAsync(cts.Token);

            stopwatch.Stop();
            _logger.ToolCompleted("debug_continue", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Continued execution for process {ProcessId}", updatedSession.ProcessId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                session = BuildSessionResponse(updatedSession)
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("debug_continue", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Continue operation timed out");
        }
        catch (InvalidOperationException ex)
        {
            _logger.ToolError("debug_continue", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.ToolError("debug_continue", "CONTINUE_FAILED");
            return CreateErrorResponse("CONTINUE_FAILED", $"Failed to continue: {ex.Message}");
        }
    }

    private static string CreateErrorResponse(string code, string message, object? details = null)
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = new
            {
                code,
                message,
                details
            }
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object BuildSessionResponse(DebugSession session)
    {
        var response = new Dictionary<string, object?>
        {
            ["processId"] = session.ProcessId,
            ["processName"] = session.ProcessName,
            ["state"] = session.State.ToString().ToLowerInvariant(),
            ["launchMode"] = session.LaunchMode.ToString().ToLowerInvariant()
        };

        if (session.State == SessionState.Paused)
        {
            if (session.PauseReason.HasValue)
            {
                response["pauseReason"] = session.PauseReason.Value.ToString().ToLowerInvariant();
            }

            if (session.CurrentLocation != null)
            {
                response["location"] = new
                {
                    file = session.CurrentLocation.File,
                    line = session.CurrentLocation.Line,
                    column = session.CurrentLocation.Column,
                    functionName = session.CurrentLocation.FunctionName
                };
            }

            if (session.ActiveThreadId.HasValue)
            {
                response["activeThreadId"] = session.ActiveThreadId.Value;
            }
        }

        return response;
    }
}
