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
/// MCP tool for listing managed threads in the debuggee process.
/// </summary>
[McpServerToolType]
public sealed class ThreadsListTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<ThreadsListTool> _logger;

    public ThreadsListTool(IDebugSessionManager sessionManager, ILogger<ThreadsListTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// List all managed threads in the debuggee process.
    /// </summary>
    /// <returns>List of threads with their states and locations.</returns>
    [McpServerTool(Name = "threads_list")]
    [Description("List all managed threads in the debuggee process")]
    public string ListThreads()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("threads_list", "{}");

        try
        {
            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("threads_list", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Get threads
            var threads = _sessionManager.GetThreads();

            stopwatch.Stop();
            _logger.ToolCompleted("threads_list", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Retrieved {ThreadCount} managed threads", threads.Count);

            return JsonSerializer.Serialize(new
            {
                success = true,
                threads = threads.Select(t => BuildThreadResponse(t))
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("threads_list", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.ToolError("threads_list", "THREADS_FAILED");
            return CreateErrorResponse("THREADS_FAILED",
                $"Failed to retrieve threads: {ex.Message}");
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

    private static object BuildThreadResponse(ThreadInfo thread)
    {
        var response = new Dictionary<string, object?>
        {
            ["id"] = thread.Id,
            ["name"] = thread.Name,
            ["state"] = thread.State.ToString().ToLowerInvariant(),
            ["is_current"] = thread.IsCurrent
        };

        if (thread.Location != null)
        {
            response["location"] = new
            {
                file = thread.Location.File,
                line = thread.Location.Line,
                column = thread.Location.Column,
                function = thread.Location.FunctionName
            };
        }

        return response;
    }
}
