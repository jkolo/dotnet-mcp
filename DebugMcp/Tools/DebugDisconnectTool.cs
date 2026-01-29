using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for disconnecting from the current debug session.
/// </summary>
[McpServerToolType]
public sealed class DebugDisconnectTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<DebugDisconnectTool> _logger;

    public DebugDisconnectTool(IDebugSessionManager sessionManager, ILogger<DebugDisconnectTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Disconnect from the current debug session.
    /// </summary>
    /// <param name="terminateProcess">Terminate the process instead of detaching (only for launched processes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Disconnect result.</returns>
    [McpServerTool(Name = "debug_disconnect")]
    [Description("Disconnect from the current debug session")]
    public async Task<string> DisconnectAsync(
        [Description("Terminate the process instead of detaching (only for launched processes)")] bool terminateProcess = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("debug_disconnect", JsonSerializer.Serialize(new { terminateProcess }));

        try
        {
            var currentSession = _sessionManager.CurrentSession;

            // Handle case where no session is active
            if (currentSession == null)
            {
                stopwatch.Stop();
                _logger.ToolCompleted("debug_disconnect", stopwatch.ElapsedMilliseconds);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    state = "disconnected",
                    message = "No active debug session",
                    previousSession = (object?)null
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Capture session info before disconnect
            var previousSessionInfo = new
            {
                processId = currentSession.ProcessId,
                processName = currentSession.ProcessName,
                launchMode = currentSession.LaunchMode.ToString().ToLowerInvariant()
            };

            // Determine if process will be terminated
            var willTerminate = terminateProcess && currentSession.LaunchMode == LaunchMode.Launch;

            // Perform disconnect
            await _sessionManager.DisconnectAsync(terminateProcess, cancellationToken);

            stopwatch.Stop();
            _logger.ToolCompleted("debug_disconnect", stopwatch.ElapsedMilliseconds);

            return JsonSerializer.Serialize(new
            {
                success = true,
                state = "disconnected",
                wasTerminated = willTerminate,
                previousSession = previousSessionInfo
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.ToolError("debug_disconnect", "DISCONNECT_FAILED");

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = new
                {
                    code = "DISCONNECT_FAILED",
                    message = $"Failed to disconnect: {ex.Message}"
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
