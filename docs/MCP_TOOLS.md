# MCP Tools Reference

Complete reference for all debugging tools exposed by DotnetMcp.

## Session Management

### debug_launch

Launch a .NET application for debugging.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `program` | string | Yes | Path to .NET DLL or project file |
| `args` | string[] | No | Command line arguments |
| `cwd` | string | No | Working directory |
| `env` | object | No | Environment variables |
| `stop_at_entry` | boolean | No | Break on entry point (default: false) |

**Example:**
```json
{
  "program": "/app/MyService.dll",
  "args": ["--environment", "Development"],
  "cwd": "/app",
  "env": {
    "ASPNETCORE_URLS": "http://localhost:5000",
    "DOTNET_ENVIRONMENT": "Development"
  },
  "stop_at_entry": true
}
```

**Response:**
```json
{
  "success": true,
  "pid": 12345,
  "state": "stopped",
  "message": "Process launched and stopped at entry point"
}
```

---

### debug_attach

Attach to a running .NET process.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `pid` | integer | Yes | Process ID to attach to |

**Example:**
```json
{
  "pid": 12345
}
```

**Response:**
```json
{
  "success": true,
  "pid": 12345,
  "state": "running",
  "process_name": "MyService",
  "runtime_version": ".NET 8.0.1"
}
```

**Errors:**
- `process_not_found` — No process with given PID
- `not_managed` — Process is not running .NET
- `already_attached` — A debugger is already attached

---

### debug_disconnect

End the debugging session.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `terminate` | boolean | No | Kill the process (default: false) |

**Example:**
```json
{
  "terminate": false
}
```

**Response:**
```json
{
  "success": true,
  "message": "Detached from process 12345"
}
```

---

### debug_state

Get the current debugging state.

**Parameters:** None

**Response:**
```json
{
  "state": "stopped",
  "reason": "breakpoint",
  "thread_id": 5,
  "breakpoint_id": 1,
  "location": {
    "file": "/app/Services/UserService.cs",
    "line": 42,
    "column": 12,
    "function": "GetUser"
  }
}
```

**State values:**

| State | Description |
|-------|-------------|
| `not_attached` | No debugging session active |
| `running` | Process is executing |
| `stopped` | Process is paused |
| `exited` | Process has terminated |

**Reason values (when stopped):**

| Reason | Description |
|--------|-------------|
| `breakpoint` | Hit a breakpoint |
| `step` | Step operation completed |
| `pause` | User requested pause |
| `exception` | Exception thrown |
| `entry_point` | Stopped at program entry |

---

## Breakpoints

### breakpoint_set

Set a breakpoint in source code.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `file` | string | Yes* | Source file path |
| `line` | integer | Yes* | Line number (1-based) |
| `column` | integer | No | Column for specific position |
| `function` | string | No* | Full method name (alternative to file/line) |
| `condition` | string | No | Condition expression |
| `hit_count` | integer | No | Break after N hits |
| `log_message` | string | No | Log message (logpoint) |

*Either `file`+`line` or `function` is required.

**Examples:**

Basic breakpoint:
```json
{
  "file": "Services/UserService.cs",
  "line": 42
}
```

Conditional breakpoint:
```json
{
  "file": "Services/UserService.cs",
  "line": 42,
  "condition": "userId == null || userId.Length == 0"
}
```

Function breakpoint:
```json
{
  "function": "MyApp.Services.UserService.GetUser"
}
```

Logpoint (doesn't break, just logs):
```json
{
  "file": "Services/UserService.cs",
  "line": 42,
  "log_message": "GetUser called with userId={userId}"
}
```

**Response:**
```json
{
  "id": 1,
  "verified": true,
  "location": {
    "file": "/app/Services/UserService.cs",
    "line": 42
  }
}
```

If source not yet loaded:
```json
{
  "id": 2,
  "verified": false,
  "message": "Breakpoint pending, module not loaded"
}
```

---

### breakpoint_remove

Remove a breakpoint.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `id` | integer | Yes | Breakpoint ID |

**Response:**
```json
{
  "success": true,
  "id": 1
}
```

---

### breakpoint_list

List all breakpoints.

**Parameters:** None

**Response:**
```json
{
  "breakpoints": [
    {
      "id": 1,
      "verified": true,
      "enabled": true,
      "file": "/app/Services/UserService.cs",
      "line": 42,
      "hit_count": 3,
      "condition": null
    },
    {
      "id": 2,
      "verified": false,
      "enabled": true,
      "file": "/app/Services/OrderService.cs",
      "line": 100,
      "hit_count": 0,
      "condition": "order.Total > 1000"
    }
  ]
}
```

---

### breakpoint_wait

Wait for any breakpoint to be hit.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `timeout_ms` | integer | No | Timeout in milliseconds (default: 30000) |
| `breakpoint_id` | integer | No | Wait for specific breakpoint |

**Example:**
```json
{
  "timeout_ms": 60000,
  "breakpoint_id": 1
}
```

**Response (success):**
```json
{
  "hit": true,
  "breakpoint_id": 1,
  "thread_id": 5,
  "location": {
    "file": "/app/Services/UserService.cs",
    "line": 42,
    "column": 8,
    "function": "GetUser",
    "module": "MyApp.dll"
  },
  "hit_count": 1
}
```

**Response (timeout):**
```json
{
  "hit": false,
  "reason": "timeout",
  "message": "No breakpoint hit within 60000ms"
}
```

---

### breakpoint_set_exception

Set an exception breakpoint to break when specific exception types are thrown.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `exception_type` | string | Yes | Full exception type name (e.g., System.NullReferenceException) |
| `break_on_first_chance` | boolean | No | Break on first-chance exception (default: true) |
| `break_on_second_chance` | boolean | No | Break on second-chance/unhandled exception (default: true) |
| `include_subtypes` | boolean | No | Also break on derived exception types (default: true) |

**Example:**
```json
{
  "exception_type": "System.NullReferenceException",
  "break_on_first_chance": true,
  "break_on_second_chance": true,
  "include_subtypes": true
}
```

**Response:**
```json
{
  "success": true,
  "breakpoint": {
    "id": "ex-550e8400-e29b-41d4-a716-446655440000",
    "exceptionType": "System.NullReferenceException",
    "breakOnFirstChance": true,
    "breakOnSecondChance": true,
    "includeSubtypes": true,
    "enabled": true,
    "verified": true,
    "hitCount": 0
  }
}
```

**Errors:**
- `invalid_condition` — Invalid exception type format
- `invalid_condition` — Must specify at least first-chance or second-chance

---

### breakpoint_enable

Enable or disable a breakpoint without removing it.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `id` | string | Yes | Breakpoint ID |
| `enabled` | boolean | No | True to enable, false to disable (default: true) |

**Example (disable):**
```json
{
  "id": "bp-550e8400-e29b-41d4-a716-446655440000",
  "enabled": false
}
```

**Example (re-enable):**
```json
{
  "id": "bp-550e8400-e29b-41d4-a716-446655440000",
  "enabled": true
}
```

**Response:**
```json
{
  "success": true,
  "breakpoint": {
    "id": "bp-550e8400-e29b-41d4-a716-446655440000",
    "location": {
      "file": "/app/Services/UserService.cs",
      "line": 42
    },
    "state": "bound",
    "enabled": false,
    "verified": true,
    "hitCount": 3
  }
}
```

**Errors:**
- `breakpoint_not_found` — No breakpoint with given ID

---

## Execution Control

### debug_continue

Resume execution.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `thread_id` | integer | No | Resume specific thread only |

**Response:**
```json
{
  "success": true,
  "state": "running"
}
```

---

### debug_pause

Pause execution.

**Parameters:** None

**Response:**
```json
{
  "success": true,
  "state": "stopped",
  "threads": [
    { "id": 1, "location": "System.Threading.Thread.Sleep" },
    { "id": 5, "location": "MyApp.Services.UserService.GetUser" }
  ]
}
```

---

### debug_step_over

Execute current line and stop at next line (step over).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `thread_id` | integer | No | Thread to step (default: current) |

**Response:**
```json
{
  "success": true,
  "location": {
    "file": "/app/Services/UserService.cs",
    "line": 43,
    "function": "GetUser"
  }
}
```

---

### debug_step_into

Step into the next method call.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `thread_id` | integer | No | Thread to step |

**Response:**
```json
{
  "success": true,
  "location": {
    "file": "/app/Data/UserRepository.cs",
    "line": 15,
    "function": "FindById"
  }
}
```

---

### debug_step_out

Step out of current method.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `thread_id` | integer | No | Thread to step |

**Response:**
```json
{
  "success": true,
  "location": {
    "file": "/app/Controllers/UserController.cs",
    "line": 28,
    "function": "Get"
  }
}
```

---

## Inspection

### threads_list

List all managed threads.

**Parameters:** None

**Response:**
```json
{
  "threads": [
    {
      "id": 1,
      "name": "Main Thread",
      "state": "running",
      "is_current": false
    },
    {
      "id": 5,
      "name": ".NET ThreadPool Worker",
      "state": "stopped",
      "is_current": true,
      "location": {
        "function": "GetUser",
        "file": "UserService.cs",
        "line": 42
      }
    },
    {
      "id": 8,
      "name": "Background Worker",
      "state": "waiting"
    }
  ]
}
```

---

### stacktrace_get

Get stack trace for a thread.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `thread_id` | integer | No | Thread ID (default: current) |
| `start_frame` | integer | No | Start from frame N (default: 0) |
| `max_frames` | integer | No | Max frames to return (default: 20) |

**Response:**
```json
{
  "thread_id": 5,
  "total_frames": 15,
  "frames": [
    {
      "index": 0,
      "function": "GetUser",
      "file": "/app/Services/UserService.cs",
      "line": 42,
      "column": 12,
      "module": "MyApp.dll",
      "arguments": [
        { "name": "userId", "type": "string", "value": "\"abc123\"" }
      ]
    },
    {
      "index": 1,
      "function": "Get",
      "file": "/app/Controllers/UserController.cs",
      "line": 28,
      "module": "MyApp.dll"
    },
    {
      "index": 2,
      "function": "InvokeAction",
      "module": "Microsoft.AspNetCore.Mvc.Core.dll",
      "is_external": true
    }
  ]
}
```

---

### variables_get

Get variables for a stack frame.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `thread_id` | integer | No | Thread ID (default: current) |
| `frame_index` | integer | No | Frame index (default: 0 = top) |
| `scope` | string | No | `locals`, `arguments`, `this`, or `all` (default: `all`) |
| `expand` | string | No | Variable path to expand children |

**Response:**
```json
{
  "variables": [
    {
      "name": "this",
      "type": "MyApp.Services.UserService",
      "value": "{UserService}",
      "has_children": true,
      "children_count": 3
    },
    {
      "name": "userId",
      "type": "string",
      "value": "\"\"",
      "has_children": false,
      "scope": "argument"
    },
    {
      "name": "user",
      "type": "MyApp.Models.User",
      "value": "null",
      "has_children": false,
      "scope": "local"
    },
    {
      "name": "cache",
      "type": "Dictionary<string, User>",
      "value": "Count = 5",
      "has_children": true,
      "children_count": 5,
      "scope": "local"
    }
  ]
}
```

**Expanding children:**
```json
{
  "expand": "this._repository"
}
```

**Response:**
```json
{
  "variables": [
    {
      "name": "_connectionString",
      "type": "string",
      "value": "\"Server=localhost;...\"",
      "parent": "this._repository"
    },
    {
      "name": "_logger",
      "type": "ILogger<UserRepository>",
      "value": "{Logger}",
      "has_children": true,
      "parent": "this._repository"
    }
  ]
}
```

---

### evaluate

Evaluate an expression.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `expression` | string | Yes | C# expression to evaluate |
| `thread_id` | integer | No | Thread context |
| `frame_index` | integer | No | Stack frame context |
| `format` | string | No | Output format: `default`, `hex`, `binary` |

**Examples:**

Simple variable:
```json
{
  "expression": "userId"
}
```

Method call:
```json
{
  "expression": "user?.GetFullName()"
}
```

Complex expression:
```json
{
  "expression": "users.Where(u => u.IsActive).Count()"
}
```

**Response:**
```json
{
  "result": "\"John Doe\"",
  "type": "string",
  "has_children": false
}
```

**For objects:**
```json
{
  "result": "{User}",
  "type": "MyApp.Models.User",
  "has_children": true,
  "children": [
    { "name": "Id", "value": "\"abc123\"" },
    { "name": "Name", "value": "\"John\"" },
    { "name": "Email", "value": "\"john@example.com\"" }
  ]
}
```

**Errors:**
```json
{
  "error": true,
  "message": "Object reference not set to an instance of an object",
  "type": "NullReferenceException"
}
```

---

## Memory Inspection

### object_inspect

Inspect a heap object's contents including all fields.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `object_ref` | string | Yes | Object reference (variable name or expression, e.g., 'customer', 'this._orders') |
| `depth` | integer | No | Maximum depth for nested object expansion (1-10, default: 1) |
| `thread_id` | integer | No | Thread ID (default: current thread) |
| `frame_index` | integer | No | Frame index (0 = top of stack, default: 0) |

**Example:**
```json
{
  "object_ref": "customer",
  "depth": 2
}
```

**Response:**
```json
{
  "success": true,
  "inspection": {
    "address": "0x00007FF8A1234560",
    "typeName": "MyApp.Models.Customer",
    "size": 48,
    "fields": [
      {
        "name": "Id",
        "typeName": "System.Int32",
        "value": "42",
        "offset": 8,
        "size": 4,
        "hasChildren": false,
        "childCount": 0
      },
      {
        "name": "Name",
        "typeName": "System.String",
        "value": "\"John Doe\"",
        "offset": 16,
        "size": 8,
        "hasChildren": true,
        "childCount": 8
      },
      {
        "name": "Orders",
        "typeName": "System.Collections.Generic.List`1[MyApp.Models.Order]",
        "value": "Count = 3",
        "offset": 24,
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

**Response (null object):**
```json
{
  "success": true,
  "inspection": {
    "isNull": true,
    "typeName": "MyApp.Models.Customer"
  }
}
```

**Errors:**
- `NOT_PAUSED` — Process must be paused
- `INVALID_REFERENCE` — Cannot resolve object reference
- `DEPTH_EXCEEDED` — Expansion depth exceeded limit

---

### memory_read

Read raw memory bytes from the debuggee process.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `address` | string | Yes | Memory address in hex (e.g., '0x00007FF8A1234560') or decimal |
| `size` | integer | No | Number of bytes to read (default: 256, max: 65536) |
| `format` | string | No | Output format: 'hex', 'hex_ascii' (default), 'raw' |

**Example:**
```json
{
  "address": "0x00007FF8A1234560",
  "size": 64,
  "format": "hex_ascii"
}
```

**Response:**
```json
{
  "success": true,
  "memory": {
    "address": "0x00007FF8A1234560",
    "requestedSize": 64,
    "actualSize": 64,
    "bytes": "48 65 6C 6C 6F 20 57 6F 72 6C 64 21 00 00 00 00\n00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
    "ascii": "Hello World!....\n................\n................\n................"
  }
}
```

**Response (partial read):**
```json
{
  "success": true,
  "memory": {
    "address": "0x00007FFFFFFFFFE0",
    "requestedSize": 64,
    "actualSize": 32,
    "bytes": "48 65 6C 6C 6F 00 00 00 00 00 00 00 00 00 00 00\n00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
    "ascii": "Hello...........\n................",
    "error": "Partial read: only 32 of 64 bytes readable"
  }
}
```

**Errors:**
- `NOT_PAUSED` — Process must be paused
- `INVALID_ADDRESS` — Memory address is not accessible
- `SIZE_EXCEEDED` — Requested size exceeds 64KB limit

---

### references_get

Analyze object references - find what objects a target references (outbound).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `object_ref` | string | Yes | Object reference (variable name or expression) |
| `direction` | string | No | Reference direction: 'outbound' (default), 'inbound', 'both'. Note: inbound not yet implemented |
| `max_results` | integer | No | Maximum references to return (default: 50, max: 100) |
| `include_arrays` | boolean | No | Include array element references (default: true) |
| `thread_id` | integer | No | Thread ID (default: current thread) |
| `frame_index` | integer | No | Frame index (0 = top of stack, default: 0) |

**Example:**
```json
{
  "object_ref": "orderManager",
  "direction": "outbound",
  "max_results": 50,
  "include_arrays": true
}
```

**Response:**
```json
{
  "success": true,
  "references": {
    "targetAddress": "0x00007FF8A1234560",
    "targetType": "MyApp.Services.OrderManager",
    "outbound": [
      {
        "sourceAddress": "0x00007FF8A1234560",
        "sourceType": "MyApp.Services.OrderManager",
        "targetAddress": "0x00007FF8A1235000",
        "targetType": "MyApp.Repositories.OrderRepository",
        "path": "_repository",
        "referenceType": "Field"
      },
      {
        "sourceAddress": "0x00007FF8A1234560",
        "sourceType": "MyApp.Services.OrderManager",
        "targetAddress": "0x00007FF8A1236000",
        "targetType": "Microsoft.Extensions.Logging.ILogger`1[OrderManager]",
        "path": "_logger",
        "referenceType": "Field"
      },
      {
        "sourceAddress": "0x00007FF8A1234560",
        "sourceType": "MyApp.Services.OrderManager",
        "targetAddress": "0x00007FF8A1237000",
        "targetType": "MyApp.Models.Order",
        "path": "_orders[0]",
        "referenceType": "ArrayElement"
      }
    ],
    "outboundCount": 3,
    "truncated": false
  }
}
```

**Response (with inbound request - not yet implemented):**
```json
{
  "success": true,
  "references": {
    "targetAddress": "0x00007FF8A1234560",
    "targetType": "MyApp.Services.OrderManager",
    "outbound": [...],
    "outboundCount": 3,
    "inbound": [],
    "inboundCount": 0,
    "inboundNote": "Inbound reference analysis is not yet implemented",
    "truncated": false
  }
}
```

**Errors:**
- `NOT_PAUSED` — Process must be paused
- `INVALID_REFERENCE` — Cannot resolve object reference

---

### layout_get

Get the memory layout of a type including field offsets, sizes, and padding.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `type_name` | string | Yes | Full type name (e.g., 'MyApp.Models.Customer') or object reference |
| `include_inherited` | boolean | No | Include inherited fields from base classes (default: true) |
| `include_padding` | boolean | No | Include padding analysis between fields (default: true) |
| `thread_id` | integer | No | Thread ID (default: current thread) |
| `frame_index` | integer | No | Frame index (0 = top of stack, default: 0) |

**Example:**
```json
{
  "type_name": "MyApp.Models.Customer",
  "include_inherited": true,
  "include_padding": true
}
```

**Response:**
```json
{
  "success": true,
  "layout": {
    "typeName": "MyApp.Models.Customer",
    "totalSize": 48,
    "headerSize": 16,
    "dataSize": 32,
    "baseType": "System.Object",
    "isValueType": false,
    "fields": [
      {
        "name": "Id",
        "typeName": "System.Int32",
        "offset": 16,
        "size": 4,
        "alignment": 4,
        "isReference": false,
        "declaringType": "MyApp.Models.Customer"
      },
      {
        "name": "IsActive",
        "typeName": "System.Boolean",
        "offset": 20,
        "size": 1,
        "alignment": 1,
        "isReference": false,
        "declaringType": "MyApp.Models.Customer"
      },
      {
        "name": "Name",
        "typeName": "System.String",
        "offset": 24,
        "size": 8,
        "alignment": 8,
        "isReference": true,
        "declaringType": "MyApp.Models.Customer"
      },
      {
        "name": "Email",
        "typeName": "System.String",
        "offset": 32,
        "size": 8,
        "alignment": 8,
        "isReference": true,
        "declaringType": "MyApp.Models.Customer"
      },
      {
        "name": "Orders",
        "typeName": "System.Collections.Generic.List`1[MyApp.Models.Order]",
        "offset": 40,
        "size": 8,
        "alignment": 8,
        "isReference": true,
        "declaringType": "MyApp.Models.Customer"
      }
    ],
    "padding": [
      {
        "offset": 21,
        "size": 3,
        "reason": "Alignment padding before Name"
      }
    ]
  }
}
```

**Response (value type):**
```json
{
  "success": true,
  "layout": {
    "typeName": "System.DateTime",
    "totalSize": 8,
    "headerSize": 0,
    "dataSize": 8,
    "isValueType": true,
    "fields": [
      {
        "name": "_dateData",
        "typeName": "System.UInt64",
        "offset": 0,
        "size": 8,
        "alignment": 8,
        "isReference": false,
        "declaringType": "System.DateTime"
      }
    ]
  }
}
```

**Errors:**
- `NOT_PAUSED` — Process must be paused
- `TYPE_NOT_FOUND` — Cannot find type with given name

---

## Module Inspection

Module inspection tools allow browsing loaded assemblies, exploring types, and searching across modules. These operations work with both **running** and **paused** sessions (they only read metadata).

### modules_list

List all loaded modules (assemblies) in the debugged process.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `include_system` | boolean | No | Include system assemblies like mscorlib (default: true) |
| `name_filter` | string | No | Filter modules by name pattern (supports * wildcard) |

**Example:**
```json
{
  "include_system": false,
  "name_filter": "MyApp*"
}
```

**Response:**
```json
{
  "success": true,
  "modules": [
    {
      "name": "MyApp",
      "path": "/app/MyApp.dll",
      "version": "1.0.0.0",
      "hasSymbols": true,
      "isOptimized": false,
      "isDynamic": false,
      "moduleId": "550e8400-e29b-41d4-a716-446655440000"
    },
    {
      "name": "MyApp.Core",
      "path": "/app/MyApp.Core.dll",
      "version": "1.0.0.0",
      "hasSymbols": true,
      "isOptimized": false,
      "isDynamic": false,
      "moduleId": "550e8400-e29b-41d4-a716-446655440001"
    }
  ],
  "count": 2
}
```

**Errors:**
- `NO_SESSION` — No active debug session

---

### types_get

Get types defined in a module, organized by namespace.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `module_name` | string | Yes | Module name to browse |
| `namespace_filter` | string | No | Filter by namespace pattern (supports * wildcard) |
| `kind` | string | No | Filter by type kind: class, interface, struct, enum, delegate |
| `visibility` | string | No | Filter by visibility: public, internal, private, protected |
| `max_results` | integer | No | Maximum types to return (default: 100) |
| `continuation_token` | string | No | Token for pagination |

**Example:**
```json
{
  "module_name": "MyApp",
  "namespace_filter": "MyApp.Services*",
  "kind": "class",
  "visibility": "public"
}
```

**Response:**
```json
{
  "success": true,
  "moduleName": "MyApp",
  "types": [
    {
      "fullName": "MyApp.Services.UserService",
      "name": "UserService",
      "namespace": "MyApp.Services",
      "kind": "class",
      "visibility": "public",
      "isAbstract": false,
      "genericParameters": [],
      "isGeneric": false,
      "baseType": "System.Object",
      "interfaces": ["MyApp.Services.IUserService"],
      "moduleName": "MyApp"
    },
    {
      "fullName": "MyApp.Services.OrderService",
      "name": "OrderService",
      "namespace": "MyApp.Services",
      "kind": "class",
      "visibility": "public",
      "isAbstract": false,
      "genericParameters": [],
      "isGeneric": false,
      "baseType": "System.Object",
      "interfaces": ["MyApp.Services.IOrderService"],
      "moduleName": "MyApp"
    }
  ],
  "namespaces": [
    {
      "name": "MyApp.Services",
      "typeCount": 2
    }
  ],
  "totalTypes": 2,
  "returnedTypes": 2,
  "hasMore": false,
  "continuationToken": null
}
```

**Errors:**
- `NO_SESSION` — No active debug session
- `MODULE_NOT_FOUND` — Module with given name not found

---

### members_get

Get members (methods, properties, fields, events) of a type.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `type_name` | string | Yes | Full type name to inspect (e.g., "MyApp.Models.Customer") |
| `module_name` | string | No | Module containing the type (helps resolve ambiguous types) |
| `include_inherited` | boolean | No | Include inherited members from base types (default: false) |
| `member_kinds` | string | No | Comma-separated list: methods, properties, fields, events |
| `visibility` | string | No | Filter by visibility: public, internal, private, protected |
| `include_static` | boolean | No | Include static members (default: true) |
| `include_instance` | boolean | No | Include instance members (default: true) |

**Example:**
```json
{
  "type_name": "MyApp.Models.Customer",
  "include_inherited": true,
  "member_kinds": "methods,properties",
  "visibility": "public"
}
```

**Response:**
```json
{
  "success": true,
  "typeName": "MyApp.Models.Customer",
  "moduleName": "MyApp",
  "methods": [
    {
      "name": "GetFullName",
      "signature": "string GetFullName()",
      "returnType": "string",
      "parameters": [],
      "visibility": "public",
      "isStatic": false,
      "isVirtual": false,
      "isAbstract": false,
      "isOverride": false,
      "genericParameters": null,
      "declaringType": "MyApp.Models.Customer"
    },
    {
      "name": "UpdateEmail",
      "signature": "void UpdateEmail(string email)",
      "returnType": "void",
      "parameters": [
        {
          "name": "email",
          "type": "string",
          "isOptional": false,
          "isOut": false,
          "isRef": false,
          "defaultValue": null
        }
      ],
      "visibility": "public",
      "isStatic": false,
      "isVirtual": false,
      "isAbstract": false,
      "isOverride": false,
      "genericParameters": null,
      "declaringType": "MyApp.Models.Customer"
    }
  ],
  "properties": [
    {
      "name": "Id",
      "type": "int",
      "visibility": "public",
      "isStatic": false,
      "hasGetter": true,
      "hasSetter": true,
      "declaringType": "MyApp.Models.Customer"
    },
    {
      "name": "Name",
      "type": "string",
      "visibility": "public",
      "isStatic": false,
      "hasGetter": true,
      "hasSetter": true,
      "declaringType": "MyApp.Models.Customer"
    }
  ],
  "fields": [],
  "events": []
}
```

**Errors:**
- `NO_SESSION` — No active debug session
- `TYPE_NOT_FOUND` — Type with given name not found

---

### modules_search

Search for types and methods across all loaded modules.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `pattern` | string | Yes | Search pattern (supports * wildcard: *prefix, suffix*, *contains*) |
| `search_type` | string | No | What to search: types, methods, or both (default: both) |
| `module_filter` | string | No | Limit search to specific module (supports * wildcard) |
| `case_sensitive` | boolean | No | Enable case-sensitive matching (default: false) |
| `max_results` | integer | No | Maximum results to return (max: 100, default: 50) |

**Example:**
```json
{
  "pattern": "*Customer*",
  "search_type": "both",
  "module_filter": "MyApp*",
  "case_sensitive": false,
  "max_results": 50
}
```

**Response:**
```json
{
  "success": true,
  "query": "*Customer*",
  "searchType": "both",
  "types": [
    {
      "fullName": "MyApp.Models.Customer",
      "name": "Customer",
      "namespace": "MyApp.Models",
      "kind": "class",
      "visibility": "public",
      "moduleName": "MyApp"
    },
    {
      "fullName": "MyApp.Services.CustomerService",
      "name": "CustomerService",
      "namespace": "MyApp.Services",
      "kind": "class",
      "visibility": "public",
      "moduleName": "MyApp"
    }
  ],
  "methods": [
    {
      "declaringType": "MyApp.Services.CustomerService",
      "moduleName": "MyApp",
      "matchReason": "name",
      "method": {
        "name": "GetCustomer",
        "signature": "Customer GetCustomer(int id)",
        "returnType": "Customer",
        "visibility": "public",
        "isStatic": false
      }
    },
    {
      "declaringType": "MyApp.Services.CustomerService",
      "moduleName": "MyApp",
      "matchReason": "name",
      "method": {
        "name": "UpdateCustomer",
        "signature": "void UpdateCustomer(Customer customer)",
        "returnType": "void",
        "visibility": "public",
        "isStatic": false
      }
    }
  ],
  "totalMatches": 4,
  "returnedMatches": 4,
  "truncated": false,
  "continuationToken": null
}
```

**Response (truncated results):**
```json
{
  "success": true,
  "query": "*String*",
  "searchType": "types",
  "types": [
    {
      "fullName": "System.String",
      "name": "String",
      "namespace": "System",
      "kind": "class",
      "visibility": "public",
      "moduleName": "System.Private.CoreLib"
    }
  ],
  "methods": [],
  "totalMatches": 150,
  "returnedMatches": 50,
  "truncated": true,
  "continuationToken": "next-page-token"
}
```

**Errors:**
- `NO_SESSION` — No active debug session
- `INVALID_PATTERN` — Search pattern cannot be empty
- `SEARCH_FAILED` — Search operation failed

---

## Error Responses

All tools may return errors in this format:

```json
{
  "success": false,
  "error": {
    "code": "NO_SESSION",
    "message": "No active debug session"
  }
}
```

**Common error codes:**

| Code | Description |
|------|-------------|
| `NO_SESSION` | No active debugging session |
| `NOT_PAUSED` | Process must be paused for this operation |
| `INVALID_THREAD` | Thread ID not found |
| `INVALID_FRAME` | Frame index out of range |
| `BREAKPOINT_NOT_FOUND` | Breakpoint ID not found |
| `EVAL_FAILED` | Expression evaluation error |
| `TIMEOUT` | Operation timed out |
| `PROCESS_NOT_FOUND` | Target process not found |
| `NOT_DOTNET_PROCESS` | Process is not a .NET application |
| `ATTACH_FAILED` | ICorDebug attach failed |
| `STACKTRACE_FAILED` | Stack trace retrieval failed |
| `VARIABLES_FAILED` | Variable inspection failed |
| `INVALID_PARAMETER` | Invalid parameter value |
| `INVALID_CONDITION` | Condition expression has syntax error |
| `INVALID_REFERENCE` | Cannot resolve object reference |
| `NULL_REFERENCE` | Object reference is null |
| `INVALID_ADDRESS` | Memory address is not accessible |
| `MEMORY_READ_FAILED` | Failed to read memory at address |
| `SIZE_EXCEEDED` | Requested size exceeds limit |
| `DEPTH_EXCEEDED` | Object inspection depth exceeded limit |
| `TYPE_NOT_FOUND` | Cannot find type with given name |
| `MODULE_NOT_FOUND` | Module with given name not found |
| `INVALID_PATTERN` | Search pattern is invalid or empty |
| `SEARCH_FAILED` | Module search operation failed |
