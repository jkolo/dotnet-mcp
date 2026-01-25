# Research: Memory Inspection Operations

**Feature**: 004-memory-ops
**Date**: 2026-01-25
**Purpose**: Document ICorDebug APIs for memory inspection, object analysis, and reference tracking

## Research Areas

1. Object inspection (heap objects, fields, values)
2. Raw memory reading
3. Object reference analysis (inbound/outbound)
4. Type memory layout

---

## 1. Object Inspection

### Decision: Use ICorDebugObjectValue and ICorDebugValue hierarchy

**Rationale**: ICorDebug provides direct access to heap object internals through typed value interfaces.

**Key APIs (via ClrDebug)**:

```csharp
// Get object from variable reference
CorDebugValue value = ...; // from GetVariables or Evaluate
CorDebugReferenceValue refValue = value as CorDebugReferenceValue;
CorDebugValue dereferencedValue = refValue.Dereference();

// Object instance inspection
CorDebugObjectValue objValue = dereferencedValue as CorDebugObjectValue;
CorDebugClass objClass = objValue.Class;
CorDebugType exactType = objValue.ExactType;

// Get field values
objValue.GetFieldValue(objClass, fieldToken) → CorDebugValue

// Type hierarchy for values
CorDebugValue → base (has Address, Type)
├── CorDebugReferenceValue → reference (IsNull, Dereference)
├── CorDebugObjectValue → object instance (GetFieldValue, Class)
├── CorDebugBoxValue → boxed value type (GetObject)
├── CorDebugArrayValue → array (Count, GetElement)
├── CorDebugStringValue → string (GetString, Length)
└── CorDebugGenericValue → primitive (GetValue<T>)
```

**Field Enumeration Strategy**:
1. Get class metadata token from CorDebugObjectValue.Class
2. Use System.Reflection.Metadata to enumerate field definitions
3. For each field token, call GetFieldValue()
4. Recursively inspect nested objects up to specified depth

**Special Type Handling**:

| Type | Inspection Approach |
|------|---------------------|
| string | Use CorDebugStringValue.GetString(), truncate at 1000 chars |
| array | Use CorDebugArrayValue.Count, GetElement() for first N elements |
| List<T> | Get _items field (internal array) and _size field |
| Dictionary<K,V> | Get _entries field, enumerate non-null entries |
| null | Check CorDebugReferenceValue.IsNull before dereference |
| boxed value | Use CorDebugBoxValue.GetObject() to get inner value |

**Depth Control**:
- Default depth: 1 (immediate fields only)
- Maximum depth: 10 (prevent infinite recursion)
- Circular reference detection via address tracking

**Alternatives Considered**:
- ICorDebugEval for ToString(): Too slow, has side effects
- ClrMD heap walking: Different API, better for post-mortem

---

## 2. Raw Memory Reading

### Decision: Use ICorDebugProcess.ReadMemory

**Rationale**: Direct memory access API with explicit size control.

**Key APIs (via ClrDebug)**:

```csharp
// Read raw bytes from debuggee memory
CorDebugProcess.ReadMemory(
    CORDB_ADDRESS address,  // virtual address in debuggee
    uint size,              // bytes to read
    byte[] buffer,          // output buffer
    out uint bytesRead      // actual bytes read
);

// Get object address
CorDebugValue.Address → CORDB_ADDRESS (ulong)
CorDebugHeapValue.Address → CORDB_ADDRESS
```

**Memory Address Sources**:
- Object reference: CorDebugValue.Address
- Pointer variable: Read pointer value, use as address
- Array element: Calculate from base address + index * element size
- Explicit user address: Pass directly (with validation)

**Safety Constraints**:
- Maximum read size: 64KB per request (configurable)
- Validate address is within process memory space
- Handle ERROR_PARTIAL_COPY (incomplete read at boundary)
- Return actual bytes read if less than requested

**Output Format**:
```json
{
  "address": "0x00007FF8A1234560",
  "requestedSize": 256,
  "actualSize": 256,
  "bytes": "48 65 6C 6C 6F 20 57 6F 72 6C 64 ...",
  "ascii": "Hello World..."
}
```

**Alternatives Considered**:
- ClrMD DataTarget.ReadMemory: Different API surface, same underlying capability
- P/Invoke ReadProcessMemory: Already used by ICorDebug internally

---

## 3. Object Reference Analysis

### Decision: Use ICorDebugObjectValue field enumeration for outbound, heap traversal for inbound

**Rationale**: Outbound references can be derived from field values. Inbound requires broader heap analysis.

**Outbound References (objects referenced by target)**:

```csharp
// Enumerate all reference-type fields
foreach (var field in GetReferenceFields(objValue))
{
    CorDebugValue fieldValue = objValue.GetFieldValue(field.Token);
    if (fieldValue is CorDebugReferenceValue refVal && !refVal.IsNull)
    {
        // This is an outbound reference
        yield return new ReferenceInfo(
            source: targetAddress,
            target: refVal.Dereference().Address,
            path: field.Name
        );
    }
}

// Also check array elements for reference types
if (objValue is CorDebugArrayValue arrayVal && IsReferenceElementType(arrayVal))
{
    for (int i = 0; i < arrayVal.Count; i++)
    {
        var element = arrayVal.GetElement(i);
        if (element is CorDebugReferenceValue refEl && !refEl.IsNull)
        {
            yield return new ReferenceInfo(
                source: targetAddress,
                target: refEl.Dereference().Address,
                path: $"[{i}]"
            );
        }
    }
}
```

**Inbound References (objects referencing target)**:

This requires heap traversal which is expensive. Two approaches:

1. **Limited scope (recommended for Phase 1)**:
   - Track object addresses encountered during variable inspection
   - Only report references from "known" objects
   - Fast but incomplete

2. **Full heap walk (future enhancement)**:
   - Use ICorDebugProcess.EnumerateHeaps() (if available)
   - Or integrate ClrMD for heap enumeration
   - Slow but complete

**Decision for Phase 1**: Implement outbound references only. Inbound references require
heap traversal which is expensive and complex. Can be added later using ClrMD integration
or ICorDebugProcess enhancements.

**Reference Result Limits**:
- Maximum 100 outbound references per request
- Indicate truncation in response

**Alternatives Considered**:
- ICorDebugGCReferenceEnum: Only available in certain scenarios
- ClrMD GCRoot: Complete but requires separate library
- SOS commands (!gcroot): External dependency, parsing overhead

---

## 4. Type Memory Layout

### Decision: Use System.Reflection.Metadata for layout, ICorDebugType for runtime info

**Rationale**: Layout information is in metadata. Runtime type provides actual instantiation details.

**Key APIs**:

```csharp
// Get type metadata
CorDebugType type = objValue.ExactType;
CorDebugClass clazz = type.Class;
uint typeToken = clazz.Token;

// Use System.Reflection.Metadata
MetadataReader reader = GetMetadataReader(module);
TypeDefinition typeDef = reader.GetTypeDefinition(typeToken);

// Get layout info from attributes
foreach (var fieldDef in typeDef.GetFields())
{
    int offset = fieldDef.GetOffset(); // if LayoutKind.Explicit
    // For LayoutKind.Sequential or Auto, compute from field sizes
}

// Runtime size from ICorDebugValue
CorDebugObjectValue.Size → uint (total object size including header)
```

**Layout Computation Strategy**:

For **LayoutKind.Explicit**:
- Read FieldOffset attribute directly from metadata
- Trust user-specified offsets

For **LayoutKind.Sequential**:
- Iterate fields in declaration order
- Compute offset = previous_offset + previous_size + alignment_padding
- Use Pack attribute if specified

For **LayoutKind.Auto** (default for reference types):
- JIT may reorder fields for efficiency
- Get actual runtime offset from ICorDebugObjectValue inspection
- This requires reading the field at known offsets to determine actual layout

**Object Header Overhead**:
- Reference types: 8 bytes (32-bit) or 16 bytes (64-bit) for sync block + method table pointer
- Value types (structs): No header when inline, has header when boxed
- Arrays: Additional length field(s)

**Layout Response Format**:
```json
{
  "typeName": "MyApp.Customer",
  "totalSize": 48,
  "headerSize": 16,
  "fields": [
    { "name": "_id", "type": "int", "offset": 16, "size": 4 },
    { "name": "_name", "type": "string", "offset": 24, "size": 8 },
    { "name": "_balance", "type": "decimal", "offset": 32, "size": 16 }
  ],
  "padding": [
    { "offset": 20, "size": 4, "reason": "alignment" }
  ]
}
```

**Alternatives Considered**:
- SOS !dumpobj: External dependency
- Marshal.SizeOf: Only works for blittable types
- ClrMD TypeLayout: Good option for future enhancement

---

## Implementation Notes

### Shared Infrastructure

1. **Existing from 003-inspection-ops**:
   - ICorDebugValue hierarchy handling in ProcessDebugger
   - Variable inspection with type detection
   - Metadata reading via System.Reflection.Metadata

2. **New infrastructure**:
   - Object address tracking for circular reference detection
   - Memory read buffering and pagination
   - Type layout cache (layout doesn't change during session)

### Error Handling

| Scenario | Error Code | Message |
|----------|------------|---------|
| Not attached | not_attached | No debugging session active |
| Not paused | not_paused | Process must be paused for memory inspection |
| Invalid reference | invalid_reference | Object reference is not valid |
| Null reference | null_reference | Cannot inspect null object |
| Invalid address | invalid_address | Memory address {addr} is not accessible |
| Read failed | memory_read_failed | Failed to read memory at {addr}: {reason} |
| Size exceeded | size_exceeded | Requested size {n} exceeds maximum {max} |
| Depth exceeded | depth_exceeded | Expansion depth {n} exceeds maximum {max} |
| Type not found | type_not_found | Type {name} not found in loaded modules |

### Performance Considerations

1. **Object inspection**: Cache type metadata per module load
2. **Memory reading**: Use 4KB pages for large reads
3. **Reference analysis**: Limit traversal depth, use address set for visited tracking
4. **Layout queries**: Cache per type (immutable for session duration)

---

## References

- [ICorDebugValue Interface](https://docs.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/icordebugvalue-interface)
- [ICorDebugObjectValue Interface](https://docs.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/icordebugobjectvalue-interface)
- [ICorDebugProcess::ReadMemory](https://docs.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/icordebugprocess-readmemory-method)
- [System.Reflection.Metadata](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.metadata)
- [ClrDebug NuGet Package](https://www.nuget.org/packages/ClrDebug)
- Existing 003-inspection-ops research.md for value handling patterns
