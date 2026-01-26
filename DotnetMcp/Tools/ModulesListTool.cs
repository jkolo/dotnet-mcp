using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for listing loaded modules/assemblies in the debuggee process.
/// </summary>
[McpServerToolType]
public sealed class ModulesListTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly IProcessDebugger _processDebugger;
    private readonly ILogger<ModulesListTool> _logger;

    public ModulesListTool(
        IDebugSessionManager sessionManager,
        IProcessDebugger processDebugger,
        ILogger<ModulesListTool> logger)
    {
        _sessionManager = sessionManager;
        _processDebugger = processDebugger;
        _logger = logger;
    }

    /// <summary>
    /// List all loaded modules/assemblies in the debuggee process.
    /// </summary>
    /// <param name="include_system">Include system assemblies (System.*, Microsoft.*). Default: true.</param>
    /// <param name="name_filter">Filter modules by name pattern (supports * wildcard).</param>
    /// <returns>List of loaded modules with metadata.</returns>
    [McpServerTool(Name = "modules_list")]
    [Description("List all loaded modules/assemblies in the debuggee process")]
    public async Task<string> ListModules(
        [Description("Include system assemblies (System.*, Microsoft.*)")] bool include_system = true,
        [Description("Filter modules by name pattern (supports * wildcard)")] string? name_filter = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("modules_list",
            $"{{\"include_system\": {include_system.ToString().ToLowerInvariant()}, \"name_filter\": {(name_filter == null ? "null" : $"\"{name_filter}\"")}}}");

        try
        {
            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("modules_list", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Module listing works with running or paused process (metadata only)
            _logger.LogDebug("Getting modules (includeSystem={IncludeSystem}, nameFilter={NameFilter})",
                include_system, name_filter);

            var modules = await _processDebugger.GetModulesAsync(include_system, name_filter);

            stopwatch.Stop();
            _logger.ToolCompleted("modules_list", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Retrieved {Count} modules", modules.Count);

            // Build response
            var moduleList = modules.Select(m => new Dictionary<string, object?>
            {
                ["name"] = m.Name,
                ["fullName"] = m.FullName,
                ["path"] = m.Path,
                ["version"] = m.Version,
                ["isManaged"] = m.IsManaged,
                ["isDynamic"] = m.IsDynamic,
                ["hasSymbols"] = m.HasSymbols,
                ["moduleId"] = m.ModuleId,
                ["baseAddress"] = m.BaseAddress,
                ["size"] = m.Size
            }).ToList();

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["modules"] = moduleList,
                ["count"] = modules.Count
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not attached"))
        {
            _logger.ToolError("modules_list", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to enumerate"))
        {
            _logger.ToolError("modules_list", ErrorCodes.EnumerationFailed);
            return CreateErrorResponse(ErrorCodes.EnumerationFailed, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.ToolError("modules_list", ErrorCodes.EnumerationFailed);
            _logger.LogError(ex, "Module listing failed");
            return CreateErrorResponse(ErrorCodes.EnumerationFailed,
                $"Failed to list modules: {ex.Message}");
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
