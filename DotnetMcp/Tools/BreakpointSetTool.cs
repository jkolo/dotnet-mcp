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
/// MCP tool for setting breakpoints at source locations.
/// </summary>
[McpServerToolType]
public sealed class BreakpointSetTool
{
    private readonly IBreakpointManager _breakpointManager;
    private readonly IDebugSessionManager _sessionManager;
    private readonly IPdbSymbolReader _pdbReader;
    private readonly ILogger<BreakpointSetTool> _logger;

    public BreakpointSetTool(
        IBreakpointManager breakpointManager,
        IDebugSessionManager sessionManager,
        IPdbSymbolReader pdbReader,
        ILogger<BreakpointSetTool> logger)
    {
        _breakpointManager = breakpointManager;
        _sessionManager = sessionManager;
        _pdbReader = pdbReader;
        _logger = logger;
    }

    /// <summary>
    /// Set a breakpoint at a source location.
    /// </summary>
    /// <param name="file">Source file path (absolute or relative to project).</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="column">Optional 1-based column for targeting specific sequence point (lambda/inline).</param>
    /// <param name="condition">Optional C# condition expression (breakpoint only triggers when true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Breakpoint information or error response.</returns>
    [McpServerTool(Name = "breakpoint_set")]
    [Description("Set a breakpoint at a source location (file and line)")]
    public async Task<string> SetBreakpointAsync(
        [Description("Source file path (absolute or relative to project)")] string file,
        [Description("1-based line number")] int line,
        [Description("1-based column for targeting lambdas/inline statements (optional)")] int? column = null,
        [Description("C# condition expression (breakpoint only triggers when true)")] string? condition = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("breakpoint_set", JsonSerializer.Serialize(new { file, line, column, condition }));

        try
        {
            // Validate input parameters
            if (string.IsNullOrWhiteSpace(file))
            {
                _logger.ToolError("breakpoint_set", ErrorCodes.InvalidFile);
                return CreateErrorResponse(ErrorCodes.InvalidFile, "File path cannot be empty");
            }

            if (line < 1)
            {
                _logger.ToolError("breakpoint_set", ErrorCodes.InvalidLine);
                return CreateErrorResponse(ErrorCodes.InvalidLine, $"Line must be >= 1, got: {line}");
            }

            if (column.HasValue && column.Value < 1)
            {
                _logger.ToolError("breakpoint_set", ErrorCodes.InvalidColumn);
                return CreateErrorResponse(ErrorCodes.InvalidColumn, $"Column must be >= 1, got: {column}");
            }

            // Check for active session (optional - breakpoint can be pending)
            var hasSession = _sessionManager.CurrentSession != null;
            if (!hasSession)
            {
                _logger.LogDebug("No active debug session, breakpoint will be pending");
            }

            // Set the breakpoint
            var breakpoint = await _breakpointManager.SetBreakpointAsync(
                file, line, column, condition, cancellationToken);

            stopwatch.Stop();
            _logger.ToolCompleted("breakpoint_set", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Set breakpoint {BreakpointId} at {File}:{Line} (state: {State})",
                breakpoint.Id, file, line, breakpoint.State);

            // Check if this was a duplicate (condition might have changed)
            var isDuplicate = breakpoint.Message?.Contains("already exists") == true;

            // Return success response
            return JsonSerializer.Serialize(new
            {
                success = true,
                breakpoint = SerializeBreakpoint(breakpoint),
                duplicate = isDuplicate ? true : (bool?)null
            }, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        }
        catch (ArgumentException ex)
        {
            _logger.ToolError("breakpoint_set", ErrorCodes.InvalidCondition);
            return CreateErrorResponse(ErrorCodes.InvalidCondition, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.ToolError("breakpoint_set", ErrorCodes.InvalidFile);
            return await CreateInvalidLineResponseAsync(file, line, ex.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("breakpoint_set", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.ToolError("breakpoint_set", ErrorCodes.InvalidLine);
            return CreateErrorResponse(
                ErrorCodes.InvalidLine,
                $"Failed to set breakpoint: {ex.Message}",
                new { file, line, exceptionType = ex.GetType().Name });
        }
    }

    private async Task<string> CreateInvalidLineResponseAsync(string file, int requestedLine, string originalMessage)
    {
        // Try to find nearest valid line
        int? nearestLine = null;
        var session = _sessionManager.CurrentSession;

        // If we have a session, try to find the nearest valid line
        // For now, just return the error without suggestions
        // TODO: Implement module enumeration to find assembly path

        if (nearestLine.HasValue)
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidLine,
                originalMessage,
                new { requestedLine, nearestValidLine = nearestLine.Value });
        }

        return CreateErrorResponse(ErrorCodes.InvalidLine, originalMessage, new { file, line = requestedLine });
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
