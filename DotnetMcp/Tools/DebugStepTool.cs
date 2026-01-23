using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for stepping through code during debugging.
/// </summary>
[McpServerToolType]
public sealed class DebugStepTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<DebugStepTool> _logger;

    public DebugStepTool(IDebugSessionManager sessionManager, ILogger<DebugStepTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Step through code in the specified mode.
    /// </summary>
    /// <param name="mode">Step mode: "in" (step into), "over" (step over), or "out" (step out).</param>
    /// <param name="timeout">Timeout in milliseconds (default: 30000, min: 1000, max: 300000).</param>
    /// <returns>Updated session state after stepping.</returns>
    [McpServerTool(Name = "debug_step")]
    [Description("Step through code during debugging")]
    public async Task<string> StepAsync(
        [Description("Step mode: 'in', 'over', or 'out'")] string mode,
        [Description("Timeout in milliseconds")] int timeout = 30000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("debug_step", $"{{\"mode\": \"{mode}\", \"timeout\": {timeout}}}");

        try
        {
            // Validate timeout bounds
            if (timeout < 1000 || timeout > 300000)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    $"Timeout must be between 1000 and 300000 milliseconds (got {timeout})",
                    new { parameter = "timeout", value = timeout });
            }

            // Parse and validate step mode
            if (!TryParseStepMode(mode, out var stepMode))
            {
                _logger.ToolError("debug_step", ErrorCodes.InvalidParameter);
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    $"Invalid step mode: '{mode}'. Valid modes: in, over, out",
                    new { parameter = "mode", value = mode, validModes = new[] { "in", "over", "out" } });
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("debug_step", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("debug_step", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot step: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
            var updatedSession = await _sessionManager.StepAsync(stepMode, cts.Token);

            stopwatch.Stop();
            _logger.ToolCompleted("debug_step", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Stepped {Mode} for process {ProcessId}", mode, updatedSession.ProcessId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                stepMode = mode,
                session = BuildSessionResponse(updatedSession)
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("debug_step", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Step operation timed out");
        }
        catch (InvalidOperationException ex)
        {
            var errorCode = ex.Message.Contains("not paused") ? ErrorCodes.NotPaused : ErrorCodes.StepFailed;
            _logger.ToolError("debug_step", errorCode);
            return CreateErrorResponse(errorCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.ToolError("debug_step", ErrorCodes.StepFailed);
            return CreateErrorResponse(ErrorCodes.StepFailed, $"Failed to step: {ex.Message}");
        }
    }

    private static bool TryParseStepMode(string mode, out StepMode stepMode)
    {
        stepMode = default;

        switch (mode?.ToLowerInvariant())
        {
            case "in":
                stepMode = StepMode.In;
                return true;
            case "over":
                stepMode = StepMode.Over;
                return true;
            case "out":
                stepMode = StepMode.Out;
                return true;
            default:
                return false;
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
