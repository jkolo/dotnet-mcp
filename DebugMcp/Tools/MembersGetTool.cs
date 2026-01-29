using System.ComponentModel;
using System.Text.Json;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Models.Modules;
using DebugMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DebugMcp.Tools;

/// <summary>
/// MCP tool for inspecting type members (methods, properties, fields, events).
/// </summary>
[McpServerToolType]
public sealed class MembersGetTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly IProcessDebugger _processDebugger;
    private readonly ILogger<MembersGetTool> _logger;

    public MembersGetTool(
        IDebugSessionManager sessionManager,
        IProcessDebugger processDebugger,
        ILogger<MembersGetTool> logger)
    {
        _sessionManager = sessionManager;
        _processDebugger = processDebugger;
        _logger = logger;
    }

    /// <summary>
    /// Get members (methods, properties, fields, events) of a type.
    /// </summary>
    /// <param name="type_name">Full type name to inspect (e.g., 'System.String' or 'MyApp.Models.Customer').</param>
    /// <param name="module_name">Module containing the type (optional, searches all if omitted).</param>
    /// <param name="include_inherited">Include inherited members from base types.</param>
    /// <param name="member_kinds">Comma-separated list of member kinds to include: methods, properties, fields, events.</param>
    /// <param name="visibility">Filter by visibility: public, internal, private, protected.</param>
    /// <param name="include_static">Include static members.</param>
    /// <param name="include_instance">Include instance members.</param>
    /// <returns>Type members with methods, properties, fields, and events.</returns>
    [McpServerTool(Name = "members_get")]
    [Description("Get members (methods, properties, fields, events) of a type")]
    public async Task<string> GetMembers(
        [Description("Full type name to inspect")] string type_name,
        [Description("Module containing the type (optional)")] string? module_name = null,
        [Description("Include inherited members from base types")] bool include_inherited = false,
        [Description("Comma-separated list of member kinds: methods, properties, fields, events")] string? member_kinds = null,
        [Description("Filter by visibility: public, internal, private, protected")] string? visibility = null,
        [Description("Include static members")] bool include_static = true,
        [Description("Include instance members")] bool include_instance = true)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("members_get",
            $"{{\"type_name\": \"{type_name}\", \"module_name\": {(module_name == null ? "null" : $"\"{module_name}\"")}, \"include_inherited\": {include_inherited.ToString().ToLowerInvariant()}}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(type_name))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "type_name cannot be empty",
                    new { parameter = "type_name" });
            }

            // Parse member kinds
            string[]? memberKindsArray = null;
            if (!string.IsNullOrEmpty(member_kinds))
            {
                memberKindsArray = member_kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var validKinds = new[] { "methods", "properties", "fields", "events" };
                foreach (var kind in memberKindsArray)
                {
                    if (!validKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
                    {
                        return CreateErrorResponse(ErrorCodes.InvalidParameter,
                            $"Invalid member_kinds value: {kind}. Valid values: methods, properties, fields, events",
                            new { parameter = "member_kinds", value = kind, validValues = validKinds });
                    }
                }
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

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("members_get", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Member inspection works with running or paused process (metadata only)
            _logger.LogDebug("Getting members for type {TypeName}", type_name);

            var result = await _processDebugger.GetMembersAsync(
                type_name,
                module_name,
                include_inherited,
                memberKindsArray,
                visibilityFilter,
                include_static,
                include_instance);

            stopwatch.Stop();
            _logger.ToolCompleted("members_get", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Retrieved {MethodCount} methods, {PropertyCount} properties, {FieldCount} fields, {EventCount} events for type {TypeName}",
                result.MethodCount, result.PropertyCount, result.FieldCount, result.EventCount, type_name);

            // Build response
            var methodList = result.Methods.Select(m => new Dictionary<string, object?>
            {
                ["name"] = m.Name,
                ["signature"] = m.Signature,
                ["returnType"] = m.ReturnType,
                ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["type"] = p.Type,
                    ["isOptional"] = p.IsOptional,
                    ["isOut"] = p.IsOut,
                    ["isRef"] = p.IsRef,
                    ["defaultValue"] = p.DefaultValue
                }).ToList(),
                ["visibility"] = m.Visibility.ToString().ToLowerInvariant(),
                ["isStatic"] = m.IsStatic,
                ["isVirtual"] = m.IsVirtual,
                ["isAbstract"] = m.IsAbstract,
                ["isGeneric"] = m.IsGeneric,
                ["genericParameters"] = m.GenericParameters,
                ["declaringType"] = m.DeclaringType
            }).ToList();

            var propertyList = result.Properties.Select(p => new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["type"] = p.Type,
                ["visibility"] = p.Visibility.ToString().ToLowerInvariant(),
                ["isStatic"] = p.IsStatic,
                ["hasGetter"] = p.HasGetter,
                ["hasSetter"] = p.HasSetter,
                ["getterVisibility"] = p.GetterVisibility?.ToString().ToLowerInvariant(),
                ["setterVisibility"] = p.SetterVisibility?.ToString().ToLowerInvariant(),
                ["isIndexer"] = p.IsIndexer,
                ["indexerParameters"] = p.IndexerParameters?.Select(ip => new Dictionary<string, object?>
                {
                    ["name"] = ip.Name,
                    ["type"] = ip.Type
                }).ToList()
            }).ToList();

            var fieldList = result.Fields.Select(f => new Dictionary<string, object?>
            {
                ["name"] = f.Name,
                ["type"] = f.Type,
                ["visibility"] = f.Visibility.ToString().ToLowerInvariant(),
                ["isStatic"] = f.IsStatic,
                ["isReadOnly"] = f.IsReadOnly,
                ["isConst"] = f.IsConst,
                ["constValue"] = f.ConstValue
            }).ToList();

            var eventList = result.Events.Select(e => new Dictionary<string, object?>
            {
                ["name"] = e.Name,
                ["type"] = e.Type,
                ["visibility"] = e.Visibility.ToString().ToLowerInvariant(),
                ["isStatic"] = e.IsStatic,
                ["addMethod"] = e.AddMethod,
                ["removeMethod"] = e.RemoveMethod
            }).ToList();

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["typeName"] = result.TypeName,
                ["methods"] = methodList,
                ["properties"] = propertyList,
                ["fields"] = fieldList,
                ["events"] = eventList,
                ["includesInherited"] = result.IncludesInherited,
                ["methodCount"] = result.MethodCount,
                ["propertyCount"] = result.PropertyCount,
                ["fieldCount"] = result.FieldCount,
                ["eventCount"] = result.EventCount
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not attached"))
        {
            _logger.ToolError("members_get", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.ToolError("members_get", ErrorCodes.TypeNotFound);
            return CreateErrorResponse(ErrorCodes.TypeNotFound, ex.Message,
                new { typeName = type_name, moduleName = module_name });
        }
        catch (Exception ex)
        {
            _logger.ToolError("members_get", ErrorCodes.MetadataError);
            _logger.LogError(ex, "Member inspection failed for type '{TypeName}'", type_name);
            return CreateErrorResponse(ErrorCodes.MetadataError,
                $"Failed to inspect members: {ex.Message}");
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
