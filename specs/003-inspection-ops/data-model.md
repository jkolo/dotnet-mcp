# Data Model: Inspection Operations

**Feature**: 003-inspection-ops
**Date**: 2026-01-23
**Source**: spec.md Key Entities section

## Entities

### ThreadInfo

Represents a managed thread in the debuggee process.

```
ThreadInfo
├── Id: int                    # OS thread ID (from ICorDebugThread.Id)
├── Name: string?              # Thread name (from Thread.Name property, may be null)
├── State: ThreadState         # Current thread state
├── IsCurrent: bool            # True if this is the active/current thread
└── Location: SourceLocation?  # Current location if thread is stopped
```

**Relationships**:
- Has zero or one SourceLocation (only when State is Stopped)
- Referenced by StackFrame (stack belongs to a thread)

**Validation Rules**:
- Id must be positive integer
- Name may be null or empty (unnamed threads)
- IsCurrent: exactly one thread should be marked current when paused

---

### ThreadState (enum)

```
ThreadState
├── Running      # Thread is actively executing
├── Stopped      # Thread is paused (breakpoint, step, pause)
├── Waiting      # Thread is in wait/sleep/join state
├── NotStarted   # Thread created but not yet started
└── Terminated   # Thread has exited
```

**Mapping from ICorDebug**:
- USER_STOP_REQUESTED, USER_SUSPENDED, USER_STOPPED → Stopped
- USER_WAIT_SLEEP_JOIN → Waiting
- USER_UNSTARTED → NotStarted
- Default/USER_BACKGROUND → Running

---

### StackFrame

Represents a single frame in the call stack.

```
StackFrame
├── Index: int                 # Frame index (0 = top of stack)
├── Function: string           # Full method name (Namespace.Class.Method)
├── Module: string             # Assembly name (e.g., "MyApp.dll")
├── Location: SourceLocation?  # Source file/line if symbols available
├── IsExternal: bool           # True if no source available (framework code)
└── Arguments: Variable[]      # Method arguments with values
```

**Relationships**:
- Belongs to a ThreadInfo (one thread has many frames)
- Has zero or one SourceLocation
- Has zero or more Arguments (Variable instances)

**Validation Rules**:
- Index must be non-negative, sequential from 0
- Function must not be empty
- Module must not be empty
- IsExternal should be true when Location is null

---

### Variable

Represents a named value in scope (local, argument, field, or property).

```
Variable
├── Name: string               # Variable name
├── Type: string               # Full type name (e.g., "System.String")
├── Value: string              # Display value (formatted for readability)
├── Scope: VariableScope       # Where this variable comes from
├── HasChildren: bool          # True if value can be expanded
├── ChildrenCount: int?        # Number of children if known
└── Path: string?              # Expansion path (e.g., "user.Address")
```

**Relationships**:
- May have child Variables (for object fields, array elements)
- Parent path tracked for expansion requests

**Validation Rules**:
- Name must not be empty
- Type must not be empty
- Value always has a display representation (even for null: "null")
- ChildrenCount only set when HasChildren is true

---

### VariableScope (enum)

```
VariableScope
├── Local        # Local variable declared in method
├── Argument     # Method parameter
├── This         # The 'this' reference (instance methods)
├── Field        # Instance or static field
├── Property     # Property value (requires getter call)
└── Element      # Array/collection element
```

---

### EvaluationResult

Result of expression evaluation.

```
EvaluationResult
├── Success: bool              # True if evaluation succeeded
├── Value: string?             # Result value as display string
├── Type: string?              # Result type name
├── HasChildren: bool          # True if result can be expanded
├── Error: EvaluationError?    # Error details if Success is false
```

**Relationships**:
- May have EvaluationError when Success is false

---

### EvaluationError

Error details for failed evaluation.

```
EvaluationError
├── Code: string               # Error code (eval_timeout, eval_exception, syntax_error)
├── Message: string            # Human-readable error message
├── ExceptionType: string?     # Exception type if eval threw
└── Position: int?             # Character position for syntax errors
```

---

## Existing Entities (from previous features)

### SourceLocation (from 001-debug-session)

```
SourceLocation
├── File: string               # Absolute file path
├── Line: int                  # 1-based line number
├── Column: int?               # 1-based column (optional)
└── Function: string?          # Function name (optional)
```

Used by: ThreadInfo.Location, StackFrame.Location

---

## Response Models

### ThreadsListResponse

```json
{
  "threads": [
    {
      "id": 1,
      "name": "Main Thread",
      "state": "stopped",
      "is_current": true,
      "location": {
        "file": "/app/Program.cs",
        "line": 42,
        "function": "Main"
      }
    },
    {
      "id": 5,
      "name": ".NET ThreadPool Worker",
      "state": "waiting",
      "is_current": false,
      "location": null
    }
  ]
}
```

### StacktraceGetResponse

```json
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
        "line": 42,
        "column": 12
      },
      "is_external": false,
      "arguments": [
        {
          "name": "userId",
          "type": "System.String",
          "value": "\"abc123\"",
          "scope": "argument",
          "has_children": false
        }
      ]
    },
    {
      "index": 1,
      "function": "Microsoft.AspNetCore.Mvc.Internal.ActionInvoker.InvokeAsync",
      "module": "Microsoft.AspNetCore.Mvc.Core.dll",
      "location": null,
      "is_external": true,
      "arguments": []
    }
  ]
}
```

### VariablesGetResponse

```json
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
      "value": "\"abc123\"",
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

### EvaluateResponse

```json
{
  "success": true,
  "value": "\"John Doe\"",
  "type": "System.String",
  "has_children": false
}
```

Or on error:

```json
{
  "success": false,
  "error": {
    "code": "eval_exception",
    "message": "Object reference not set to an instance of an object",
    "exception_type": "System.NullReferenceException"
  }
}
```

### DebugPauseResponse

```json
{
  "success": true,
  "state": "paused",
  "threads": [
    {
      "id": 1,
      "location": {
        "function": "MyApp.Program.Main",
        "file": "/app/Program.cs",
        "line": 15
      }
    }
  ]
}
```
