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

## Error Responses

All tools may return errors in this format:

```json
{
  "error": true,
  "code": "not_attached",
  "message": "No debugging session active. Use debug_launch or debug_attach first."
}
```

**Common error codes:**

| Code | Description |
|------|-------------|
| `not_attached` | No active debugging session |
| `not_stopped` | Process must be stopped for this operation |
| `invalid_thread` | Thread ID not found |
| `invalid_frame` | Frame index out of range |
| `invalid_breakpoint` | Breakpoint ID not found |
| `evaluation_failed` | Expression evaluation error |
| `timeout` | Operation timed out |
| `process_exited` | Target process has terminated |
