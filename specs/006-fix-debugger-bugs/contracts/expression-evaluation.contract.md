# Test Contract: Expression Evaluation Member Access (Bug #2)

**Priority**: P3
**Feature**: Fix Debugger Bugs
**Related Requirement**: FR-009, FR-010, FR-011

## Contract Overview

Verify that `evaluate` tool supports member access expressions including properties inherited from base types.

## Preconditions

- Debugger is attached to `DebugTestApp`
- Process is paused at a breakpoint
- Test object hierarchy with inheritance:
  ```csharp
  class BaseEntity {
      public int Id { get; }        // Property on base
      public string CreatedBy { get; }
  }
  class Person : BaseEntity {
      public string Name { get; }
      public Address HomeAddress { get; }
  }
  ```

## Test Scenarios

### T001: Direct Property Access

**Given** local variable `_currentUser` of type `Person`
**When** I evaluate `_currentUser.Name`
**Then** I get the property value

```json
{
  "tool": "evaluate",
  "input": {
    "expression": "_currentUser.Name"
  }
}
// Expected:
{
  "success": true,
  "value": "\"John Doe\"",
  "type": "System.String"
}
```

### T002: This Keyword Access

**Given** paused in instance method
**When** I evaluate `this._currentUser.Name`
**Then** I get the property value

```json
{
  "tool": "evaluate",
  "input": { "expression": "this._currentUser.Name" }
}
// Expected:
{
  "success": true,
  "value": "\"John Doe\"",
  "type": "System.String"
}
```

### T003: Base Type Property Access

**Given** `Person` inherits `Id` from `BaseEntity`
**When** I evaluate `_currentUser.Id`
**Then** I get the inherited property value

```json
{
  "tool": "evaluate",
  "input": { "expression": "_currentUser.Id" }
}
// Expected:
{
  "success": true,
  "value": "42",
  "type": "System.Int32"
}
```

### T004: Nested Property Chain

**Given** nested object structure
**When** I evaluate `this._currentUser.HomeAddress.City`
**Then** I get the deeply nested value

```json
{
  "tool": "evaluate",
  "input": { "expression": "this._currentUser.HomeAddress.City" }
}
// Expected:
{
  "success": true,
  "value": "\"Seattle\"",
  "type": "System.String"
}
```

### T005: Five Level Chain

**Given** 5-level nested structure
**When** I evaluate `a.b.c.d.e`
**Then** evaluation completes successfully

## Error Scenarios

### E001: Non-existent Member

**Given** valid object
**When** I evaluate `_currentUser.InvalidProperty`
**Then** I get member not found error

```json
{
  "tool": "evaluate",
  "input": { "expression": "_currentUser.InvalidProperty" }
}
// Expected:
{
  "success": false,
  "error": {
    "code": "syntax_error",
    "message": "Member 'InvalidProperty' not found on type 'Person'"
  }
}
```

### E002: Null Object Access

**Given** `_currentUser` is null
**When** I evaluate `_currentUser.Name`
**Then** I get null reference error

```json
{
  "tool": "evaluate",
  "input": { "expression": "_currentUser.Name" }
}
// Expected:
{
  "success": false,
  "error": {
    "code": "eval_exception",
    "message": "Cannot access 'Name': '_currentUser' is null"
  }
}
```

### E003: Property Getter Throws

**Given** property getter throws exception
**When** I evaluate `_object.ThrowingProperty`
**Then** I get error with exception details

```json
{
  "tool": "evaluate",
  "input": { "expression": "_object.ThrowingProperty" }
}
// Expected:
{
  "success": false,
  "error": {
    "code": "eval_exception",
    "message": "Property getter threw exception: <message>",
    "exception_type": "System.InvalidOperationException"
  }
}
```

## Acceptance Criteria

- [ ] T001: Direct property access works
- [ ] T002: `this` keyword with property works
- [ ] T003: Base type property access works (KEY FIX)
- [ ] T004: Nested property chain works
- [ ] T005: 5-level chain evaluates successfully
- [ ] E001: Non-existent member shows clear error
- [ ] E002: Null access shows clear error
- [ ] E003: Throwing property shows exception details

## Implementation Notes

Modify `FindPropertyGetter` to traverse base types:

```csharp
private CorDebugFunction? FindPropertyGetter(CorDebugValue objectValue, string propertyName)
{
    // ... existing setup code ...

    var currentClass = objValue.Class;
    while (currentClass != null)
    {
        var module = currentClass.Module;
        var metaImport = module.GetMetaDataInterface<MetaDataImport>();

        // Search for get_PropertyName in current type
        var methods = metaImport.EnumMethods((int)currentClass.Token);
        foreach (var methodToken in methods)
        {
            var props = metaImport.GetMethodProps(methodToken);
            if (props.szMethod == $"get_{propertyName}")
            {
                return module.GetFunctionFromToken((uint)methodToken);
            }
        }

        // Move to base type
        var typeProps = metaImport.GetTypeDefProps((int)currentClass.Token);
        if (typeProps.tkExtends == 0) break;

        currentClass = module.GetClassFromToken((uint)typeProps.tkExtends);
    }

    return null;
}
```
