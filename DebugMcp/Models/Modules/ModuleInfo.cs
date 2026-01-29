namespace DebugMcp.Models.Modules;

/// <summary>
/// Represents a loaded assembly/DLL in the debugged process.
/// </summary>
/// <param name="Name">Assembly simple name (e.g., "MyApp").</param>
/// <param name="FullName">Full assembly name with version/culture/key.</param>
/// <param name="Path">File path (null for in-memory modules).</param>
/// <param name="Version">Assembly version (e.g., "1.0.0.0").</param>
/// <param name="IsManaged">True for .NET assemblies.</param>
/// <param name="IsDynamic">True for Reflection.Emit assemblies.</param>
/// <param name="HasSymbols">True if PDB symbols are loaded.</param>
/// <param name="ModuleId">Unique identifier for this module.</param>
/// <param name="BaseAddress">Memory base address (hex string, e.g., "0x00007FF8A1230000").</param>
/// <param name="Size">Module size in bytes.</param>
public sealed record ModuleInfo(
    string Name,
    string FullName,
    string? Path,
    string Version,
    bool IsManaged,
    bool IsDynamic,
    bool HasSymbols,
    string ModuleId,
    string? BaseAddress,
    int Size);
