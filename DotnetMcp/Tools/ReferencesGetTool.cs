using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for analyzing object references.
/// </summary>
[McpServerToolType]
public sealed class ReferencesGetTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<ReferencesGetTool> _logger;

    public ReferencesGetTool(IDebugSessionManager sessionManager, ILogger<ReferencesGetTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Analyze object references - find what objects a target references (outbound).
    /// </summary>
    /// <param name="object_ref">Object reference (variable name or expression).</param>
    /// <param name="direction">Reference direction: 'outbound' (default), 'inbound', 'both'. Note: inbound not yet implemented.</param>
    /// <param name="max_results">Maximum references to return (default: 50, max: 100).</param>
    /// <param name="include_arrays">Include array element references (default: true).</param>
    /// <param name="thread_id">Thread ID (default: current thread).</param>
    /// <param name="frame_index">Frame index (0 = top of stack, default: 0).</param>
    /// <returns>Reference analysis with outbound object references.</returns>
    [McpServerTool(Name = "references_get")]
    [Description("Analyze object references - find what objects a target references")]
    public async Task<string> GetReferences(
        [Description("Object reference (variable name or expression)")] string object_ref,
        [Description("Reference direction: outbound, inbound, both")] string direction = "outbound",
        [Description("Maximum references to return (max: 100)")] int max_results = 50,
        [Description("Include array element references")] bool include_arrays = true,
        [Description("Thread ID (default: current thread)")] int? thread_id = null,
        [Description("Frame index (0 = top of stack)")] int frame_index = 0)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("references_get",
            $"{{\"object_ref\": \"{object_ref}\", \"direction\": \"{direction}\", \"max_results\": {max_results}, \"include_arrays\": {include_arrays.ToString().ToLowerInvariant()}}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(object_ref))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "object_ref cannot be empty",
                    new { parameter = "object_ref" });
            }

            string[] validDirections = ["outbound", "inbound", "both"];
            if (!validDirections.Contains(direction))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    $"direction must be one of: {string.Join(", ", validDirections)}",
                    new { parameter = "direction", value = direction, validValues = validDirections });
            }

            if (max_results < 1)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "max_results must be >= 1",
                    new { parameter = "max_results", value = max_results });
            }

            if (max_results > 100)
            {
                max_results = 100; // Clamp to max
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
                _logger.ToolError("references_get", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("references_get", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot get references: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            // Get references (currently only outbound is supported)
            var references = await _sessionManager.GetOutboundReferencesAsync(
                object_ref, include_arrays, max_results, thread_id, frame_index);

            stopwatch.Stop();
            _logger.ToolCompleted("references_get", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Found {Count} outbound references for '{ObjectRef}'",
                references.OutboundCount, object_ref);

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["references"] = new Dictionary<string, object?>
                {
                    ["targetAddress"] = references.TargetAddress,
                    ["targetType"] = references.TargetType,
                    ["outbound"] = references.Outbound.Select(r => new
                    {
                        sourceAddress = r.SourceAddress,
                        sourceType = r.SourceType,
                        targetAddress = r.TargetAddress,
                        targetType = r.TargetType,
                        path = r.Path,
                        referenceType = r.ReferenceType.ToString()
                    }),
                    ["outboundCount"] = references.OutboundCount,
                    ["truncated"] = references.Truncated
                }
            };

            // Add inbound placeholders when direction includes inbound
            if (direction == "inbound" || direction == "both")
            {
                ((Dictionary<string, object?>)response["references"]!)["inbound"] = Array.Empty<object>();
                ((Dictionary<string, object?>)response["references"]!)["inboundCount"] = 0;
                ((Dictionary<string, object?>)response["references"]!)["inboundNote"] = "Inbound reference analysis is not yet implemented";
            }

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("references_get", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not paused"))
        {
            _logger.ToolError("references_get", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid reference"))
        {
            _logger.ToolError("references_get", ErrorCodes.InvalidReference);
            return CreateErrorResponse(ErrorCodes.InvalidReference, ex.Message,
                new { object_ref });
        }
        catch (Exception ex)
        {
            _logger.ToolError("references_get", "REFERENCE_ANALYSIS_FAILED");
            _logger.LogError(ex, "Reference analysis failed for '{ObjectRef}'", object_ref);
            return CreateErrorResponse("REFERENCE_ANALYSIS_FAILED",
                $"Failed to analyze references: {ex.Message}");
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
