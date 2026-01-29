using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for getting type memory layout.
/// </summary>
[McpServerToolType]
public sealed class LayoutGetTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<LayoutGetTool> _logger;

    public LayoutGetTool(IDebugSessionManager sessionManager, ILogger<LayoutGetTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Get the memory layout of a type including field offsets, sizes, and padding.
    /// </summary>
    /// <param name="type_name">Full type name (e.g., 'MyApp.Models.Customer') or object reference.</param>
    /// <param name="include_inherited">Include inherited fields from base classes (default: true).</param>
    /// <param name="include_padding">Include padding analysis between fields (default: true).</param>
    /// <param name="thread_id">Thread ID (default: current thread).</param>
    /// <param name="frame_index">Frame index (0 = top of stack, default: 0).</param>
    /// <returns>Type memory layout with fields, offsets, sizes, and padding.</returns>
    [McpServerTool(Name = "layout_get")]
    [Description("Get the memory layout of a type including field offsets, sizes, and padding")]
    public async Task<string> GetLayout(
        [Description("Full type name or object reference")] string type_name,
        [Description("Include inherited fields from base classes")] bool include_inherited = true,
        [Description("Include padding analysis between fields")] bool include_padding = true,
        [Description("Thread ID (default: current thread)")] int? thread_id = null,
        [Description("Frame index (0 = top of stack)")] int frame_index = 0)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("layout_get",
            $"{{\"type_name\": \"{type_name}\", \"include_inherited\": {include_inherited.ToString().ToLowerInvariant()}, \"include_padding\": {include_padding.ToString().ToLowerInvariant()}}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(type_name))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "type_name cannot be empty",
                    new { parameter = "type_name" });
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
                _logger.ToolError("layout_get", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("layout_get", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot get layout: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            // Get layout
            var layout = await _sessionManager.GetTypeLayoutAsync(
                type_name, include_inherited, include_padding, thread_id, frame_index);

            stopwatch.Stop();
            _logger.ToolCompleted("layout_get", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Got layout for type '{TypeName}': {TotalSize} bytes, {FieldCount} fields",
                layout.TypeName, layout.TotalSize, layout.Fields.Count);

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["layout"] = new Dictionary<string, object?>
                {
                    ["typeName"] = layout.TypeName,
                    ["totalSize"] = layout.TotalSize,
                    ["headerSize"] = layout.HeaderSize,
                    ["dataSize"] = layout.DataSize,
                    ["fields"] = layout.Fields.Select(f => new Dictionary<string, object?>
                    {
                        ["name"] = f.Name,
                        ["typeName"] = f.TypeName,
                        ["offset"] = f.Offset,
                        ["size"] = f.Size,
                        ["alignment"] = f.Alignment,
                        ["isReference"] = f.IsReference,
                        ["declaringType"] = f.DeclaringType
                    }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value)),
                    ["isValueType"] = layout.IsValueType
                }
            };

            // Add padding if requested and available
            if (include_padding && layout.Padding.Count > 0)
            {
                ((Dictionary<string, object?>)response["layout"]!)["padding"] = layout.Padding.Select(p => new
                {
                    offset = p.Offset,
                    size = p.Size,
                    reason = p.Reason
                });
            }

            // Add base type if available
            if (layout.BaseType != null)
            {
                ((Dictionary<string, object?>)response["layout"]!)["baseType"] = layout.BaseType;
            }

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("layout_get", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not paused"))
        {
            _logger.ToolError("layout_get", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.ToolError("layout_get", ErrorCodes.TypeNotFound);
            return CreateErrorResponse(ErrorCodes.TypeNotFound, ex.Message,
                new { type_name });
        }
        catch (Exception ex)
        {
            _logger.ToolError("layout_get", "LAYOUT_FAILED");
            _logger.LogError(ex, "Layout retrieval failed for '{TypeName}'", type_name);
            return CreateErrorResponse("LAYOUT_FAILED",
                $"Failed to get type layout: {ex.Message}");
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
