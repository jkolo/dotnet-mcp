# Research: Fix Debugger Bugs

**Feature Branch**: `006-fix-debugger-bugs`
**Research Date**: 2026-01-26

## Executive Summary

Three bugs in the .NET debugger MCP have been analyzed. All three have clear root causes in `ProcessDebugger.cs` with straightforward fixes that don't require architectural changes.

## Bug #1: Nested Property Access in `object_inspect`

### Symptoms
- `object_inspect` with reference `this._currentUser.HomeAddress` fails with "Invalid reference"
- Single-level access like `this._currentUser` works correctly
- Error: `INVALID_REFERENCE: could not resolve 'this._currentUser.HomeAddress'`

### Root Cause Analysis

**Location**: `ProcessDebugger.cs:3037-3067` (`InspectObjectAsync`)

The `InspectObjectAsync` method calls `ResolveExpressionToValue` which:

1. Splits expression by `.` → `["this", "_currentUser", "HomeAddress"]`
2. Resolves `this` correctly
3. For subsequent segments, calls `TryGetFieldValue` only

**The Bug**: `TryGetFieldValue` (lines 2946-2999) only searches for **fields**, not **properties**. In C#, properties like `HomeAddress` are methods (`get_HomeAddress`), not fields.

```csharp
// ProcessDebugger.cs:2973-2990 - Only searches fields!
var fields = metaImport.EnumFields((int)objClass.Token).ToList();
foreach (var fieldToken in fields)
{
    var fieldProps = metaImport.GetFieldProps(fieldToken);
    if (fieldProps.szField == fieldName)
    {
        return objValue.GetFieldValue(objClass.Raw, (int)fieldToken);
    }
}
// No property getter fallback!
```

**Contrast with EvaluateAsync**: The `TryResolvePropertyPathAsync` method (used by `evaluate`) correctly falls back to `FindPropertyGetter` when field lookup fails.

### Solution Approach

Modify `ResolveExpressionToValue` to:
1. First try `TryGetFieldValue` (current behavior)
2. If null, try to find property backing field (`<PropertyName>k__BackingField`)
3. If still null, invoke property getter via `FindPropertyGetter` + `CallFunctionAsync`

**Alternative**: Create a unified `TryGetMemberValue` that handles both fields and properties.

### Impact Assessment
- **Scope**: Internal change to `ProcessDebugger.cs`
- **Risk**: Medium - property getters may have side effects
- **Mitigation**: Document that property evaluation may execute code

---

## Bug #2: Expression Evaluation Member Access

### Symptoms
- `evaluate` with expression `_currentUser.HomeAddress` returns "Unrecognized expression"
- Error: `syntax_error: Unrecognized expression: _currentUser.HomeAddress`

### Root Cause Analysis

**Location**: `ProcessDebugger.cs:2749-2856` (`TryResolvePropertyPathAsync`)

The code DOES attempt property getter lookup via `FindPropertyGetter`, but the bug is in how `FindPropertyGetter` searches for methods:

```csharp
// ProcessDebugger.cs:2509-2523 - Only searches current type!
var methodEnum = metaImport.EnumMethods(typeToken);
foreach (var methodToken in methodEnum)
{
    var methodProps = metaImport.GetMethodProps(methodToken);
    if (methodProps.szMethod == getterName)
    {
        return module.GetFunctionFromToken((uint)methodToken);
    }
}
```

**The Bug**: `FindPropertyGetter` doesn't traverse base types. If `HomeAddress` is defined on a base class of `Person`, it won't be found.

### Additional Analysis

After further testing, the issue may also be that:
1. The first segment must be a local variable or `this` - underscored fields like `_currentUser` may not be recognized if they're instance fields of `this`
2. The expression should be `this._currentUser.HomeAddress` not just `_currentUser.HomeAddress`

### Solution Approach

1. Modify `FindPropertyGetter` to traverse base types using `GetTypeDefProps().tkExtends`
2. Consider supporting implicit `this` prefix for field access (e.g., `_field` → `this._field`)

### Impact Assessment
- **Scope**: Internal change to `ProcessDebugger.cs`
- **Risk**: Low - adding base type traversal is safe
- **Testing**: Need to verify with properties inherited from base classes

---

## Bug #3: Reattachment Failure After Disconnect

### Symptoms
- After `debug_disconnect`, subsequent `debug_attach` fails with `ERROR_INVALID_PARAMETER`
- Error: `ATTACH_FAILED: Error HRESULT ERROR_INVALID_PARAMETER has been returned from a call to a COM component`
- Workaround: Restart MCP server

### Root Cause Analysis

**Location**: `ProcessDebugger.cs:242-279` (`DetachAsync`) and `ProcessDebugger.cs:321-355` (`InitializeCorDebugForProcess`)

The issue is a resource lifecycle problem:

1. **DetachAsync** (lines 242-279):
```csharp
_process.Detach();
_process = null;
// Note: Don't call _corDebug.Terminate() here...
// _corDebug can be reused for subsequent attach/launch operations.
```

2. **InitializeCorDebugForProcess** (lines 321-355):
```csharp
if (_corDebug != null) return;  // BUG: Returns early with stale ICorDebug!
// ... creates new ICorDebug only if null
```

**The Bug**: The comment in `DetachAsync` claims `_corDebug` can be reused, but this is incorrect. After detaching, the `ICorDebug` instance is in a stale state. The early return in `InitializeCorDebugForProcess` prevents creating a fresh instance.

**Why It Happens**:
- ICorDebug interfaces are tied to specific process attachments
- After detach, the managed callback and internal state are invalid
- COM components remember the previous process state

### Solution Approach

**Option A (Recommended)**: Terminate and recreate ICorDebug on disconnect
```csharp
// In DetachAsync, after _process = null:
if (_corDebug != null)
{
    try { _corDebug.Terminate(); } catch { }
    _corDebug = null;
}
```

**Option B**: Allow `InitializeCorDebugForProcess` to recreate even when non-null
- Riskier, may leak resources if Terminate not called

### Impact Assessment
- **Scope**: `DetachAsync` method only
- **Risk**: Low - calling Terminate after detach is the expected lifecycle
- **Testing**: Critical - needs attach/detach/reattach cycle test

---

## Cross-Bug Analysis

### Shared Code Patterns

All three bugs involve the `ProcessDebugger.cs` service class and share common themes:

1. **Incomplete traversal**: Bug #1 doesn't try properties, Bug #2 doesn't traverse base types
2. **Resource lifecycle**: Bug #3 is a classic resource management issue
3. **Missing fallbacks**: The code often has the capability but doesn't use it in all paths

### Testing Strategy

| Bug | Test Type | Test Scenario |
|-----|-----------|---------------|
| #1 | Integration | Inspect `this._field.NestedProp` at breakpoint |
| #2 | Integration | Evaluate `_field.Property` expression |
| #3 | Integration | Attach → Disconnect → Attach (10x cycles) |

### Dependencies Between Fixes

- Bug #1 and #2 both need `TryGetMemberValue` or similar - could share implementation
- Bug #3 is independent and can be fixed first (highest priority)

---

## Recommended Implementation Order

1. **Bug #3 (Reattachment)** - P1, blocks all debugging workflows after first session
2. **Bug #1 (Nested Inspection)** - P2, most common user complaint
3. **Bug #2 (Expression Eval)** - P3, extends capabilities built in #1

## Open Questions

1. Should property evaluation in `object_inspect` be opt-in due to potential side effects?
2. Should we cache property values to avoid repeated getter invocations?
3. How deep should base type traversal go? (Current: none, Proposed: full hierarchy)

## References

- ICorDebug Documentation: https://docs.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/
- ClrDebug Source: https://github.com/lordmilko/ClrDebug
- BUGS.md: Original bug reports with reproduction steps
