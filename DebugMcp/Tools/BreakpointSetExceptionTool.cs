using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services.Breakpoints;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for setting exception breakpoints.
/// </summary>
[McpServerToolType]
public sealed class BreakpointSetExceptionTool
{
    private readonly IBreakpointManager _breakpointManager;
    private readonly ILogger<BreakpointSetExceptionTool> _logger;

    public BreakpointSetExceptionTool(
        IBreakpointManager breakpointManager,
        ILogger<BreakpointSetExceptionTool> logger)
    {
        _breakpointManager = breakpointManager;
        _logger = logger;
    }

    /// <summary>
    /// Set an exception breakpoint to break when specific exception types are thrown.
    /// </summary>
    /// <param name="exception_type">Full exception type name (e.g., System.NullReferenceException).</param>
    /// <param name="break_on_first_chance">Break on first-chance exception (before catch blocks). Default: true.</param>
    /// <param name="break_on_second_chance">Break on second-chance exception (unhandled). Default: true.</param>
    /// <param name="include_subtypes">Also break on exception types that inherit from the specified type. Default: true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exception breakpoint information or error response.</returns>
    [McpServerTool(Name = "breakpoint_set_exception")]
    [Description("Set an exception breakpoint to break when specific exception types are thrown")]
    public async Task<string> SetExceptionBreakpointAsync(
        [Description("Full exception type name (e.g., System.NullReferenceException)")] string exception_type,
        [Description("Break on first-chance exception (before catch blocks)")] bool break_on_first_chance = true,
        [Description("Break on second-chance exception (unhandled)")] bool break_on_second_chance = true,
        [Description("Also break on exception types that inherit from the specified type")] bool include_subtypes = true,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("breakpoint_set_exception", JsonSerializer.Serialize(new
        {
            exception_type,
            break_on_first_chance,
            break_on_second_chance,
            include_subtypes
        }));

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(exception_type))
            {
                _logger.ToolError("breakpoint_set_exception", ErrorCodes.InvalidCondition);
                return CreateErrorResponse(
                    ErrorCodes.InvalidCondition,
                    "Exception type cannot be empty");
            }

            // Validate exception type format (basic check)
            if (!IsValidExceptionTypeName(exception_type))
            {
                _logger.ToolError("breakpoint_set_exception", ErrorCodes.InvalidCondition);
                return CreateErrorResponse(
                    ErrorCodes.InvalidCondition,
                    $"Invalid exception type format: '{exception_type}'. Expected fully qualified type name like 'System.NullReferenceException'");
            }

            // Must break on at least one type of exception
            if (!break_on_first_chance && !break_on_second_chance)
            {
                _logger.ToolError("breakpoint_set_exception", ErrorCodes.InvalidCondition);
                return CreateErrorResponse(
                    ErrorCodes.InvalidCondition,
                    "Must break on at least first-chance or second-chance exceptions");
            }

            // Set the exception breakpoint
            var exceptionBreakpoint = await _breakpointManager.SetExceptionBreakpointAsync(
                exception_type,
                break_on_first_chance,
                break_on_second_chance,
                include_subtypes,
                cancellationToken);

            stopwatch.Stop();
            _logger.ToolCompleted("breakpoint_set_exception", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Set exception breakpoint {Id} for {Type} (first: {First}, second: {Second}, subtypes: {Subtypes})",
                exceptionBreakpoint.Id, exception_type, break_on_first_chance, break_on_second_chance, include_subtypes);

            // Return success response
            return JsonSerializer.Serialize(new
            {
                success = true,
                breakpoint = SerializeExceptionBreakpoint(exceptionBreakpoint)
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("breakpoint_set_exception", ErrorCodes.Timeout);
            return CreateErrorResponse(ErrorCodes.Timeout, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.ToolError("breakpoint_set_exception", ErrorCodes.InvalidCondition);
            return CreateErrorResponse(
                ErrorCodes.InvalidCondition,
                $"Failed to set exception breakpoint: {ex.Message}",
                new { exception_type, exceptionType = ex.GetType().Name });
        }
    }

    private static bool IsValidExceptionTypeName(string typeName)
    {
        // Basic validation: must contain at least one dot (namespace.class) or be a simple name
        // Must not start with a number, must contain only valid identifier characters
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        // Split by dots and validate each part
        var parts = typeName.Split('.');
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                return false;
            }

            // First character must be letter or underscore
            if (!char.IsLetter(part[0]) && part[0] != '_')
            {
                return false;
            }

            // Rest must be letters, digits, or underscores
            foreach (var c in part.AsSpan(1))
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static object SerializeExceptionBreakpoint(Models.Breakpoints.ExceptionBreakpoint ebp)
    {
        return new
        {
            id = ebp.Id,
            exceptionType = ebp.ExceptionType,
            breakOnFirstChance = ebp.BreakOnFirstChance,
            breakOnSecondChance = ebp.BreakOnSecondChance,
            includeSubtypes = ebp.IncludeSubtypes,
            enabled = ebp.Enabled,
            verified = ebp.Verified,
            hitCount = ebp.HitCount
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
