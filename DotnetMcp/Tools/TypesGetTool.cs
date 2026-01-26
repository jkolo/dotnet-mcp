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
/// MCP tool for browsing types in a loaded module.
/// </summary>
[McpServerToolType]
public sealed class TypesGetTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly IProcessDebugger _processDebugger;
    private readonly ILogger<TypesGetTool> _logger;

    public TypesGetTool(
        IDebugSessionManager sessionManager,
        IProcessDebugger processDebugger,
        ILogger<TypesGetTool> logger)
    {
        _sessionManager = sessionManager;
        _processDebugger = processDebugger;
        _logger = logger;
    }

    /// <summary>
    /// Get types defined in a module, organized by namespace.
    /// </summary>
    /// <param name="module_name">Name of the module to inspect (e.g., 'MyApp' or 'System.Private.CoreLib').</param>
    /// <param name="namespace_filter">Filter types by namespace pattern (supports * wildcard).</param>
    /// <param name="kind">Filter by type kind: class, interface, struct, enum, delegate.</param>
    /// <param name="visibility">Filter by visibility: public, internal, private, protected.</param>
    /// <param name="max_results">Maximum types to return (default: 100, max: 1000).</param>
    /// <param name="continuation_token">Token from previous response for pagination.</param>
    /// <returns>Types with namespace hierarchy and pagination info.</returns>
    [McpServerTool(Name = "types_get")]
    [Description("Get types defined in a module, organized by namespace")]
    public async Task<string> GetTypes(
        [Description("Name of the module to inspect")] string module_name,
        [Description("Filter types by namespace pattern (supports * wildcard)")] string? namespace_filter = null,
        [Description("Filter by type kind: class, interface, struct, enum, delegate")] string? kind = null,
        [Description("Filter by visibility: public, internal, private, protected")] string? visibility = null,
        [Description("Maximum types to return (max: 1000)")] int max_results = 100,
        [Description("Token from previous response for pagination")] string? continuation_token = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("types_get",
            $"{{\"module_name\": \"{module_name}\", \"namespace_filter\": {(namespace_filter == null ? "null" : $"\"{namespace_filter}\"")}, \"kind\": {(kind == null ? "null" : $"\"{kind}\"")}, \"max_results\": {max_results}}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(module_name))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "module_name cannot be empty",
                    new { parameter = "module_name" });
            }

            // Parse kind filter
            TypeKind? kindFilter = null;
            if (!string.IsNullOrEmpty(kind))
            {
                if (!Enum.TryParse<TypeKind>(kind, ignoreCase: true, out var parsedKind))
                {
                    return CreateErrorResponse(ErrorCodes.InvalidParameter,
                        $"Invalid kind value: {kind}. Valid values: class, interface, struct, enum, delegate",
                        new { parameter = "kind", value = kind, validValues = new[] { "class", "interface", "struct", "enum", "delegate" } });
                }
                kindFilter = parsedKind;
            }

            // Parse visibility filter
            Visibility? visibilityFilter = null;
            if (!string.IsNullOrEmpty(visibility))
            {
                if (!Enum.TryParse<Visibility>(visibility, ignoreCase: true, out var parsedVisibility))
                {
                    return CreateErrorResponse(ErrorCodes.InvalidParameter,
                        $"Invalid visibility value: {visibility}. Valid values: public, internal, private, protected",
                        new { parameter = "visibility", value = visibility, validValues = new[] { "public", "internal", "private", "protected" } });
                }
                visibilityFilter = parsedVisibility;
            }

            // Validate max_results
            if (max_results <= 0 || max_results > 1000)
            {
                max_results = Math.Clamp(max_results, 1, 1000);
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("types_get", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Type browsing works with running or paused process (metadata only)
            _logger.LogDebug("Getting types from module {ModuleName}", module_name);

            var result = await _processDebugger.GetTypesAsync(
                module_name,
                namespace_filter,
                kindFilter,
                visibilityFilter,
                max_results,
                continuation_token);

            stopwatch.Stop();
            _logger.ToolCompleted("types_get", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Retrieved {Count}/{Total} types from module {ModuleName}",
                result.ReturnedCount, result.TotalCount, module_name);

            // Build response
            var typeList = result.Types.Select(t => new Dictionary<string, object?>
            {
                ["fullName"] = t.FullName,
                ["name"] = t.Name,
                ["namespace"] = t.Namespace,
                ["kind"] = t.Kind.ToString().ToLowerInvariant(),
                ["visibility"] = t.Visibility.ToString().ToLowerInvariant(),
                ["isGeneric"] = t.IsGeneric,
                ["genericParameters"] = t.GenericParameters,
                ["isNested"] = t.IsNested,
                ["declaringType"] = t.DeclaringType,
                ["moduleName"] = t.ModuleName,
                ["baseType"] = t.BaseType,
                ["interfaces"] = t.Interfaces
            }).ToList();

            var namespaceList = result.Namespaces.Select(n => new Dictionary<string, object?>
            {
                ["name"] = n.Name,
                ["fullName"] = n.FullName,
                ["typeCount"] = n.TypeCount,
                ["childNamespaces"] = n.ChildNamespaces,
                ["depth"] = n.Depth
            }).ToList();

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["moduleName"] = result.ModuleName,
                ["namespaceFilter"] = result.NamespaceFilter,
                ["types"] = typeList,
                ["namespaces"] = namespaceList,
                ["totalCount"] = result.TotalCount,
                ["returnedCount"] = result.ReturnedCount,
                ["truncated"] = result.Truncated,
                ["continuationToken"] = result.ContinuationToken
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not attached"))
        {
            _logger.ToolError("types_get", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.ToolError("types_get", ErrorCodes.ModuleNotFound);
            return CreateErrorResponse(ErrorCodes.ModuleNotFound, ex.Message,
                new { moduleName = module_name });
        }
        catch (Exception ex)
        {
            _logger.ToolError("types_get", ErrorCodes.MetadataError);
            _logger.LogError(ex, "Type browsing failed for module '{ModuleName}'", module_name);
            return CreateErrorResponse(ErrorCodes.MetadataError,
                $"Failed to browse types: {ex.Message}");
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
