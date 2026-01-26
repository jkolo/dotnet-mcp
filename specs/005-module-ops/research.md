# Research: Module and Assembly Inspection

**Feature**: 005-module-ops
**Date**: 2026-01-25
**Purpose**: Resolve technical unknowns before implementation

## Research Questions

### R1: How to enumerate loaded modules via ICorDebug?

**Decision**: Use `ICorDebugAppDomain.EnumerateAssemblies()` to get loaded assemblies, then
`ICorDebugAssembly.EnumerateModules()` for multi-module assemblies.

**Rationale**: ICorDebug provides direct access to module enumeration without requiring the
process to be paused. This is the same API used by professional debuggers (VS, WinDbg).

**ClrDebug wrapper usage**:
```csharp
// Get app domain (typically one for most .NET apps)
var appDomains = process.AppDomains;
foreach (var domain in appDomains)
{
    // Enumerate assemblies in domain
    var assemblies = domain.Assemblies;
    foreach (var assembly in assemblies)
    {
        // Get modules in assembly
        var modules = assembly.Modules;
        foreach (var module in modules)
        {
            var name = module.Name;
            // Module metadata available via GetMetaDataInterface
        }
    }
}
```

**Alternatives considered**:
- `ICorDebugProcess.EnumerateModules()` - doesn't exist, assemblies are per-AppDomain
- CLR profiling API - requires profiler attach, too heavyweight for debugging

---

### R2: How to read type metadata from modules?

**Decision**: Use `ICorDebugModule.GetMetaDataInterface()` to get `IMetaDataImport`, then
enumerate types with `EnumTypeDefs()` or use System.Reflection.Metadata for portable parsing.

**Rationale**: System.Reflection.Metadata is in-box (.NET), provides a clean API for reading
PE/COFF metadata without requiring COM interop. It can read metadata from module file path
or in-memory stream.

**Implementation approach**:
1. Get module path from `ICorDebugModule.Name`
2. Open PEReader from file (if path available) or memory-map
3. Use MetadataReader to enumerate TypeDefinitions, MethodDefinitions, etc.

```csharp
using var peReader = new PEReader(File.OpenRead(modulePath));
var metadataReader = peReader.GetMetadataReader();

foreach (var typeDefHandle in metadataReader.TypeDefinitions)
{
    var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
    var typeName = metadataReader.GetString(typeDef.Name);
    var namespaceName = metadataReader.GetString(typeDef.Namespace);
    var attributes = typeDef.Attributes; // visibility, sealed, abstract, etc.
}
```

**Alternatives considered**:
- IMetaDataImport COM interface - works but more complex, requires COM interop
- Mono.Cecil - third-party dependency, no advantage over in-box option
- Reflection on loaded assemblies - requires loading into Claude's process, security risk

---

### R3: How to handle modules loaded from memory (no file path)?

**Decision**: For in-memory modules, use `ICorDebugModule.GetBaseAddress()` to read metadata
directly from debuggee process memory, or mark as "in-memory" with limited metadata.

**Rationale**: Dynamically generated assemblies (Reflection.Emit, etc.) don't have file paths.
We can still get basic info from ICorDebugModule and attempt metadata reading from memory.

**Implementation approach**:
```csharp
var modulePath = module.Name;
if (File.Exists(modulePath))
{
    // Read from file - fast path
    return ReadMetadataFromFile(modulePath);
}
else
{
    // In-memory module
    var baseAddress = module.BaseAddress;
    if (baseAddress != 0)
    {
        // Read PE header from process memory
        return ReadMetadataFromMemory(process, baseAddress);
    }
    else
    {
        // Dynamic module - minimal info available
        return new ModuleInfo { Name = modulePath, IsDynamic = true };
    }
}
```

**Alternatives considered**:
- Always require file path - excludes dynamic assemblies, incomplete
- Skip in-memory modules - loses information about Reflection.Emit types

---

### R4: How to distinguish managed vs native modules?

**Decision**: ICorDebug only enumerates managed assemblies. Native modules (ntdll.dll, etc.)
are not visible through ICorDebugAppDomain.EnumerateAssemblies().

**Rationale**: ICorDebug is specifically for managed debugging. If native module listing is
needed, it would require separate Win32 API calls (EnumProcessModules), which is outside
scope for initial implementation.

**Implementation approach**:
- All modules from ICorDebug enumeration are managed
- Mark all as `IsManaged: true`
- Future enhancement could add native module listing via separate tool

---

### R5: How to determine if symbols (PDB) are loaded?

**Decision**: Check if `ICorDebugModule.GetSymbolReader()` returns a valid reader, or use
`ICorDebugModule3.GetDebuggerStatus()` flags.

**Rationale**: Symbol availability affects debugging experience (source stepping, local
variable names). Important metadata for AI assistants to know.

**Implementation approach**:
```csharp
bool hasSymbols;
try
{
    var symReader = module.SymbolReader;
    hasSymbols = symReader != null;
}
catch
{
    hasSymbols = false;
}
```

**Alternatives considered**:
- Check for .pdb file existence - doesn't account for embedded PDBs or symbol servers
- Use ICorDebugModule3 - more accurate but more complex

---

### R6: How to handle generic types and methods?

**Decision**: Use MetadataReader's TypeSpecification and MethodSpecification handles, display
generic parameters using `<T>` syntax.

**Rationale**: Generic types are common in .NET code. Display should be user-friendly
(e.g., `List<T>` not `List`1[T]`).

**Implementation approach**:
```csharp
// Get generic parameters
var genericParams = typeDef.GetGenericParameters();
if (genericParams.Count > 0)
{
    var paramNames = genericParams.Select(p =>
        metadataReader.GetString(metadataReader.GetGenericParameter(p).Name));
    displayName = $"{typeName}<{string.Join(", ", paramNames)}>";
}
```

---

### R7: Performance considerations for large modules?

**Decision**: Implement pagination with continuation tokens and result limits (max 100 per
request). Cache metadata readers per module.

**Rationale**: Modules like mscorlib/System.Private.CoreLib have thousands of types.
Returning all at once would be slow and overwhelming for AI context.

**Implementation approach**:
1. Default limit: 100 items per request
2. Return `hasMore: true` and `continuationToken` when truncated
3. Cache MetadataReader instances (they're read-only, safe to cache)
4. Namespace filtering reduces result set before pagination

---

## Key Decisions Summary

| Area | Decision |
|------|----------|
| Module enumeration | ICorDebugAppDomain.EnumerateAssemblies() |
| Metadata reading | System.Reflection.Metadata (in-box) |
| In-memory modules | Read from process memory or mark as dynamic |
| Native modules | Out of scope (ICorDebug is managed-only) |
| Symbols check | ICorDebugModule.GetSymbolReader() presence |
| Generics display | `<T>` syntax for user-friendly names |
| Large modules | Pagination with 100 item limit, caching |

## Dependencies

- **ClrDebug**: Already in project, provides ICorDebug wrappers
- **System.Reflection.Metadata**: In-box with .NET 10, no new dependency needed
- **001-debug-session**: Requires active CorDebugProcess from attach/launch
