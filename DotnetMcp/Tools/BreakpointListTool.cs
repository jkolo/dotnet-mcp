using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Models.Breakpoints;
using DotnetMcp.Services.Breakpoints;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for listing all breakpoints in the current debug session.
/// </summary>
[McpServerToolType]
public sealed class BreakpointListTool
{
    private readonly IBreakpointManager _breakpointManager;
    private readonly ILogger<BreakpointListTool> _logger;

    public BreakpointListTool(
        IBreakpointManager breakpointManager,
        ILogger<BreakpointListTool> logger)
    {
        _breakpointManager = breakpointManager;
        _logger = logger;
    }

    /// <summary>
    /// List all breakpoints in the current debug session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all breakpoints with their details.</returns>
    [McpServerTool(Name = "breakpoint_list")]
    [Description("List all breakpoints in the current debug session")]
    public async Task<string> ListBreakpointsAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("breakpoint_list", "{}");

        try
        {
            // Get all breakpoints
            var breakpoints = await _breakpointManager.GetBreakpointsAsync(cancellationToken);

            stopwatch.Stop();
            _logger.ToolCompleted("breakpoint_list", stopwatch.ElapsedMilliseconds);
            _logger.LogDebug("Listed {Count} breakpoints", breakpoints.Count);

            // Serialize breakpoints
            var serializedBreakpoints = breakpoints.Select(bp => new
            {
                id = bp.Id,
                type = "source", // Could be "source", "function", or "exception"
                location = new
                {
                    file = bp.Location.File,
                    line = bp.Location.Line,
                    column = bp.Location.Column,
                    endLine = bp.Location.EndLine,
                    endColumn = bp.Location.EndColumn,
                    functionName = bp.Location.FunctionName,
                    moduleName = bp.Location.ModuleName
                },
                state = bp.State.ToString().ToLowerInvariant(),
                enabled = bp.Enabled,
                verified = bp.Verified,
                condition = bp.Condition,
                hitCount = bp.HitCount,
                message = bp.Message
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                breakpoints = serializedBreakpoints,
                count = breakpoints.Count
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("breakpoint_list", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.ToolError("breakpoint_list", ErrorCodes.NoSession);
            return CreateErrorResponse(
                ErrorCodes.NoSession,
                $"Failed to list breakpoints: {ex.Message}",
                new { exceptionType = ex.GetType().Name });
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
