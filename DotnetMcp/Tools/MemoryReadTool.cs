using System.ComponentModel;
using System.Text.Json;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotnetMcp.Tools;

/// <summary>
/// MCP tool for reading raw memory bytes.
/// </summary>
[McpServerToolType]
public sealed class MemoryReadTool
{
    private readonly IDebugSessionManager _sessionManager;
    private readonly ILogger<MemoryReadTool> _logger;

    public MemoryReadTool(IDebugSessionManager sessionManager, ILogger<MemoryReadTool> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Read raw memory bytes from the debuggee process.
    /// </summary>
    /// <param name="address">Memory address in hex (e.g., '0x00007FF8A1234560') or decimal.</param>
    /// <param name="size">Number of bytes to read (default: 256, max: 65536).</param>
    /// <param name="format">Output format: 'hex', 'hex_ascii' (default), 'raw'.</param>
    /// <returns>Memory dump with hex bytes and optional ASCII representation.</returns>
    [McpServerTool(Name = "memory_read")]
    [Description("Read raw memory bytes from the debuggee process")]
    public async Task<string> ReadMemory(
        [Description("Memory address in hex (0x...) or decimal")] string address,
        [Description("Number of bytes to read (max: 65536)")] int size = 256,
        [Description("Output format: hex, hex_ascii, raw")] string format = "hex_ascii")
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.ToolInvoked("memory_read",
            $"{{\"address\": \"{address}\", \"size\": {size}, \"format\": \"{format}\"}}");

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(address))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "address cannot be empty",
                    new { parameter = "address" });
            }

            if (size <= 0)
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    "size must be positive",
                    new { parameter = "size", value = size });
            }

            if (size > 65536)
            {
                return CreateErrorResponse(ErrorCodes.SizeExceeded,
                    $"Requested size {size} exceeds maximum limit of 65536 bytes",
                    new { requestedSize = size, maxSize = 65536 });
            }

            string[] validFormats = ["hex", "hex_ascii", "raw"];
            if (!validFormats.Contains(format))
            {
                return CreateErrorResponse(ErrorCodes.InvalidParameter,
                    $"format must be one of: {string.Join(", ", validFormats)}",
                    new { parameter = "format", value = format, validValues = validFormats });
            }

            // Check for active session
            var session = _sessionManager.CurrentSession;
            if (session == null)
            {
                _logger.ToolError("memory_read", ErrorCodes.NoSession);
                return CreateErrorResponse(ErrorCodes.NoSession, "No active debug session");
            }

            // Check if paused
            if (session.State != SessionState.Paused)
            {
                _logger.ToolError("memory_read", ErrorCodes.NotPaused);
                return CreateErrorResponse(ErrorCodes.NotPaused,
                    $"Cannot read memory: process is not paused (current state: {session.State.ToString().ToLowerInvariant()})",
                    new { currentState = session.State.ToString().ToLowerInvariant() });
            }

            // Read memory
            var memory = await _sessionManager.ReadMemoryAsync(address, size);

            stopwatch.Stop();
            _logger.ToolCompleted("memory_read", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Read {ActualSize}/{RequestedSize} bytes from address {Address}",
                memory.ActualSize, memory.RequestedSize, memory.Address);

            var response = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["memory"] = new Dictionary<string, object?>
                {
                    ["address"] = memory.Address,
                    ["requestedSize"] = memory.RequestedSize,
                    ["actualSize"] = memory.ActualSize,
                    ["bytes"] = memory.Bytes
                }
            };

            if (format == "hex_ascii" && memory.Ascii != null)
            {
                ((Dictionary<string, object?>)response["memory"]!)["ascii"] = memory.Ascii;
            }

            if (memory.Error != null)
            {
                ((Dictionary<string, object?>)response["memory"]!)["error"] = memory.Error;
            }

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active debug session"))
        {
            _logger.ToolError("memory_read", ErrorCodes.NoSession);
            return CreateErrorResponse(ErrorCodes.NoSession, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not paused"))
        {
            _logger.ToolError("memory_read", ErrorCodes.NotPaused);
            return CreateErrorResponse(ErrorCodes.NotPaused, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to read memory"))
        {
            _logger.ToolError("memory_read", ErrorCodes.InvalidAddress);
            return CreateErrorResponse(ErrorCodes.InvalidAddress, ex.Message,
                new { address });
        }
        catch (ArgumentException ex)
        {
            _logger.ToolError("memory_read", ErrorCodes.InvalidParameter);
            return CreateErrorResponse(ErrorCodes.InvalidParameter, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.ToolError("memory_read", ErrorCodes.MemoryReadFailed);
            _logger.LogError(ex, "Memory read failed for address '{Address}'", address);
            return CreateErrorResponse(ErrorCodes.MemoryReadFailed,
                $"Failed to read memory: {ex.Message}");
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
