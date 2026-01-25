# Quickstart: Inspection Operations

**Feature**: 003-inspection-ops
**Prerequisites**: Active debug session (001), paused state via breakpoint or pause (002)

## Typical Debugging Workflow

```
1. Attach to process        → debug_attach
2. Set breakpoint           → breakpoint_set
3. Continue execution       → debug_continue
4. Wait for breakpoint hit  → breakpoint_wait
5. List threads             → threads_list
6. Get stack trace          → stacktrace_get
7. Inspect variables        → variables_get
8. Evaluate expressions     → evaluate
9. Continue or step         → debug_continue / debug_step
```

---

## Example: Debugging a NullReferenceException

### Step 1: List Threads

After hitting a breakpoint:

```json
// Request
{ "tool": "threads_list" }

// Response
{
  "threads": [
    {
      "id": 1,
      "name": "Main Thread",
      "state": "stopped",
      "is_current": true,
      "location": {
        "file": "/app/Services/UserService.cs",
        "line": 42,
        "function": "GetUser"
      }
    },
    {
      "id": 5,
      "name": ".NET ThreadPool Worker",
      "state": "waiting",
      "is_current": false
    }
  ]
}
```

### Step 2: Get Stack Trace

```json
// Request
{ "tool": "stacktrace_get", "thread_id": 1, "max_frames": 10 }

// Response
{
  "thread_id": 1,
  "total_frames": 15,
  "frames": [
    {
      "index": 0,
      "function": "MyApp.Services.UserService.GetUser",
      "module": "MyApp.dll",
      "location": {
        "file": "/app/Services/UserService.cs",
        "line": 42
      },
      "is_external": false,
      "arguments": [
        {
          "name": "userId",
          "type": "System.String",
          "value": "\"\"",
          "scope": "argument",
          "has_children": false
        }
      ]
    },
    {
      "index": 1,
      "function": "MyApp.Controllers.UserController.Get",
      "module": "MyApp.dll",
      "location": {
        "file": "/app/Controllers/UserController.cs",
        "line": 28
      },
      "is_external": false,
      "arguments": []
    }
  ]
}
```

### Step 3: Inspect Local Variables

```json
// Request
{ "tool": "variables_get", "frame_index": 0, "scope": "all" }

// Response
{
  "variables": [
    {
      "name": "this",
      "type": "MyApp.Services.UserService",
      "value": "{UserService}",
      "scope": "this",
      "has_children": true,
      "children_count": 3
    },
    {
      "name": "userId",
      "type": "System.String",
      "value": "\"\"",
      "scope": "argument",
      "has_children": false
    },
    {
      "name": "user",
      "type": "MyApp.Models.User",
      "value": "null",
      "scope": "local",
      "has_children": false
    }
  ]
}
```

**Finding**: The `userId` is empty string, and `user` is null. This explains the NullReferenceException!

### Step 4: Expand Object to See Fields

```json
// Request
{ "tool": "variables_get", "frame_index": 0, "expand": "this" }

// Response
{
  "variables": [
    {
      "name": "_repository",
      "type": "MyApp.Data.UserRepository",
      "value": "{UserRepository}",
      "scope": "field",
      "has_children": true,
      "path": "this._repository"
    },
    {
      "name": "_logger",
      "type": "Microsoft.Extensions.Logging.ILogger`1",
      "value": "{Logger}",
      "scope": "field",
      "has_children": true,
      "path": "this._logger"
    },
    {
      "name": "_cache",
      "type": "System.Collections.Generic.Dictionary`2",
      "value": "Count = 5",
      "scope": "field",
      "has_children": true,
      "children_count": 5,
      "path": "this._cache"
    }
  ]
}
```

### Step 5: Evaluate Expression

```json
// Request
{ "tool": "evaluate", "expression": "userId.Length" }

// Response
{
  "success": true,
  "value": "0",
  "type": "System.Int32",
  "has_children": false
}
```

```json
// Request - check what calling code passed
{ "tool": "evaluate", "expression": "string.IsNullOrEmpty(userId)" }

// Response
{
  "success": true,
  "value": "true",
  "type": "System.Boolean",
  "has_children": false
}
```

---

## Example: Pause a Running Process

When you need to interrupt execution without a breakpoint:

```json
// Request
{ "tool": "debug_pause" }

// Response
{
  "success": true,
  "state": "paused",
  "threads": [
    {
      "id": 1,
      "location": {
        "function": "System.Threading.Thread.Sleep",
        "file": null,
        "line": null
      }
    },
    {
      "id": 5,
      "location": {
        "function": "MyApp.Services.BackgroundWorker.ProcessQueue",
        "file": "/app/Services/BackgroundWorker.cs",
        "line": 87
      }
    }
  ]
}
```

---

## Error Handling Examples

### Not Attached

```json
// Request
{ "tool": "threads_list" }

// Response
{
  "success": false,
  "error": {
    "code": "NO_SESSION",
    "message": "No active debug session"
  }
}
```

### Not Paused

```json
// Request
{ "tool": "variables_get" }

// Response
{
  "success": false,
  "error": {
    "code": "NOT_PAUSED",
    "message": "Cannot get variables: process is not paused (current state: running)"
  }
}
```

### Invalid Thread

```json
// Request
{ "tool": "stacktrace_get", "thread_id": 999 }

// Response
{
  "success": false,
  "error": {
    "code": "INVALID_THREAD",
    "message": "Thread 999 not found",
    "details": { "thread_id": 999 }
  }
}
```

### Evaluation Timeout

```json
// Request
{ "tool": "evaluate", "expression": "SlowMethod()", "timeout_ms": 1000 }

// Response
{
  "success": false,
  "error": {
    "code": "EVAL_TIMEOUT",
    "message": "Expression evaluation timed out after 1000ms"
  }
}
```

### Variable Optimized Away

```json
// Request
{ "tool": "variables_get", "frame_index": 0 }

// Response
{
  "variables": [
    {
      "name": "result",
      "type": "System.Int32",
      "value": "<unavailable>",
      "scope": "local",
      "has_children": false
    }
  ]
}
```

---

## Best Practices

1. **Always check thread state** before inspection - use `threads_list` first
2. **Use pagination** for deep stacks - set `max_frames` to avoid large responses
3. **Expand objects lazily** - only request children when needed
4. **Set evaluation timeouts** - prevent hanging on infinite loops
5. **Handle optimized variables** - release builds may have unavailable variables
