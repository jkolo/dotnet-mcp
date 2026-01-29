using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for querying the current debug session state.
/// </summary>
[McpServerToolType]
public sealed class DebugStateTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<DebugStateTool> _logger;

    public DebugStateTool(IDebugSessionManager sessionManager, ILogger<DebugStateTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Get the current state of the debug session.
    /// </summary>
    /// <returns>Current session state and details.</returns>
    [McpServerTool(Name = "debug_state")]
    [Description("Get the current state of the debug session")]
    public string GetState()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("debug_state", "{}");

        try
        {
            var state = _sessionManager.GetCurrentState();
            var session = _sessionManager.CurrentSession;

            stopwatch.Stop();
            _logger.ToolCompleted("debug_state", stopwatch.ElapsedMilliseconds);

            if (state == SessionState.Disconnected || session == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    state = "disconnected",
                    session = (object?)null
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Build response with session details
            var response = new
            {
                success = true,
                state = state.ToString().ToLowerInvariant(),
                session = BuildSessionResponse(session)
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.ToolError("debug_state", "QUERY_FAILED");

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = new
                {
                    code = "QUERY_FAILED",
                    message = $"Failed to query session state: {ex.Message}"
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static object BuildSessionResponse(DebugSession session)
    {
        var response = new Dictionary<string, object?>
        {
            ["processId"] = session.ProcessId,
            ["processName"] = session.ProcessName,
            ["executablePath"] = session.ExecutablePath,
            ["runtimeVersion"] = session.RuntimeVersion,
            ["state"] = session.State.ToString().ToLowerInvariant(),
            ["launchMode"] = session.LaunchMode.ToString().ToLowerInvariant(),
            ["attachedAt"] = session.AttachedAt.ToString("O")
        };

        // Include pause information if paused
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
                    functionName = session.CurrentLocation.FunctionName,
                    moduleName = session.CurrentLocation.ModuleName
                };
            }

            if (session.ActiveThreadId.HasValue)
            {
                response["activeThreadId"] = session.ActiveThreadId.Value;
            }
        }

        // Include launch-specific info
        if (session.LaunchMode == LaunchMode.Launch)
        {
            if (session.CommandLineArgs?.Length > 0)
            {
                response["commandLineArgs"] = session.CommandLineArgs;
            }

            if (!string.IsNullOrEmpty(session.WorkingDirectory))
            {
                response["workingDirectory"] = session.WorkingDirectory;
            }
        }

        return response;
    }
}
