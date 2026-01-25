using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Models.Inspection;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for evaluating expressions in the debuggee context.
/// </summary>
[McpServerToolType]
public sealed class EvaluateTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<EvaluateTool> _logger;

    public EvaluateTool(IDebugSessionManager sessionManager, ILogger<EvaluateTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate a C# expression in the debuggee context.
    /// </summary>
    /// <param name="expression">C# expression to evaluate.</param>
    /// <param name="thread_id">Thread context (default: current thread).</param>
    /// <param name="frame_index">Stack frame context (0 = top).</param>
    /// <param name="timeout_ms">Evaluation timeout in milliseconds (default: 5000).</param>
    /// <returns>Evaluation result with value or error.</returns>
    [McpServerTool(Name = "evaluate")]
    [Description("Evaluate a C# expression in the debuggee context")]
    public async Task<string> EvaluateAsync(
        [Description("C# expression to evaluate")] string expression,
        [Description("Thread context (default: current thread)")] int? thread_id = null,
        [Description("Stack frame context (0 = top)")] int frame_index = 0,
        [Description("Evaluation timeout in milliseconds")] int timeout_ms = 5000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("evaluate",
            $"{{\"expression\": \"{EscapeJsonString(expression)}\", \"thread_id\": {(thread_id?.ToString() ?? "null")}, \"frame_index\": {frame_index}, \"timeout_ms\": {timeout_ms}}}");

        try
        {
            // Validate expression parameter
            if (string.IsNullOrWhiteSpace(expression))
            {
                return CreateErrorResponse("syntax_error", "Expression cannot be empty", position: 0);
            }

            // Validate timeout range (100-60000)
            if (timeout_ms < 100 || timeout_ms > 60000)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "timeout_ms must be between 100 and 60000",
                    new { parameter = "timeout_ms", value = timeout_ms });
            }

            // Validate frame_index
            if (frame_index < 0)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "frame_index must be >= 0",
                    new { parameter = "frame_index", value = frame_index });
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("evaluate", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("evaluate", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot evaluate expression: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})");
            }

            // Evaluate expression
            using var cts = new CancellationTokenSource(timeout_ms);
            var result = await _sessionManager.EvaluateAsync(expression, thread_id, frame_index, timeout_ms, cts.Token);

            stopwatch.Stop();
            _logger.ToolCompleted("evaluate", stopwatch.ElapsedMilliseconds);

            if (result.Success)
            {
                _logger.LogInformation("Evaluated expression '{Expression}' = {Value} ({Type})",
                    expression, result.Value ?? "null", result.Type ?? "void");

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    value = result.Value,
                    type = result.Type,
                    has_children = result.HasChildren
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                _logger.LogWarning("Expression evaluation failed: {Code} - {Message}",
                    result.Error?.Code, result.Error?.Message);

                return CreateEvaluationErrorResponse(result.Error!);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("evaluate", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not paused"))
        {
            _logger.ToolError("evaluate", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.ToolError("evaluate", "eval_timeout");
            return CreateErrorResponse("eval_timeout",
                $"Expression evaluation timed out after {timeout_ms}ms");
        }
        catch (Exception ex)
        {
            _logger.ToolError("evaluate", "eval_exception");
            return CreateErrorResponse("eval_exception", ex.Message,
                new { exception_type = ex.GetType().FullName });
        }
    }

    private static string CreateErrorResponse(string code, string message, object? details = null, int? position = null)
    {
        var errorObj = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message
        };

        if (details != null)
        {
            errorObj["details"] = details;
        }

        if (position.HasValue)
        {
            errorObj["position"] = position.Value;
        }

        return JsonSerializer.Serialize(new
        {
            success = false,
            error = errorObj
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CreateEvaluationErrorResponse(EvaluationError error)
    {
        var errorObj = new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["message"] = error.Message
        };

        if (!string.IsNullOrEmpty(error.ExceptionType))
        {
            errorObj["exception_type"] = error.ExceptionType;
        }

        if (error.Position.HasValue)
        {
            errorObj["position"] = error.Position.Value;
        }

        return JsonSerializer.Serialize(new
        {
            success = false,
            error = errorObj
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
