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
/// MCP tool for enabling and disabling breakpoints.
/// </summary>
[McpServerToolType]
public sealed class BreakpointEnableTool
{
    private readonly IBreakpointManager _breakpointManager;
    private readonly ILogger<BreakpointEnableTool> _logger;

    public BreakpointEnableTool(
        IBreakpointManager breakpointManager,
        ILogger<BreakpointEnableTool> logger)
    {
        _breakpointManager = breakpointManager;
        _logger = logger;
    }

    /// <summary>
    /// Enable or disable a breakpoint by ID.
    /// </summary>
    /// <param name="id">Breakpoint ID to enable or disable.</param>
    /// <param name="enabled">True to enable, false to disable. Default: true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated breakpoint information or error response.</returns>
    [McpServerTool(Name = "breakpoint_enable")]
    [Description("Enable or disable a breakpoint by ID")]
    public async Task<string> EnableBreakpointAsync(
        [Description("Breakpoint ID to enable or disable")] string id,
        [Description("True to enable, false to disable")] bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("breakpoint_enable", JsonSerializer.Serialize(new { id, enabled }));

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.ToolError("breakpoint_enable", ErrorCodes.BreakpointNotFound);
                return CreateErrorResponse(
                    ErrorCodes.BreakpointNotFound,
                    "Breakpoint ID cannot be empty");
            }

            // Enable/disable the breakpoint
            var updatedBreakpoint = await _breakpointManager.SetBreakpointEnabledAsync(
                id, enabled, cancellationToken);

            stopwatch.Stop();
            _logger.ToolCompleted("breakpoint_enable", stopwatch.ElapsedMilliseconds);

            if (updatedBreakpoint == null)
            {
                _logger.ToolError("breakpoint_enable", ErrorCodes.BreakpointNotFound);
                return CreateErrorResponse(
                    ErrorCodes.BreakpointNotFound,
                    $"No breakpoint with ID '{id}'");
            }

            _logger.LogInformation("Breakpoint {BreakpointId} {Action}",
                id, enabled ? "enabled" : "disabled");

            // Return success response
            return JsonSerializer.Serialize(new
            {
                success = true,
                breakpoint = SerializeBreakpoint(updatedBreakpoint)
            }, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("breakpoint_enable", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.ToolError("breakpoint_enable", ErrorCodes.BreakpointNotFound);
            return CreateErrorResponse(
                ErrorCodes.BreakpointNotFound,
                $"Failed to enable/disable breakpoint: {ex.Message}",
                new { id, exceptionType = ex.GetType().Name });
        }
    }

    private static object SerializeBreakpoint(Breakpoint bp)
    {
        return new
        {
            id = bp.Id,
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
