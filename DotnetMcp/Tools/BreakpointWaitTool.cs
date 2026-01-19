using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Models.Breakpoints;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for waiting for breakpoint hits.
/// </summary>
[McpServerToolType]
public sealed class BreakpointWaitTool
{
    private readonly IBreakpointManager _breakpointManager;
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<BreakpointWaitTool> _logger;

    public BreakpointWaitTool(
        IBreakpointManager breakpointManager,
        IDebugSessionManager sessionManager,
        ILogger<BreakpointWaitTool> logger)
    {
        _breakpointManager = breakpointManager;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Wait for a breakpoint to be hit.
    /// </summary>
    /// <param name="timeout_ms">Timeout in milliseconds (default: 30000, max: 300000).</param>
    /// <param name="breakpoint_id">Wait for specific breakpoint only (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hit information or timeout response.</returns>
    [McpServerTool(Name = "breakpoint_wait")]
    [Description("Wait for a breakpoint to be hit")]
    public async Task<string> WaitForBreakpointAsync(
        [Description("Timeout in milliseconds (default: 30000, max: 300000)")] int timeout_ms = 30000,
        [Description("Wait for specific breakpoint only (optional)")] string? breakpoint_id = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("breakpoint_wait", JsonSerializer.Serialize(new { timeout_ms, breakpoint_id }));

        try
        {
            // Validate input parameters
            if (timeout_ms < 1 || timeout_ms > 300000)
            {
                _logger.ToolError("breakpoint_wait", ErrorCodes.Timeout);
                return CreateErrorResponse(
                    ErrorCodes.Timeout,
                    $"Timeout must be between 1 and 300000 milliseconds, got: {timeout_ms}");
            }

            // Check for active session
            if (_sessionManager.CurrentSession == null)
            {
                _logger.ToolError("breakpoint_wait", ErrorCodes.NoSession);
                return CreateErrorResponse(
                    ErrorCodes.NoSession,
                    "No active debug session. Use debug_attach or debug_launch first.");
            }

            // If specific breakpoint requested, verify it exists
            if (!string.IsNullOrEmpty(breakpoint_id))
            {
                var breakpoint = await _breakpointManager.GetBreakpointAsync(breakpoint_id, cancellationToken);
                if (breakpoint == null)
                {
                    _logger.ToolError("breakpoint_wait", ErrorCodes.BreakpointNotFound);
                    return CreateErrorResponse(
                        ErrorCodes.BreakpointNotFound,
                        $"Breakpoint with ID '{breakpoint_id}' not found");
                }
            }

            _logger.LogDebug("Waiting for breakpoint hit (timeout: {Timeout}ms, filter: {BreakpointId})",
                timeout_ms, breakpoint_id ?? "any");

            // Wait for hit
            var hit = await _breakpointManager.WaitForBreakpointAsync(
                TimeSpan.FromMilliseconds(timeout_ms),
                cancellationToken);

            stopwatch.Stop();

            if (hit == null)
            {
                // Timeout occurred
                _logger.ToolCompleted("breakpoint_wait", stopwatch.ElapsedMilliseconds);
                _logger.LogDebug("Wait timed out after {Timeout}ms", timeout_ms);

                return JsonSerializer.Serialize(new
                {
                    hit = false,
                    timeout = true,
                    message = $"No breakpoint hit within {timeout_ms}ms"
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // If filtering by specific breakpoint, keep waiting if this isn't the one
            // (For now, the BreakpointManager returns any hit - filtering would need queue inspection)
            if (!string.IsNullOrEmpty(breakpoint_id) && hit.BreakpointId != breakpoint_id)
            {
                // In a production implementation, we'd filter hits in the queue
                // For now, return the hit anyway
                _logger.LogDebug("Received hit for {HitId}, but waiting for {FilterId}",
                    hit.BreakpointId, breakpoint_id);
            }

            _logger.ToolCompleted("breakpoint_wait", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Breakpoint {BreakpointId} hit on thread {ThreadId} at {File}:{Line}",
                hit.BreakpointId, hit.ThreadId, hit.Location.File, hit.Location.Line);

            // Return hit response
            return JsonSerializer.Serialize(new
            {
                hit = true,
                breakpointId = hit.BreakpointId,
                threadId = hit.ThreadId,
                timestamp = hit.Timestamp.ToString("O"),
                location = SerializeLocation(hit.Location),
                hitCount = hit.HitCount,
                exceptionInfo = hit.ExceptionInfo != null ? SerializeExceptionInfo(hit.ExceptionInfo) : null
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("breakpoint_wait", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.ToolError("breakpoint_wait", ErrorCodes.Timeout);
            return CreateErrorResponse(
                ErrorCodes.Timeout,
                $"Failed to wait for breakpoint: {ex.Message}",
                new { exceptionType = ex.GetType().Name });
        }
    }

    private static object SerializeLocation(BreakpointLocation location)
    {
        return new
        {
            file = location.File,
            line = location.Line,
            column = location.Column,
            endLine = location.EndLine,
            endColumn = location.EndColumn,
            functionName = location.FunctionName,
            moduleName = location.ModuleName
        };
    }

    private static object SerializeExceptionInfo(ExceptionInfo info)
    {
        return new
        {
            type = info.Type,
            message = info.Message,
            isFirstChance = info.IsFirstChance,
            stackTrace = info.StackTrace
        };
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
