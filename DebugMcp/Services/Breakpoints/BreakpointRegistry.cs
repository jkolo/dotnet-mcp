using System.Collections.Concurrent;
using DebugMcp.Models.Breakpoints;
using Microsoft.Extensions.Logging;

namespace DebugMcp.Services.Breakpoints;

/// <summary>
/// Thread-safe registry for tracking all breakpoints in the current debug session.
/// Supports pending breakpoints (unbound) and bound breakpoints.
/// </summary>
public sealed class BreakpointRegistry
{
    private readonly ConcurrentDictionary<string, BreakpointEntry> _breakpoints = new();
    private readonly ConcurrentDictionary<string, ExceptionBreakpointEntry> _exceptionBreakpoints = new();
    private readonly ILogger<BreakpointRegistry> _logger;

    public BreakpointRegistry(ILogger<BreakpointRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a new breakpoint to the registry.
    /// </summary>
    /// <param name="breakpoint">The breakpoint to add.</param>
    /// <returns>True if added, false if ID already exists.</returns>
    public bool Add(Breakpoint breakpoint)
    {
        var entry = new BreakpointEntry(breakpoint);
        if (_breakpoints.TryAdd(breakpoint.Id, entry))
        {
            _logger.LogDebug("Added breakpoint {Id} at {File}:{Line}",
                breakpoint.Id, breakpoint.Location.File, breakpoint.Location.Line);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Updates an existing breakpoint.
    /// </summary>
    /// <param name="breakpoint">The updated breakpoint.</param>
    /// <returns>True if updated, false if not found.</returns>
    public bool Update(Breakpoint breakpoint)
    {
        if (_breakpoints.TryGetValue(breakpoint.Id, out var entry))
        {
            entry.Breakpoint = breakpoint;
            _logger.LogDebug("Updated breakpoint {Id}", breakpoint.Id);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a breakpoint from the registry.
    /// </summary>
    /// <param name="breakpointId">ID of the breakpoint to remove.</param>
    /// <returns>The removed breakpoint, or null if not found.</returns>
    public Breakpoint? Remove(string breakpointId)
    {
        if (_breakpoints.TryRemove(breakpointId, out var entry))
        {
            _logger.LogDebug("Removed breakpoint {Id}", breakpointId);
            return entry.Breakpoint;
        }
        return null;
    }

    /// <summary>
    /// Gets a breakpoint by ID.
    /// </summary>
    /// <param name="breakpointId">The breakpoint ID.</param>
    /// <returns>The breakpoint, or null if not found.</returns>
    public Breakpoint? Get(string breakpointId)
    {
        return _breakpoints.TryGetValue(breakpointId, out var entry) ? entry.Breakpoint : null;
    }

    /// <summary>
    /// Gets all breakpoints in the registry.
    /// </summary>
    /// <returns>Read-only list of all breakpoints.</returns>
    public IReadOnlyList<Breakpoint> GetAll()
    {
        return _breakpoints.Values.Select(e => e.Breakpoint).ToList();
    }

    /// <summary>
    /// Finds a breakpoint by source location.
    /// </summary>
    /// <param name="file">Absolute path to source file.</param>
    /// <param name="line">1-based line number.</param>
    /// <returns>The breakpoint at the location, or null if none exists.</returns>
    public Breakpoint? FindByLocation(string file, int line)
    {
        var normalizedFile = NormalizePath(file);
        return _breakpoints.Values
            .Select(e => e.Breakpoint)
            .FirstOrDefault(bp =>
                NormalizePath(bp.Location.File).Equals(normalizedFile, StringComparison.OrdinalIgnoreCase) &&
                bp.Location.Line == line);
    }

    /// <summary>
    /// Gets all pending breakpoints (not yet bound to IL).
    /// </summary>
    /// <returns>List of pending breakpoints.</returns>
    public IReadOnlyList<Breakpoint> GetPending()
    {
        return _breakpoints.Values
            .Select(e => e.Breakpoint)
            .Where(bp => bp.State == BreakpointState.Pending)
            .ToList();
    }

    /// <summary>
    /// Gets all bound breakpoints for a specific module.
    /// </summary>
    /// <param name="modulePath">Path to the module.</param>
    /// <returns>List of bound breakpoints for the module.</returns>
    public IReadOnlyList<Breakpoint> GetBoundForModule(string modulePath)
    {
        var normalizedPath = NormalizePath(modulePath);
        return _breakpoints.Values
            .Where(e => e.Breakpoint.State == BreakpointState.Bound &&
                        e.ModulePath != null &&
                        NormalizePath(e.ModulePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Breakpoint)
            .ToList();
    }

    /// <summary>
    /// Updates a breakpoint's native handle and module path when bound.
    /// </summary>
    /// <param name="breakpointId">The breakpoint ID.</param>
    /// <param name="nativeBreakpoint">The ICorDebugFunctionBreakpoint handle.</param>
    /// <param name="modulePath">Path to the module containing the breakpoint.</param>
    /// <returns>True if updated, false if not found.</returns>
    public bool SetNativeBreakpoint(string breakpointId, object? nativeBreakpoint, string? modulePath)
    {
        if (_breakpoints.TryGetValue(breakpointId, out var entry))
        {
            entry.NativeBreakpoint = nativeBreakpoint;
            entry.ModulePath = modulePath;
            _logger.LogDebug("Set native breakpoint for {Id} in module {Module}",
                breakpointId, modulePath);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the native breakpoint handle for a breakpoint.
    /// </summary>
    /// <param name="breakpointId">The breakpoint ID.</param>
    /// <returns>The native handle, or null if not bound.</returns>
    public object? GetNativeBreakpoint(string breakpointId)
    {
        return _breakpoints.TryGetValue(breakpointId, out var entry) ? entry.NativeBreakpoint : null;
    }

    /// <summary>
    /// Gets all native breakpoint handles that are bound.
    /// </summary>
    /// <returns>List of tuples containing breakpoint ID and native handle.</returns>
    public IReadOnlyList<(string Id, object NativeBreakpoint)> GetAllNativeBreakpoints()
    {
        return _breakpoints
            .Where(kvp => kvp.Value.NativeBreakpoint != null)
            .Select(kvp => (kvp.Key, kvp.Value.NativeBreakpoint!))
            .ToList();
    }

    /// <summary>
    /// Adds an exception breakpoint to the registry.
    /// </summary>
    /// <param name="exceptionBreakpoint">The exception breakpoint to add.</param>
    /// <returns>True if added, false if ID already exists.</returns>
    public bool AddException(ExceptionBreakpoint exceptionBreakpoint)
    {
        var entry = new ExceptionBreakpointEntry(exceptionBreakpoint);
        if (_exceptionBreakpoints.TryAdd(exceptionBreakpoint.Id, entry))
        {
            _logger.LogDebug("Added exception breakpoint {Id} for {Type}",
                exceptionBreakpoint.Id, exceptionBreakpoint.ExceptionType);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes an exception breakpoint from the registry.
    /// </summary>
    /// <param name="breakpointId">ID of the exception breakpoint to remove.</param>
    /// <returns>The removed exception breakpoint, or null if not found.</returns>
    public ExceptionBreakpoint? RemoveException(string breakpointId)
    {
        if (_exceptionBreakpoints.TryRemove(breakpointId, out var entry))
        {
            _logger.LogDebug("Removed exception breakpoint {Id}", breakpointId);
            return entry.Breakpoint;
        }
        return null;
    }

    /// <summary>
    /// Gets an exception breakpoint by ID.
    /// </summary>
    /// <param name="breakpointId">The breakpoint ID.</param>
    /// <returns>The exception breakpoint, or null if not found.</returns>
    public ExceptionBreakpoint? GetException(string breakpointId)
    {
        return _exceptionBreakpoints.TryGetValue(breakpointId, out var entry) ? entry.Breakpoint : null;
    }

    /// <summary>
    /// Updates an existing exception breakpoint.
    /// </summary>
    /// <param name="exceptionBreakpoint">The updated exception breakpoint.</param>
    /// <returns>True if updated, false if not found.</returns>
    public bool UpdateException(ExceptionBreakpoint exceptionBreakpoint)
    {
        if (_exceptionBreakpoints.TryGetValue(exceptionBreakpoint.Id, out var entry))
        {
            entry.Breakpoint = exceptionBreakpoint;
            _logger.LogDebug("Updated exception breakpoint {Id}", exceptionBreakpoint.Id);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Finds an exception breakpoint by exact type name.
    /// </summary>
    /// <param name="exceptionType">Full type name of the exception.</param>
    /// <returns>The exception breakpoint, or null if not found.</returns>
    public ExceptionBreakpoint? FindExceptionByType(string exceptionType)
    {
        return _exceptionBreakpoints.Values
            .Select(e => e.Breakpoint)
            .FirstOrDefault(eb => eb.ExceptionType.Equals(exceptionType, StringComparison.Ordinal));
    }

    /// <summary>
    /// Gets all exception breakpoints.
    /// </summary>
    /// <returns>Read-only list of all exception breakpoints.</returns>
    public IReadOnlyList<ExceptionBreakpoint> GetAllExceptions()
    {
        return _exceptionBreakpoints.Values.Select(e => e.Breakpoint).ToList();
    }

    /// <summary>
    /// Finds enabled exception breakpoints matching an exception type.
    /// </summary>
    /// <param name="exceptionTypeName">Full type name of the exception.</param>
    /// <param name="isFirstChance">True if first-chance exception.</param>
    /// <returns>Matching exception breakpoints.</returns>
    public IEnumerable<ExceptionBreakpoint> FindMatchingExceptionBreakpoints(
        string exceptionTypeName,
        bool isFirstChance)
    {
        foreach (var entry in _exceptionBreakpoints.Values)
        {
            var eb = entry.Breakpoint;
            if (!eb.Enabled)
            {
                continue;
            }

            var shouldBreak = isFirstChance ? eb.BreakOnFirstChance : eb.BreakOnSecondChance;
            if (!shouldBreak)
            {
                continue;
            }

            // Type name matching with subtype heuristics
            // Note: True subtype checking would require runtime type hierarchy traversal
            // via ICorDebugValue.ExactType and walking the base type chain.
            // Current implementation uses name-based heuristics which work for most cases:
            // - Exact match: "System.ArgumentException" == "System.ArgumentException"
            // - Suffix match (for subtypes): "System.ArgumentNullException" ends with "ArgumentException"
            // - Simple name match: "ArgumentException" matches "System.ArgumentException"
            bool typeMatches;
            if (eb.IncludeSubtypes)
            {
                // Check for exact match, suffix match, or simple name match
                typeMatches = exceptionTypeName.Equals(eb.ExceptionType, StringComparison.Ordinal) ||
                              exceptionTypeName.EndsWith("." + eb.ExceptionType, StringComparison.Ordinal) ||
                              exceptionTypeName.EndsWith(eb.ExceptionType, StringComparison.Ordinal);
            }
            else
            {
                // Exact match only (full name or simple name)
                typeMatches = exceptionTypeName.Equals(eb.ExceptionType, StringComparison.Ordinal) ||
                              exceptionTypeName.EndsWith("." + eb.ExceptionType, StringComparison.Ordinal);
            }

            if (typeMatches)
            {
                yield return eb;
            }
        }
    }

    /// <summary>
    /// Clears all breakpoints and exception breakpoints.
    /// </summary>
    public void Clear()
    {
        _breakpoints.Clear();
        _exceptionBreakpoints.Clear();
        _logger.LogDebug("Cleared all breakpoints from registry");
    }

    /// <summary>
    /// Gets the count of regular breakpoints.
    /// </summary>
    public int Count => _breakpoints.Count;

    /// <summary>
    /// Gets the count of exception breakpoints.
    /// </summary>
    public int ExceptionCount => _exceptionBreakpoints.Count;

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    /// <summary>
    /// Internal entry for tracking breakpoint with potential native handle.
    /// </summary>
    internal sealed class BreakpointEntry
    {
        public Breakpoint Breakpoint { get; set; }

        // Will hold ICorDebugFunctionBreakpoint when bound
        public object? NativeBreakpoint { get; set; }

        // Path to the module containing this breakpoint (when bound)
        public string? ModulePath { get; set; }

        public BreakpointEntry(Breakpoint breakpoint)
        {
            Breakpoint = breakpoint;
        }
    }

    /// <summary>
    /// Internal entry for tracking exception breakpoints.
    /// </summary>
    private sealed class ExceptionBreakpointEntry
    {
        public ExceptionBreakpoint Breakpoint { get; set; }

        public ExceptionBreakpointEntry(ExceptionBreakpoint breakpoint)
        {
            Breakpoint = breakpoint;
        }
    }
}
