# Data Model: Memory Inspection Operations

**Feature**: 004-memory-ops
**Date**: 2026-01-25
**Purpose**: Define entities for memory inspection, object analysis, and reference tracking

## Entities

### ObjectInspection

Result of inspecting a heap object's contents.

```
ObjectInspection
├── address: string          # Hex address of the object (e.g., "0x00007FF8A1234560")
├── typeName: string         # Full type name (e.g., "MyApp.Models.Customer")
├── size: int                # Total object size in bytes (including header)
├── fields: FieldDetail[]    # Instance fields with values
├── isNull: bool             # True if the reference was null
├── hasCircularRef: bool     # True if circular reference detected during expansion
└── truncated: bool          # True if field list was truncated (too many fields)
```

**Validation Rules**:
- address must be valid hex format starting with "0x"
- typeName must not be empty for non-null objects
- size must be > 0 for non-null objects
- fields can be empty for types with no instance fields

### FieldDetail

Information about a single object field.

```
FieldDetail
├── name: string             # Field name (e.g., "_customerId")
├── typeName: string         # Field type name (e.g., "System.Int32")
├── value: string            # Formatted value (e.g., "42", "\"Hello\"", "{Customer}")
├── offset: int              # Byte offset from object start
├── size: int                # Field size in bytes
├── hasChildren: bool        # True if field can be expanded (reference/complex type)
├── childCount: int?         # Number of children (for arrays/collections)
└── isStatic: bool           # True if static field (included optionally)
```

**Value Formatting Rules**:

| Type | Format Example |
|------|----------------|
| null | `"null"` |
| string | `"\"Hello World\""` (escaped, max 100 chars + "...") |
| int/long/short/byte | `"42"` |
| float/double | `"3.14159"` |
| bool | `"true"` or `"false"` |
| char | `"'A'"` |
| enum | `"Monday (1)"` |
| DateTime | `"2026-01-25T10:30:00Z"` |
| Guid | `"a1b2c3d4-..."` |
| array | `"Int32[10]"` |
| object | `"{Customer}"` |
| collection | `"List<String> (Count=5)"` |

### MemoryRegion

Result of reading raw memory bytes.

```
MemoryRegion
├── address: string          # Start address in hex (e.g., "0x00007FF8A1234560")
├── requestedSize: int       # Bytes requested
├── actualSize: int          # Bytes actually read (may be less at boundary)
├── bytes: string            # Hex dump (e.g., "48 65 6C 6C 6F ...")
├── ascii: string            # ASCII representation (non-printable as '.')
└── error: string?           # Error message if partial read
```

**Format Rules**:
- bytes: Space-separated hex pairs, 16 per line
- ascii: Printable ASCII (0x20-0x7E), others replaced with '.'
- Maximum size: 65536 bytes (64KB)

### ReferenceInfo

Information about an object reference relationship.

```
ReferenceInfo
├── sourceAddress: string    # Address of referencing object
├── sourceType: string       # Type of referencing object
├── targetAddress: string    # Address of referenced object
├── targetType: string       # Type of referenced object
├── path: string             # Path from source to target (e.g., "_customer", "[0]")
└── referenceType: ReferenceType  # Type of reference
```

**ReferenceType Enum**:
```
ReferenceType
├── Field                    # Regular instance field
├── ArrayElement             # Array element reference
├── StaticField              # Static field reference
└── WeakReference            # WeakReference<T> (may be collected)
```

### ReferencesResult

Result of reference analysis for an object.

```
ReferencesResult
├── targetAddress: string    # Address of the analyzed object
├── targetType: string       # Type of the analyzed object
├── outbound: ReferenceInfo[]  # Objects this object references
├── inbound: ReferenceInfo[]   # Objects referencing this object (Phase 2)
├── outboundCount: int       # Total outbound refs (may exceed returned list)
├── inboundCount: int        # Total inbound refs (may exceed returned list)
└── truncated: bool          # True if results were truncated
```

### TypeLayout

Memory layout information for a type.

```
TypeLayout
├── typeName: string         # Full type name
├── totalSize: int           # Total size in bytes (runtime size)
├── headerSize: int          # Object header size (0 for value types)
├── dataSize: int            # Size of fields (totalSize - headerSize)
├── fields: LayoutField[]    # Fields with layout info
├── padding: PaddingRegion[] # Padding between fields
└── isValueType: bool        # True for struct, false for class
```

### LayoutField

Field layout information within a type.

```
LayoutField
├── name: string             # Field name
├── typeName: string         # Field type
├── offset: int              # Byte offset from data start (after header)
├── size: int                # Field size in bytes
├── alignment: int           # Required alignment
└── isReference: bool        # True for reference types (pointer-sized)
```

### PaddingRegion

Padding between fields for alignment.

```
PaddingRegion
├── offset: int              # Start offset of padding
├── size: int                # Padding size in bytes
└── reason: string           # Reason for padding (e.g., "alignment for Int64")
```

---

## State Transitions

### ObjectInspection Flow

```
[Not Attached]
    ↓ debug_attach
[Attached/Running]
    ↓ breakpoint hit / debug_pause
[Paused]
    ↓ object_inspect(ref)
[Inspecting]
    ↓ success
[Inspection Result] → return ObjectInspection
    ↓ error
[Error] → return error response
```

### Memory Read Flow

```
[Paused]
    ↓ memory_read(address, size)
[Validating]
    ↓ valid address
[Reading]
    ↓ complete
[Result] → return MemoryRegion
    ↓ partial read
[Partial] → return MemoryRegion with actualSize < requestedSize
    ↓ invalid address
[Error] → return error response
```

---

## Relationships

```
ObjectInspection ──┬── 1:N → FieldDetail
                   └── uses → TypeLayout (for field offsets)

ReferencesResult ──┬── 1:N → ReferenceInfo (outbound)
                   └── 1:N → ReferenceInfo (inbound)

TypeLayout ──┬── 1:N → LayoutField
             └── 1:N → PaddingRegion
```

---

## Constraints

1. **Memory Access**: All memory inspection requires paused debug session
2. **Address Validity**: Addresses must be within process virtual address space
3. **Size Limits**: Memory read limited to 64KB per request
4. **Depth Limits**: Object expansion limited to depth 10
5. **Reference Limits**: Maximum 100 references returned per request
6. **String Truncation**: Strings truncated at 1000 characters
7. **Array Preview**: Arrays show first 100 elements in preview

---

## Error Responses

All tools return structured error on failure:

```
ErrorResponse
├── success: false
├── error
│   ├── code: string         # Machine-readable error code
│   ├── message: string      # Human-readable description
│   └── details: object?     # Additional context
```

**Error Codes**:
- `not_attached` - No active debug session
- `not_paused` - Process must be paused
- `invalid_reference` - Object reference is invalid
- `null_reference` - Cannot inspect null
- `invalid_address` - Memory address not accessible
- `memory_read_failed` - Memory read operation failed
- `size_exceeded` - Requested size exceeds limit
- `depth_exceeded` - Expansion depth exceeds limit
- `type_not_found` - Type not found in loaded modules
