# Quickstart: Memory Inspection Operations

**Feature**: 004-memory-ops
**Prerequisites**: Active debug session (paused state), object reference from variables_get or evaluate

## Overview

Memory inspection tools enable AI assistants to examine heap objects, read raw memory,
analyze object references, and understand type memory layouts during debugging sessions.

## Tools

| Tool | Purpose | Common Use Case |
|------|---------|-----------------|
| `object_inspect` | Inspect heap object contents | Examine object fields and values |
| `memory_read` | Read raw memory bytes | Analyze buffers, native interop data |
| `references_get` | Analyze object references | Debug memory leaks, understand object graphs |
| `layout_get` | Get type memory layout | Optimize memory usage, understand padding |

---

## Tool: object_inspect

### Basic Usage

```json
{
  "tool": "object_inspect",
  "parameters": {
    "object_ref": "customer"
  }
}
```

### Response

```json
{
  "success": true,
  "inspection": {
    "address": "0x00007FF8A1234560",
    "typeName": "MyApp.Models.Customer",
    "size": 48,
    "fields": [
      {
        "name": "_id",
        "typeName": "System.Int32",
        "value": "42",
        "offset": 16,
        "size": 4,
        "hasChildren": false
      },
      {
        "name": "_name",
        "typeName": "System.String",
        "value": "\"John Doe\"",
        "offset": 24,
        "size": 8,
        "hasChildren": false
      },
      {
        "name": "_orders",
        "typeName": "System.Collections.Generic.List<Order>",
        "value": "List<Order> (Count=3)",
        "offset": 32,
        "size": 8,
        "hasChildren": true,
        "childCount": 3
      }
    ],
    "isNull": false,
    "hasCircularRef": false,
    "truncated": false
  }
}
```

### Expand Nested Objects

```json
{
  "tool": "object_inspect",
  "parameters": {
    "object_ref": "customer._orders",
    "depth": 2
  }
}
```

### Common Scenarios

**Scenario 1: Null Reference Investigation**

```
AI: The customer variable appears to be null. Let me inspect it.
[Calls object_inspect with object_ref: "customer"]

Result: { "isNull": true }

AI: Confirmed - customer is null. The NullReferenceException at line 42
    is caused by accessing customer.Name when customer is null.
```

**Scenario 2: Collection Inspection**

```
AI: Let me check what's in the orders collection.
[Calls object_inspect with object_ref: "customer._orders", depth: 2]

AI: The list contains 3 Order objects. The first order has:
    - OrderId: 1001
    - Total: 149.99
    - Items: List<OrderItem> (Count=2)
```

---

## Tool: memory_read

### Basic Usage

```json
{
  "tool": "memory_read",
  "parameters": {
    "address": "0x00007FF8A1234560",
    "size": 64
  }
}
```

### Response

```json
{
  "success": true,
  "memory": {
    "address": "0x00007FF8A1234560",
    "requestedSize": 64,
    "actualSize": 64,
    "bytes": "48 65 6C 6C 6F 20 57 6F 72 6C 64 21 00 00 00 00",
    "ascii": "Hello World!...."
  }
}
```

### Common Scenarios

**Scenario: Inspect String Buffer**

```
AI: The string appears corrupted. Let me read the raw memory.
[Calls memory_read with address from string variable, size: 128]

AI: The memory dump shows the string data:
    48 65 6C 6C 6F 00 00 00 ...
    "Hello"

    There's a null terminator at byte 5, explaining the truncation.
```

**Scenario: Native Interop Buffer**

```
AI: Let me check the native buffer passed to the unmanaged code.
[Calls memory_read with IntPtr address, size: 256]

AI: The buffer contains binary data starting with magic bytes
    "PK 03 04" - this is a ZIP file header.
```

---

## Tool: references_get

### Basic Usage

```json
{
  "tool": "references_get",
  "parameters": {
    "object_ref": "leakedObject",
    "direction": "outbound"
  }
}
```

### Response

```json
{
  "success": true,
  "references": {
    "targetAddress": "0x00007FF8A1234560",
    "targetType": "MyApp.Services.CacheEntry",
    "outbound": [
      {
        "sourceAddress": "0x00007FF8A1234560",
        "sourceType": "MyApp.Services.CacheEntry",
        "targetAddress": "0x00007FF8A1234680",
        "targetType": "MyApp.Models.LargeData",
        "path": "_data",
        "referenceType": "Field"
      }
    ],
    "outboundCount": 1,
    "truncated": false
  }
}
```

### Common Scenarios

**Scenario: Memory Leak Investigation**

```
AI: This object should have been garbage collected. Let me check what's
    holding a reference to it.
[Calls references_get with direction: "inbound"]

AI: Found the leak! The object is referenced by:
    - StaticCache._entries[42] → keeps the object rooted

    The cache entry was never removed after the operation completed.
```

**Scenario: Understand Object Graph**

```
AI: Let me map out what this service object references.
[Calls references_get with direction: "outbound", max_results: 100]

AI: The OrderService holds references to:
    - _repository (DbRepository)
    - _logger (ILogger)
    - _cache (MemoryCache)
    - _eventBus (EventBus)

    The _cache is holding 50MB of data - this might be excessive.
```

---

## Tool: layout_get

### Basic Usage

```json
{
  "tool": "layout_get",
  "parameters": {
    "type_name": "MyApp.Models.Customer"
  }
}
```

### Response

```json
{
  "success": true,
  "layout": {
    "typeName": "MyApp.Models.Customer",
    "totalSize": 48,
    "headerSize": 16,
    "dataSize": 32,
    "fields": [
      { "name": "_id", "typeName": "System.Int32", "offset": 0, "size": 4 },
      { "name": "_name", "typeName": "System.String", "offset": 8, "size": 8 },
      { "name": "_balance", "typeName": "System.Decimal", "offset": 16, "size": 16 }
    ],
    "padding": [
      { "offset": 4, "size": 4, "reason": "Alignment for System.String" }
    ],
    "isValueType": false
  }
}
```

### Common Scenarios

**Scenario: Optimize Struct Layout**

```
AI: Let me check the memory layout of this struct used in the hot path.
[Calls layout_get with type_name: "MyApp.Data.TradeRecord"]

AI: The TradeRecord struct is 48 bytes with 8 bytes of padding.
    Current layout:
    - bool IsActive (1 byte) + 7 padding
    - long Timestamp (8 bytes)
    - decimal Price (16 bytes)
    - int Quantity (4 bytes) + 4 padding
    - long OrderId (8 bytes)

    Reordering fields could eliminate padding:
    - long Timestamp
    - long OrderId
    - decimal Price
    - int Quantity
    - bool IsActive + 3 padding

    This would save 8 bytes per record.
```

---

## Workflow Example: Debugging Memory Issue

```
1. AI: Let me check the current state of the cache.
   [Calls variables_get to get cache variable]

2. AI: The cache has 10,000 entries. Let me inspect one.
   [Calls object_inspect on cache entry]

3. AI: Each entry holds a large _data field. Let me check the layout.
   [Calls layout_get on CacheEntry type]

4. AI: Each entry is 128 bytes. With 10,000 entries that's 1.28MB.
   Let me verify with raw memory.
   [Calls memory_read on cache._entries array]

5. AI: I found the issue - entries are not being evicted properly.
   Let me check what's keeping the old entries alive.
   [Calls references_get on old entry]

6. AI: The entry is still referenced by a timer callback that was
   never cancelled. Here's the fix...
```

---

## Error Handling

All tools return structured errors:

```json
{
  "success": false,
  "error": {
    "code": "not_paused",
    "message": "Process must be paused for memory inspection",
    "details": {
      "currentState": "running"
    }
  }
}
```

Common errors:
- `not_attached` - No debug session active
- `not_paused` - Call `debug_pause` first
- `invalid_reference` - Object reference is not valid
- `invalid_address` - Memory address not accessible
- `size_exceeded` - Requested size exceeds 64KB limit
