using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services.Breakpoints;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for removing breakpoints.
/// </summary>
[McpServerToolType]
public sealed class BreakpointRemoveTool
{
    private readonly IBreakpointManager _breakpointManager;
    private readonly ILogger<BreakpointRemoveTool> _logger;

    public BreakpointRemoveTool(
        IBreakpointManager breakpointManager,
        ILogger<BreakpointRemoveTool> logger)
    {
        _breakpointManager = breakpointManager;
        _logger = logger;
    }

    /// <summary>
    /// Remove a breakpoint by ID.
    /// </summary>
    /// <param name="id">Breakpoint ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success message or error response.</returns>
    [McpServerTool(Name = "breakpoint_remove")]
    [Description("Remove a breakpoint by ID")]
    public async Task<string> RemoveBreakpointAsync(
        [Description("Breakpoint ID to remove")] string id,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("breakpoint_remove", JsonSerializer.Serialize(new { id }));

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.ToolError("breakpoint_remove", ErrorCodes.BreakpointNotFound);
                return CreateErrorResponse(
                    ErrorCodes.BreakpointNotFound,
                    "Breakpoint ID cannot be empty");
            }

            // Remove the breakpoint
            var removed = await _breakpointManager.RemoveBreakpointAsync(id, cancellationToken);

            stopwatch.Stop();
            _logger.ToolCompleted("breakpoint_remove", stopwatch.ElapsedMilliseconds);

            if (!removed)
            {
                _logger.ToolError("breakpoint_remove", ErrorCodes.BreakpointNotFound);
                return CreateErrorResponse(
                    ErrorCodes.BreakpointNotFound,
                    $"No breakpoint with ID '{id}'");
            }

            _logger.LogInformation("Removed breakpoint {BreakpointId}", id);

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Breakpoint {id} removed"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("breakpoint_remove", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.ToolError("breakpoint_remove", ErrorCodes.BreakpointNotFound);
            return CreateErrorResponse(
                ErrorCodes.BreakpointNotFound,
                $"Failed to remove breakpoint: {ex.Message}",
                new { id, exceptionType = ex.GetType().Name });
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
