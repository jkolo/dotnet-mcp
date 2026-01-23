# Research: Inspection Operations

**Feature**: 003-inspection-ops
**Date**: 2026-01-23
**Purpose**: Document ICorDebug APIs for thread, stack, variable, and expression inspection

## Research Areas

1. Thread enumeration and state inspection
2. Stack frame traversal
3. Variable/value inspection
4. Expression evaluation
5. Process pause mechanism

---

## 1. Thread Enumeration

### Decision: Use ICorDebugProcess.EnumerateThreads

**Rationale**: Direct ICorDebug API provides access to all managed threads with their states.

**Key APIs (via ClrDebug)**:

```csharp
// Get all threads
CorDebugProcess.Threads → IEnumerable<CorDebugThread>

// Thread properties
CorDebugThread.Id → int (OS thread ID)
CorDebugThread.UserState → CorDebugUserState (Running, Suspended, etc.)
CorDebugThread.CurrentException → CorDebugValue (if exception active)
CorDebugThread.ActiveChain → CorDebugChain (top of call stack)
```

**Thread State Mapping**:

| ICorDebug State | Our ThreadState |
|-----------------|-----------------|
| USER_STOP_REQUESTED | Stopped |
| USER_SUSPENDED | Stopped |
| USER_BACKGROUND | Running |
| USER_UNSTARTED | NotStarted |
| USER_STOPPED | Stopped |
| USER_WAIT_SLEEP_JOIN | Waiting |
| USER_UNSAFE_POINT | Running |
| (default) | Running |

**Thread Name Resolution**:
- Thread name is not directly available in ICorDebug
- Must use ICorDebugThread.GetObject() to get Thread object, then read Name property
- Alternative: Use System.Diagnostics.Process to get thread names (less accurate)
- Decision: Read Thread.Name via ICorDebugValue if available, fallback to "Thread {id}"

**Alternatives Considered**:
- System.Diagnostics.Process.Threads: Only provides OS-level info, not .NET state
- ClrMD ThreadPool enumeration: Different API surface, better for post-mortem

---

## 2. Stack Frame Traversal

### Decision: Use ICorDebugThread chains and frames

**Rationale**: ICorDebug chain/frame model provides full managed call stack with IL offsets.

**Key APIs (via ClrDebug)**:

```csharp
// Get call stack
CorDebugThread.ActiveChain → CorDebugChain (current execution context)
CorDebugChain.EnumerateFrames() → IEnumerable<CorDebugFrame>

// Chain types (managed vs native)
CorDebugChain.IsManaged → bool
CorDebugChain.Reason → CorDebugChainReason (CHAIN_ENTER_MANAGED, CHAIN_ENTER_UNMANAGED, etc.)

// Frame types
CorDebugFrame → base class
CorDebugILFrame : CorDebugFrame → managed frame with IL info
CorDebugNativeFrame : CorDebugFrame → native frame
CorDebugInternalFrame : CorDebugFrame → runtime internal frame

// Frame properties
CorDebugILFrame.Function → CorDebugFunction (method info)
CorDebugILFrame.GetIP(out int offset, out CorDebugMappingResult) → IL offset
CorDebugFunction.Token → int (metadata token)
CorDebugFunction.Module → CorDebugModule
```

**Source Location Resolution**:
- Use existing PdbSymbolReader from 002-breakpoint-ops
- Map IL offset to source line via sequence points
- Handle frames without symbols (mark as is_external: true)

**Pagination Strategy**:
- ICorDebug returns all frames; pagination is done in our code
- Store frame enumeration, slice based on start_frame/max_frames
- Return total_frames count for UI pagination

**Alternatives Considered**:
- Walking chains manually vs enumerating frames: Frames are cleaner, chains useful for native transitions
- Single frame list vs chain-aware: Start with single list, add chain info if needed

---

## 3. Variable Inspection

### Decision: Use ICorDebugILFrame locals/arguments + ICorDebugValue hierarchy

**Rationale**: ICorDebug provides direct access to local variables and arguments per frame.

**Key APIs (via ClrDebug)**:

```csharp
// Get locals and arguments
CorDebugILFrame.EnumerateLocalVariables() → IEnumerable<CorDebugValue>
CorDebugILFrame.EnumerateArguments() → IEnumerable<CorDebugValue>
CorDebugILFrame.GetArgument(int index) → CorDebugValue
CorDebugILFrame.GetLocalVariable(int index) → CorDebugValue

// Value type hierarchy
CorDebugValue → base (has Address, Type)
CorDebugReferenceValue : CorDebugValue → reference type (IsNull, Dereference)
CorDebugObjectValue : CorDebugValue → object instance (GetFieldValue)
CorDebugBoxValue : CorDebugValue → boxed value type (GetObject)
CorDebugArrayValue : CorDebugValue → array (GetDimensions, GetElement)
CorDebugStringValue : CorDebugValue → string (GetString)
CorDebugGenericValue : CorDebugValue → primitive (GetValue)

// Type information
CorDebugValue.ExactType → CorDebugType
CorDebugType.Type → CorElementType (ELEMENT_TYPE_CLASS, ELEMENT_TYPE_I4, etc.)
CorDebugClass.Token → metadata token for type lookup
```

**Variable Name Resolution**:
- Local variable names: Use PDB local scope info (via System.Reflection.Metadata)
- Argument names: Use method parameter metadata
- Field names: Use type metadata (GetTypeDefProps, GetFieldProps)

**Value Formatting Strategy**:

| Type | Display Format |
|------|----------------|
| null | "null" |
| string | "\"value\"" (truncated at 100 chars) |
| int/long/etc | numeric value |
| bool | "true" or "false" |
| array | "Type[length]" with expandable elements |
| object | "{TypeName}" with expandable fields |
| enum | "EnumValue (numericValue)" |

**Expansion Model**:
- First call returns top-level variables with has_children flag
- Subsequent calls with expand path (e.g., "user.Address") return children
- Track expansion depth to prevent infinite recursion (max 10 levels)

**Circular Reference Detection**:
- Track visited object addresses during expansion
- If address seen before, return "{circular reference}" instead of expanding

**Alternatives Considered**:
- ICorDebugEval for all value access: Too slow, FuncEval overhead
- ClrMD value reading: Different API, better for post-mortem dumps
- Lazy vs eager loading: Lazy with expansion is more practical for large objects

---

## 4. Expression Evaluation

### Decision: Use ICorDebugEval with Roslyn compilation

**Rationale**: ICorDebugEval is the standard way to execute code in debuggee context.

**Key APIs (via ClrDebug)**:

```csharp
// Create evaluator
CorDebugThread.CreateEval() → CorDebugEval

// Function evaluation
CorDebugEval.CallFunction(CorDebugFunction func, CorDebugValue[] args)
CorDebugEval.NewObject(CorDebugFunction ctor, CorDebugValue[] args)
CorDebugEval.NewArray(CorDebugType elementType, int[] dimensions)

// Eval completion
CorDebugEval.Abort() → cancel long-running eval
CorDebugManagedCallback.EvalComplete → eval finished
CorDebugManagedCallback.EvalException → eval threw exception
CorDebugEval.Result → CorDebugValue (result or exception)
```

**Expression Parsing Strategy**:

For simple expressions (Phase 1):
1. Variable access: `userId` → look up in locals/args, return value
2. Property access: `user.Name` → find property getter, call via FuncEval
3. Method call: `GetName()` → find method, call via FuncEval
4. Field access: `user._name` → use GetFieldValue directly (no FuncEval needed)

For complex expressions (Future enhancement):
- Use Roslyn to compile expression into IL
- Load compiled method into debuggee via ICorDebugEval
- Execute and return result

**Timeout Handling**:
- ICorDebugEval has no built-in timeout
- Use CancellationToken with timer
- Call CorDebugEval.Abort() on timeout
- Return error with "evaluation timed out" message

**Side Effect Warning**:
- Cannot reliably detect if expression has side effects
- Document that evaluation may modify debuggee state
- Consider: Add `readonly` parameter to prevent method calls (only field/property reads)

**Alternatives Considered**:
- Full Roslyn compilation: Complex, adds large dependency, defer to future
- Simple tokenizer only: Too limited, can't handle method calls
- ClrMD evaluation: Not supported, ClrMD is read-only

---

## 5. Process Pause

### Decision: Use ICorDebugProcess.Stop

**Rationale**: Direct API to stop all threads synchronously.

**Key APIs (via ClrDebug)**:

```csharp
// Stop all threads
CorDebugProcess.Stop(uint timeout) → stops all threads
// timeout in milliseconds, 0 = infinite

// Continue after stop
CorDebugProcess.Continue(bool outOfBand)
```

**State Transition**:
- Running → call Stop() → callback receives ControlC event → Paused
- Already Paused → Stop() is no-op, return success
- Not Attached → return error

**Thread Selection After Pause**:
- After Stop(), all threads are paused
- Select "current thread" based on:
  1. Thread that was last active (if known)
  2. Main thread (lowest ID)
  3. First thread in enumeration

**Alternatives Considered**:
- ICorDebugController.Stop: Same as Process.Stop for our use case
- Sending Ctrl+C signal: Platform-dependent, not reliable

---

## Implementation Notes

### Shared Infrastructure

1. **PdbSymbolReader** (existing from 002):
   - Reuse for IL offset → source line mapping
   - Add method to get local variable names from PDB

2. **ProcessDebugger Extensions**:
   - Add GetThreads() method
   - Add GetStackTrace(threadId) method
   - Add GetVariables(threadId, frameIndex) method
   - Add Pause() method
   - Keep EvaluateExpression() in separate service (ExpressionEvaluator)

3. **Caching Strategy**:
   - Cache thread list per Stop() event (invalidate on Continue)
   - Cache stack frames per thread per Stop() event
   - Don't cache variable values (may change during debugging)

### Error Handling

| Scenario | Error Code | Message |
|----------|------------|---------|
| Not attached | not_attached | No debugging session active |
| Not paused (for stack/vars) | not_paused | Process must be paused for inspection |
| Invalid thread ID | invalid_thread | Thread {id} not found |
| Invalid frame index | invalid_frame | Frame index {n} out of range (0-{max}) |
| Variable unavailable | variable_unavailable | Variable '{name}' unavailable (optimized away) |
| Eval timeout | eval_timeout | Expression evaluation timed out after {n}ms |
| Eval exception | eval_exception | Expression threw {ExceptionType}: {message} |
| Syntax error | syntax_error | Invalid expression: {details} |

---

## References

- [ClrDebug NuGet Package](https://www.nuget.org/packages/ClrDebug)
- [ICorDebug Interface (MSDN)](https://docs.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/icordebug-interface)
- [Debugging Interfaces (MSDN)](https://docs.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/)
- Microsoft.Diagnostics.Runtime (ClrMD) for comparison patterns
