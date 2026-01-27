# Feature Specification: Fix Debugger Bugs

**Feature Branch**: `006-fix-debugger-bugs`
**Created**: 2026-01-26
**Status**: Draft
**Input**: User description: "Fix debugger issues: nested property access in object_inspect, expression evaluation member access, and reattachment failure after disconnect. Based on BUGS.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reattach Debugger Without Restart (Priority: P1)

A developer is debugging multiple .NET applications sequentially. After finishing debugging one application and disconnecting, they want to attach the debugger to a different application without restarting the MCP server.

**Why this priority**: This is the highest severity bug as it completely blocks the debugging workflow and requires manual intervention (MCP restart). It affects every debugging session after the first one.

**Independent Test**: Can be fully tested by attaching to process A, disconnecting, then attaching to process B - delivers the core value of persistent debugging capability.

**Acceptance Scenarios**:

1. **Given** debugger is attached to process A, **When** user disconnects and attempts to attach to process B, **Then** attachment succeeds without errors
2. **Given** debugger was attached and disconnected multiple times, **When** user attaches to a new process, **Then** attachment succeeds on the first attempt
3. **Given** debugger is disconnected, **When** user attaches to the same process again, **Then** attachment succeeds without requiring MCP restart

---

### User Story 2 - Inspect Nested Object Properties (Priority: P2)

A developer has paused execution at a breakpoint and wants to inspect a deeply nested object property (e.g., `this._currentUser.HomeAddress.City`) in a single operation rather than navigating through each level manually.

**Why this priority**: This significantly improves debugging efficiency. Currently users must make multiple inspection calls to reach nested data, which is tedious and error-prone.

**Independent Test**: Can be fully tested by pausing at a breakpoint and requesting inspection of a nested property path - delivers immediate value for object exploration.

**Acceptance Scenarios**:

1. **Given** debugger is paused at breakpoint with object `this` containing nested property `_currentUser.HomeAddress`, **When** user requests `object_inspect` with reference `this._currentUser.HomeAddress`, **Then** system returns inspection data for the Address object
2. **Given** nested property chain of 3 levels (e.g., `a.b.c`), **When** user inspects the full path, **Then** system resolves each level and returns the final object
3. **Given** nested property path where intermediate value is null, **When** user requests inspection, **Then** system returns clear error indicating which part of the path is null

---

### User Story 3 - Evaluate Member Access Expressions (Priority: P3)

A developer wants to evaluate expressions that access object members (fields and properties) during a debugging session to check values or compute derived results.

**Why this priority**: This extends debugging capabilities beyond simple variable inspection. While nested inspection (P2) covers the primary use case, expression evaluation enables more complex queries.

**Independent Test**: Can be fully tested by evaluating expressions like `_currentUser.Age` or `_settings["timeout"]` at a breakpoint - delivers dynamic value computation.

**Acceptance Scenarios**:

1. **Given** debugger is paused with local variable `_currentUser`, **When** user evaluates expression `_currentUser.Name`, **Then** system returns the value of the Name property
2. **Given** debugger is paused with object containing nested properties, **When** user evaluates expression `this._settings["debug"]`, **Then** system returns the dictionary value
3. **Given** expression with non-existent member, **When** user evaluates expression, **Then** system returns clear error indicating member not found

---

### Edge Cases

- What happens when property path contains array indexer (e.g., `list[0].Name`)?
- How does system handle circular references in nested object inspection?
- What happens when disconnect is called while debugger is in middle of an operation?
- How does system handle evaluation of properties that throw exceptions when accessed?
- What happens when attempting to attach while previous attach is still cleaning up?

## Requirements *(mandatory)*

### Functional Requirements

**Reattachment (Bug #3 - High Severity)**

- **FR-001**: System MUST properly release all native debugging resources when disconnecting from a debug session
- **FR-002**: System MUST allow attaching to a new process immediately after disconnecting from a previous session
- **FR-003**: System MUST support multiple attach/disconnect cycles without resource leaks
- **FR-004**: System MUST handle disconnect gracefully even if the target process has terminated

**Nested Object Inspection (Bug #1 - Medium Severity)**

- **FR-005**: System MUST parse dot-notation property paths (e.g., `this._field.SubProperty`)
- **FR-006**: System MUST resolve each segment of a property path sequentially
- **FR-007**: System MUST return detailed error when any segment of the path cannot be resolved
- **FR-008**: System MUST support both fields and properties in the path

**Expression Evaluation (Bug #2 - Medium Severity)**

- **FR-009**: System MUST support member access operator (`.`) in expressions
- **FR-010**: System MUST support `this` keyword in expressions
- **FR-011**: System MUST return appropriate error for invalid expressions with position information

### Key Entities

- **DebugSession**: Represents an active debugging connection, holds native resource handles that must be properly disposed
- **PropertyPath**: Parsed representation of a dot-notation path (e.g., `this._currentUser.HomeAddress` -> segments: `["this", "_currentUser", "HomeAddress"]`)
- **Expression**: Parsed expression tree supporting member access and basic operators

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can attach/disconnect/reattach 10 times in sequence without failures or MCP restart
- **SC-002**: Nested property inspection resolves paths up to 5 levels deep within 500ms
- **SC-003**: Expression evaluation supports member access chains up to 5 levels
- **SC-004**: All three bugs documented in BUGS.md are resolved and verified through automated tests
- **SC-005**: No memory leaks detected after 100 attach/disconnect cycles (memory returns to baseline)

## Assumptions

- Property paths will use dot notation (not bracket notation for properties)
- Expression evaluation will initially support member access only; arithmetic and method calls are out of scope
- Maximum nesting depth of 10 levels is sufficient for practical debugging scenarios
- Array/list indexer support (`[0]`) is out of scope for this fix (can be addressed separately)
