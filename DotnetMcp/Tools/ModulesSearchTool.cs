using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Models.Modules;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for searching types and methods across all loaded modules.
/// </summary>
[McpServerToolType]
public sealed class ModulesSearchTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly IProcessDebugger _processDebugger;
    private readonly ILogger<ModulesSearchTool> _logger;

    public ModulesSearchTool(
        IDebugSessionManager sessionManager,
        IProcessDebugger processDebugger,
        ILogger<ModulesSearchTool> logger)
    {
        _sessionManager = sessionManager;
        _processDebugger = processDebugger;
        _logger = logger;
    }

    /// <summary>
    /// Search for types and methods across all loaded modules.
    /// </summary>
    /// <param name="pattern">Search pattern (supports * wildcard). Examples: *Customer*, Get*, *Service.</param>
    /// <param name="search_type">What to search: types, methods, or both.</param>
    /// <param name="module_filter">Limit search to specific module (supports * wildcard).</param>
    /// <param name="case_sensitive">Enable case-sensitive matching.</param>
    /// <param name="max_results">Maximum results to return (max: 100).</param>
    /// <returns>Search results with matching types and/or methods.</returns>
    [McpServerTool(Name = "modules_search")]
    [Description("Search for types and methods across all loaded modules")]
    public async Task<string> SearchModules(
        [Description("Search pattern (supports * wildcard)")] string pattern,
        [Description("What to search: types, methods, or both")] string search_type = "both",
        [Description("Limit search to specific module (supports * wildcard)")] string? module_filter = null,
        [Description("Enable case-sensitive matching")] bool case_sensitive = false,
        [Description("Maximum results to return (max: 100)")] int max_results = 50)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("modules_search",
            $"{{\"pattern\": \"{pattern}\", \"search_type\": \"{search_type}\", \"module_filter\": {(module_filter == null ? "null" : $"\"{module_filter}\"")}, \"max_results\": {max_results}}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return CreateErrorResponse(ErrorCodes.InvalidPattern,
                    "pattern cannot be empty",
                    new { parameter = "pattern" });
            }

            // Parse search type
            SearchType searchType;
            switch (search_type.ToLowerInvariant())
            {
                case "types":
                    searchType = SearchType.Types;
                    break;
                case "methods":
                    searchType = SearchType.Methods;
                    break;
                case "both":
                    searchType = SearchType.Both;
                    break;
                default:
                    return CreateErrorResponse(ErrorCodes.InvalidParameter,
                        $"Invalid search_type value: {search_type}. Valid values: types, methods, both",
                        new { parameter = "search_type", value = search_type, validValues = new[] { "types", "methods", "both" } });
            }

            // Validate max_results
            if (max_results <= 0 || max_results > 100)
            {
                max_results = Math.Clamp(max_results, 1, 100);
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("modules_search", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Module search works with running or paused process (metadata only)
            _logger.LogDebug("Searching modules for pattern '{Pattern}'", pattern);

            var result = await _processDebugger.SearchModulesAsync(
                pattern,
                searchType,
                module_filter,
                case_sensitive,
                max_results);

            stopwatch.Stop();
            _logger.ToolCompleted("modules_search", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Found {TotalMatches} matches for pattern '{Pattern}' (returned {ReturnedMatches})",
                result.TotalMatches, pattern, result.ReturnedMatches);

            // Build response
            var typeList = result.Types.Select(t => new Dictionary<string, object?>
            {
                ["fullName"] = t.FullName,
                ["name"] = t.Name,
                ["namespace"] = t.Namespace,
                ["kind"] = t.Kind.ToString().ToLowerInvariant(),
                ["visibility"] = t.Visibility.ToString().ToLowerInvariant(),
                ["moduleName"] = t.ModuleName
            }).ToList();

            var methodList = result.Methods.Select(m => new Dictionary<string, object?>
            {
                ["declaringType"] = m.DeclaringType,
                ["moduleName"] = m.ModuleName,
                ["matchReason"] = m.MatchReason,
                ["method"] = new Dictionary<string, object?>
                {
                    ["name"] = m.Method.Name,
                    ["signature"] = m.Method.Signature,
                    ["returnType"] = m.Method.ReturnType,
                    ["visibility"] = m.Method.Visibility.ToString().ToLowerInvariant(),
                    ["isStatic"] = m.Method.IsStatic
                }
            }).ToList();

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["query"] = result.Query,
                ["searchType"] = result.SearchType.ToString().ToLowerInvariant(),
                ["types"] = typeList,
                ["methods"] = methodList,
                ["totalMatches"] = result.TotalMatches,
                ["returnedMatches"] = result.ReturnedMatches,
                ["truncated"] = result.Truncated,
                ["continuationToken"] = result.ContinuationToken
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not attached"))
        {
            _logger.ToolError("modules_search", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.ToolError("modules_search", ErrorCodes.SearchFailed);
            _logger.LogError(ex, "Module search failed for pattern '{Pattern}'", pattern);
            return CreateErrorResponse(ErrorCodes.SearchFailed,
                $"Search failed: {ex.Message}");
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
