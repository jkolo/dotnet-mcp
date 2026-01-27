# Data Model: Fix Debugger Bugs

**Feature Branch**: `006-fix-debugger-bugs`
**Date**: 2026-01-26

## Overview

This feature involves bug fixes to existing functionality. No new data types are introduced. This document describes the affected types and their relationships.

## Affected Types

### ProcessDebugger (Service)

**Location**: `DotnetMcp/Services/ProcessDebugger.cs`

The core debugging service that manages ICorDebug interactions.

#### Key Fields Affected

| Field | Type | Purpose | Bug Relevance |
|-------|------|---------|---------------|
| `_corDebug` | `CorDebug?` | ICorDebug interface handle | Bug #3 - not properly released on disconnect |
| `_process` | `CorDebugProcess?` | Attached process handle | Bug #3 - correctly set to null |
| `_dbgShim` | `DbgShim?` | DbgShim native interop | Used for re-initialization |

#### Methods Requiring Changes

| Method | Lines | Bug | Change Required |
|--------|-------|-----|-----------------|
| `DetachAsync` | 242-279 | #3 | Add `_corDebug.Terminate()` and null assignment |
| `ResolveExpressionToValue` | 3074-3106 | #1 | Add property getter fallback |
| `FindPropertyGetter` | 2473-2527 | #2 | Add base type traversal |
| `TryGetFieldValue` | 2946-2999 | #1 | Consider backing field lookup |

### Existing Types Used (No Changes)

#### ObjectInspection (Record)

**Location**: `DotnetMcp/Models/ObjectInspection.cs`

```csharp
public record ObjectInspection
{
    public string Address { get; init; }
    public string TypeName { get; init; }
    public int Size { get; init; }
    public List<FieldDetail> Fields { get; init; }
    public bool IsNull { get; init; }
    public bool HasCircularRef { get; init; }
}
```

**Bug #1 Impact**: No schema changes. The existing structure supports nested inspection results.

#### EvaluationResult (Record)

**Location**: `DotnetMcp/Models/EvaluationResult.cs`

```csharp
public record EvaluationResult(
    bool Success,
    string? Value = null,
    string? Type = null,
    bool HasChildren = false,
    EvaluationError? Error = null);

public record EvaluationError(
    string Code,
    string Message,
    int? Position = null,
    string? ExceptionType = null);
```

**Bug #2 Impact**: No schema changes. Error codes remain the same.

## Type Relationships

```
┌─────────────────────┐
│  ProcessDebugger    │
│  (IDisposable)      │
├─────────────────────┤
│ - _corDebug         │──────┐
│ - _process          │      │
│ - _dbgShim          │      │
├─────────────────────┤      │
│ + DetachAsync()     │      │     ┌─────────────────┐
│ + InspectObjectAsync│──────┼────►│ ObjectInspection│
│ + EvaluateAsync()   │──────┤     └─────────────────┘
└─────────────────────┘      │
                             │     ┌──────────────────┐
                             └────►│ EvaluationResult │
                                   └──────────────────┘

Internal Resolution Chain:
┌────────────────────────────┐
│ ResolveExpressionToValue   │
├────────────────────────────┤
│ - TryGetThisForEval()      │
│ - TryGetLocalOrArgument()  │
│ - TryGetFieldValue()       │──► BUG #1: Missing property getter fallback
└────────────────────────────┘

┌────────────────────────────┐
│ TryResolvePropertyPathAsync│
├────────────────────────────┤
│ - TryGetFieldValue()       │
│ - FindPropertyGetter()     │──► BUG #2: Missing base type traversal
│ - CallFunctionAsync()      │
└────────────────────────────┘
```

## New Helper Method (Proposed)

To unify field/property access across Bug #1 and Bug #2:

```csharp
/// <summary>
/// Resolves a member (field or property) value from an object.
/// First tries field access, then backing field, then property getter.
/// </summary>
private async Task<CorDebugValue?> TryGetMemberValueAsync(
    CorDebugValue parentValue,
    string memberName,
    CorDebugThread? thread = null,
    int timeoutMs = 5000,
    CancellationToken cancellationToken = default)
{
    // 1. Try direct field access
    var fieldValue = TryGetFieldValue(parentValue, memberName);
    if (fieldValue != null) return fieldValue;

    // 2. Try auto-property backing field (<Name>k__BackingField)
    var backingFieldName = $"<{memberName}>k__BackingField";
    fieldValue = TryGetFieldValue(parentValue, backingFieldName);
    if (fieldValue != null) return fieldValue;

    // 3. Try property getter (requires thread for ICorDebugEval)
    if (thread != null)
    {
        var getter = FindPropertyGetter(parentValue, memberName);
        if (getter != null)
        {
            var result = await CallFunctionAsync(thread, getter, parentValue, null, timeoutMs, cancellationToken);
            if (result.Success) return result.Value;
        }
    }

    return null;
}
```

## State Transitions

### Bug #3: Session State Machine

```
Current (Buggy):                    Fixed:

    ┌───────────┐                       ┌───────────┐
    │Disconnected│                      │Disconnected│
    └─────┬─────┘                       └─────┬─────┘
          │ attach                            │ attach
          ▼                                   ▼
    ┌───────────┐                       ┌───────────┐
    │  Running  │                       │  Running  │
    └─────┬─────┘                       └─────┬─────┘
          │ disconnect                        │ disconnect
          ▼                                   ▼
    ┌───────────┐                       ┌───────────┐
    │Disconnected│                      │Disconnected│
    │ _corDebug  │ ◄─ STALE!            │ _corDebug  │ = null ◄─ FIXED!
    │  != null   │                      │             │
    └─────┬─────┘                       └─────┬─────┘
          │ attach                            │ attach
          ▼                                   ▼
    ┌───────────┐                       ┌───────────┐
    │  ERROR!   │                       │  Running  │ ◄─ SUCCESS!
    └───────────┘                       └───────────┘
```

## Validation Rules

No new validation rules. Existing error handling applies:

- Invalid object reference → `INVALID_REFERENCE` error
- Expression syntax error → `syntax_error` error
- Attach failure → `ATTACH_FAILED` error
- Process not paused → `InvalidOperationException`

## Migration

No data migration required. These are in-memory runtime fixes with no persisted state.
