using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for inspecting heap object contents.
/// </summary>
[McpServerToolType]
public sealed class ObjectInspectTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<ObjectInspectTool> _logger;

    public ObjectInspectTool(IDebugSessionManager sessionManager, ILogger<ObjectInspectTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Inspect a heap object's contents including all fields.
    /// </summary>
    /// <param name="object_ref">Object reference (variable name or expression, e.g., 'customer', 'this._orders').</param>
    /// <param name="depth">Maximum depth for nested object expansion (default: 1, max: 10).</param>
    /// <param name="thread_id">Thread ID (default: current thread).</param>
    /// <param name="frame_index">Frame index (0 = top of stack, default: 0).</param>
    /// <returns>Object inspection with type, size, fields, and values.</returns>
    [McpServerTool(Name = "object_inspect")]
    [Description("Inspect a heap object's contents including all fields")]
    public async Task<string> InspectObject(
        [Description("Object reference (variable name or expression)")] string object_ref,
        [Description("Maximum depth for nested object expansion (1-10)")] int depth = 1,
        [Description("Thread ID (default: current thread)")] int? thread_id = null,
        [Description("Frame index (0 = top of stack)")] int frame_index = 0)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("object_inspect",
            $"{{\"object_ref\": \"{object_ref}\", \"depth\": {depth}, \"thread_id\": {(thread_id?.ToString() ?? "null")}, \"frame_index\": {frame_index}}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(object_ref))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "object_ref cannot be empty",
                    new { parameter = "object_ref" });
            }

            if (depth < 1 || depth > 10)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "depth must be between 1 and 10",
                    new { parameter = "depth", value = depth });
            }

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
                _logger.ToolError("object_inspect", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("object_inspect", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot inspect object: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            // Inspect object
            var inspection = await _sessionManager.InspectObjectAsync(object_ref, depth, thread_id, frame_index);

            stopwatch.Stop();
            _logger.ToolCompleted("object_inspect", stopwatch.ElapsedMilliseconds);

            // Handle null reference
            if (inspection.IsNull)
            {
                _logger.LogInformation("Object '{ObjectRef}' is null", object_ref);
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    inspection = new
                    {
                        isNull = true,
                        typeName = inspection.TypeName
                    }
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            _logger.LogInformation("Inspected object '{ObjectRef}': {TypeName} with {FieldCount} fields",
                object_ref, inspection.TypeName, inspection.Fields.Count);

            return JsonSerializer.Serialize(new
            {
                success = true,
                inspection = new
                {
                    address = inspection.Address,
                    typeName = inspection.TypeName,
                    size = inspection.Size,
                    fields = inspection.Fields.Select(f => new
                    {
                        name = f.Name,
                        typeName = f.TypeName,
                        value = f.Value,
                        offset = f.Offset,
                        size = f.Size,
                        hasChildren = f.HasChildren,
                        childCount = f.ChildCount
                    }),
                    isNull = inspection.IsNull,
                    hasCircularRef = inspection.HasCircularRef,
                    truncated = inspection.Truncated
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("object_inspect", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not paused"))
        {
            _logger.ToolError("object_inspect", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid reference"))
        {
            _logger.ToolError("object_inspect", ErrorCodes.InvalidReference);
            return CreateErrorResponse(ErrorCodes.InvalidReference, ex.Message,
                new { object_ref });
        }
        catch (Exception ex)
        {
            _logger.ToolError("object_inspect", "INSPECTION_FAILED");
            _logger.LogError(ex, "Object inspection failed for '{ObjectRef}'", object_ref);
            return CreateErrorResponse("INSPECTION_FAILED",
                $"Failed to inspect object: {ex.Message}");
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
}
