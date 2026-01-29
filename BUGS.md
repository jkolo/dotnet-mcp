# Known Bugs

All bugs listed below have been **RESOLVED** as of 2026-01-26.

---

## 1. ~~`object_inspect` - nested property access not supported~~ ✅ FIXED

**Tool:** `object_inspect`

**Problem:** Chained property/field access doesn't work.

**Resolution:** Implemented nested property resolution in `ResolveExpressionToValue` method using `TryGetMemberValueAsync` helper. The method now traverses dot-notation paths like `this._currentUser.HomeAddress.City`.

**Tests:** `tests/DebugMcp.Tests/Integration/NestedInspectionTests.cs`
- `SingleLevelFieldAccess_ShouldSucceed`
- `TwoLevelPropertyAccess_ShouldReturnAddressObject`
- `ThreeLevelAccess_ShouldReturnStringValue`
- `NullIntermediate_ShouldReturnClearError`
- `InvalidMember_ShouldReturnMemberNotFound`

---

## 2. ~~`evaluate` - expression evaluation limited~~ ✅ FIXED

**Tool:** `evaluate`

**Problem:** Member access expressions are not recognized. Also, properties inherited from base types couldn't be resolved.

**Resolution:**
1. Added base type traversal in `TryGetFieldValue` and `FindPropertyGetter` methods using `GetTypeDefProps().ptkExtends` to get the base type token.
2. The traversal continues up the inheritance hierarchy until the member is found or `System.Object` is reached.

**Tests:** `tests/DebugMcp.Tests/Integration/BaseTypeExpressionTests.cs`
- `DirectPropertyAccess_ShouldReturnValue`
- `ThisKeywordAccess_ShouldReturnValue`
- `BaseTypePropertyAccess_ShouldReturnInheritedValue`
- `NestedPropertyChain_ShouldResolveAllLevels`
- `NonExistentMember_ShouldReturnError`

---

## 3. ~~`debug_attach` - reattachment fails without MCP restart~~ ✅ FIXED

**Tool:** `debug_attach`

**Problem:** After disconnecting from a debug session, subsequent attach attempts fail with `ERROR_INVALID_PARAMETER` until the MCP server is restarted.

**Resolution:** Added proper cleanup of ICorDebug instance in `DetachAsync`:
1. Call `_corDebug.Terminate()` to release native debugging resources
2. Set `_corDebug = null` to allow fresh initialization on next attach
3. Added try/catch to handle `CORDBG_E_ILLEGAL_SHUTDOWN_ORDER` gracefully

**Tests:** `tests/DebugMcp.Tests/Integration/ReattachmentTests.cs`
- `BasicReattachmentCycle_ShouldSucceed`
- `MultipleCycles_TenTimes_AllSucceed`
- `ReattachAfterTargetTerminates_ShouldSucceed`
- `ReattachToSameProcess_ShouldSucceed`

---

## Summary

| Bug | Status | Fix Location |
|-----|--------|--------------|
| Nested property access in `object_inspect` | ✅ FIXED | `ProcessDebugger.cs:ResolveExpressionToValue` |
| Expression evaluation limited | ✅ FIXED | `ProcessDebugger.cs:TryGetFieldValue, FindPropertyGetter` |
| Reattachment fails | ✅ FIXED | `ProcessDebugger.cs:DetachAsync` |

All 14 integration tests pass confirming the fixes.
