using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for retrieving stack traces from a paused debug session.
/// </summary>
[McpServerToolType]
public sealed class StacktraceGetTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<StacktraceGetTool> _logger;

    public StacktraceGetTool(IDebugSessionManager sessionManager, ILogger<StacktraceGetTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Get stack trace for a thread.
    /// </summary>
    /// <param name="thread_id">Thread ID (default: current thread).</param>
    /// <param name="start_frame">Start from frame N (for pagination, default: 0).</param>
    /// <param name="max_frames">Maximum frames to return (default: 20, min: 1, max: 1000).</param>
    /// <returns>Stack frames with source locations and arguments.</returns>
    [McpServerTool(Name = "stacktrace_get")]
    [Description("Get stack trace for a thread")]
    public string GetStackTrace(
        [Description("Thread ID (default: current thread)")] int? thread_id = null,
        [Description("Start from frame N (for pagination)")] int start_frame = 0,
        [Description("Maximum frames to return")] int max_frames = 20)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("stacktrace_get",
            $"{{\"thread_id\": {(thread_id?.ToString() ?? "null")}, \"start_frame\": {start_frame}, \"max_frames\": {max_frames}}}");

        try
        {
            // Validate parameters
            if (start_frame < 0)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "start_frame must be >= 0",
                    new { parameter = "start_frame", value = start_frame });
            }

            if (max_frames < 1 || max_frames > 1000)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "max_frames must be between 1 and 1000",
                    new { parameter = "max_frames", value = max_frames });
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("stacktrace_get", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("stacktrace_get", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot get stack trace: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            // Get stack frames
            var (frames, totalFrames) = _sessionManager.GetStackFrames(thread_id, start_frame, max_frames);

            // Use session's active thread ID if no thread specified
            var actualThreadId = thread_id ?? session.ActiveThreadId ?? 0;

            stopwatch.Stop();
            _logger.ToolCompleted("stacktrace_get", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Retrieved {FrameCount} stack frames (total: {TotalFrames}) for thread {ThreadId}",
                frames.Count, totalFrames, actualThreadId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                thread_id = actualThreadId,
                total_frames = totalFrames,
                frames = frames.Select(f => BuildFrameResponse(f))
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("stacktrace_get", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not paused"))
        {
            _logger.ToolError("stacktrace_get", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("thread"))
        {
            _logger.ToolError("stacktrace_get", ErrorCodes.InvalidThread);
            return CreateErrorResponse(ErrorCodes.InvalidThread, ex.Message,
                new { thread_id });
        }
        catch (Exception ex)
        {
            _logger.ToolError("stacktrace_get", ErrorCodes.StackTraceFailed);
            return CreateErrorResponse(ErrorCodes.StackTraceFailed,
                $"Failed to retrieve stack trace: {ex.Message}");
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

    private static object BuildFrameResponse(Models.Inspection.StackFrame frame)
    {
        var response = new Dictionary<string, object?>
        {
            ["index"] = frame.Index,
            ["function"] = frame.Function,
            ["module"] = frame.Module,
            ["is_external"] = frame.IsExternal
        };

        if (frame.Location != null)
        {
            response["location"] = new
            {
                file = frame.Location.File,
                line = frame.Location.Line,
                column = frame.Location.Column,
                function = frame.Location.FunctionName
            };
        }

        if (frame.Arguments?.Count > 0)
        {
            response["arguments"] = frame.Arguments.Select(arg => new
            {
                name = arg.Name,
                type = arg.Type,
                value = arg.Value,
                scope = arg.Scope.ToString().ToLowerInvariant(),
                has_children = arg.HasChildren,
                children_count = arg.ChildrenCount
            });
        }

        return response;
    }
}
