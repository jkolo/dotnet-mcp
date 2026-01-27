# Test Contract: Nested Property Inspection (Bug #1)

**Priority**: P2
**Feature**: Fix Debugger Bugs
**Related Requirement**: FR-005, FR-006, FR-007, FR-008

## Contract Overview

Verify that `object_inspect` can resolve nested property paths using dot notation.

## Preconditions

- Debugger is attached to `DebugTestApp`
- Process is paused at a breakpoint
- Test object hierarchy available:
  ```csharp
  class Application {
      Person _currentUser;       // Field
      Settings _settings;        // Field
  }
  class Person {
      Address HomeAddress { get; } // Property
      string Name { get; }         // Property
  }
  class Address {
      string City { get; }
      string Street { get; }
  }
  ```

## Test Scenarios

### T001: Single Level Field Access

**Given** debugger is paused with `this` being an `Application` instance
**When** I inspect `this._currentUser`
**Then** I get the `Person` object details

```json
{
  "tool": "object_inspect",
  "input": {
    "object_ref": "this._currentUser",
    "depth": 1
  }
}
// Expected:
{
  "success": true,
  "type_name": "DebugTestApp.Person",
  "fields": [
    { "name": "<Name>k__BackingField", "type": "string", "value": "..." },
    { "name": "<HomeAddress>k__BackingField", "type": "DebugTestApp.Address", ... }
  ]
}
```

### T002: Two Level Property Access

**Given** debugger is paused with `this` being an `Application` instance
**When** I inspect `this._currentUser.HomeAddress`
**Then** I get the `Address` object details

```json
{
  "tool": "object_inspect",
  "input": {
    "object_ref": "this._currentUser.HomeAddress",
    "depth": 1
  }
}
// Expected:
{
  "success": true,
  "type_name": "DebugTestApp.Address",
  "fields": [
    { "name": "<City>k__BackingField", "type": "string", "value": "Seattle" },
    { "name": "<Street>k__BackingField", "type": "string", "value": "123 Main St" }
  ]
}
```

### T003: Three Level Property Access

**Given** object with 3-level nesting
**When** I inspect `this._currentUser.HomeAddress.City`
**Then** I get the string value

```json
{
  "tool": "object_inspect",
  "input": {
    "object_ref": "this._currentUser.HomeAddress.City"
  }
}
// Expected:
{
  "success": true,
  "type_name": "System.String",
  "value": "Seattle"
}
```

### T004: Mixed Fields and Properties

**Given** path contains both fields and properties
**When** I inspect `this._settings.DebugMode` (field → property)
**Then** resolution succeeds

### T005: Five Level Deep Access

**Given** deeply nested object structure
**When** I inspect a 5-level path `a.b.c.d.e`
**Then** resolution completes within 500ms

## Error Scenarios

### E001: Null Intermediate Value

**Given** `_currentUser` is null
**When** I inspect `this._currentUser.HomeAddress`
**Then** I get error indicating null at `_currentUser`

```json
{
  "tool": "object_inspect",
  "input": { "object_ref": "this._currentUser.HomeAddress" }
}
// Expected:
{
  "success": false,
  "error": {
    "code": "NULL_REFERENCE",
    "message": "Cannot access 'HomeAddress': 'this._currentUser' is null"
  }
}
```

### E002: Non-existent Member

**Given** valid object
**When** I inspect `this._currentUser.InvalidProperty`
**Then** I get member not found error

```json
{
  "tool": "object_inspect",
  "input": { "object_ref": "this._currentUser.InvalidProperty" }
}
// Expected:
{
  "success": false,
  "error": {
    "code": "INVALID_REFERENCE",
    "message": "Member 'InvalidProperty' not found on type 'Person'"
  }
}
```

### E003: Array Indexer (Out of Scope)

**Given** object with array
**When** I inspect `this._items[0].Name`
**Then** I get not supported error (documented limitation)

## Acceptance Criteria

- [ ] T001: Single level field access works
- [ ] T002: Two level property access works
- [ ] T003: Three level nested access works
- [ ] T004: Mixed field/property paths work
- [ ] T005: 5-level deep access completes < 500ms
- [ ] E001: Null intermediate shows clear error
- [ ] E002: Invalid member shows clear error
- [ ] E003: Array indexer shows not supported message

## Implementation Notes

Modify `ResolveExpressionToValue` to:
1. Try `TryGetFieldValue` first
2. Try backing field `<Name>k__BackingField`
3. Fall back to property getter via `FindPropertyGetter` + `CallFunctionAsync`
