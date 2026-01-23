# Feature Specification: Inspection Operations

**Feature Branch**: `003-inspection-ops`
**Created**: 2026-01-23
**Status**: Draft
**Input**: User description: "MCP tools for runtime inspection during debugging: threads_list, stacktrace_get, variables_get, evaluate, debug_pause"
**Depends On**: 001-debug-session (requires active debug session), 002-breakpoint-ops (requires paused state)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get Stack Trace (Priority: P1)

As an AI assistant debugging a user's application, I need to examine the call stack
when execution is paused so I can understand how the program reached the current
point and trace the flow of execution through method calls.

**Why this priority**: Stack traces are the most fundamental inspection operation.
When a breakpoint is hit or an exception occurs, the first thing needed is
understanding the call chain. This enables all other debugging analysis.

**Independent Test**: Can be tested by attaching to a process, hitting a breakpoint,
and invoking stacktrace_get. The tool should return the complete call chain with
source locations.

**Acceptance Scenarios**:

1. **Given** a debug session is paused at a breakpoint, **When** the AI invokes
   `stacktrace_get` with no parameters, **Then** the system returns the stack
   frames for the current thread with function names, file paths, and line numbers.

2. **Given** a stack trace is retrieved, **When** the AI examines a frame,
   **Then** each frame includes the module name, function signature, and source
   location (if symbols are available).

3. **Given** a deep call stack (e.g., 50+ frames), **When** the AI requests frames
   with pagination parameters, **Then** the system returns only the requested
   range of frames with total count information.

4. **Given** a frame is in external/framework code without source, **When** the
   AI retrieves the stack trace, **Then** those frames are marked as external
   with module information but no source location.

---

### User Story 2 - Inspect Variables (Priority: P2)

As an AI assistant, I need to inspect variables in the current scope so I can
examine the program state, identify incorrect values, and understand why the
code is behaving unexpectedly.

**Why this priority**: After seeing the stack trace, variable inspection is the
next critical operation. Understanding what values are in scope is essential
for diagnosing bugs.

**Independent Test**: Can be tested by pausing at a breakpoint, invoking
variables_get for the current frame, and verifying all locals, arguments,
and `this` reference are returned with their values.

**Acceptance Scenarios**:

1. **Given** a debug session is paused at a breakpoint, **When** the AI invokes
   `variables_get` for the current frame, **Then** the system returns all local
   variables, arguments, and `this` reference with their types and values.

2. **Given** a variable is a complex object, **When** the AI requests to expand
   it, **Then** the system returns the object's fields and properties with their
   values, supporting recursive expansion.

3. **Given** a variable is a collection (array, list, dictionary), **When** the
   AI expands it, **Then** the system returns the collection elements with their
   indices/keys and values.

4. **Given** a variable is null, **When** the AI retrieves variables, **Then**
   the system clearly indicates the null value without erroring.

5. **Given** a variable value is very large (e.g., long string, large array),
   **When** the AI retrieves it, **Then** the system returns a truncated
   preview with total size information.

---

### User Story 3 - List Threads (Priority: P3)

As an AI assistant, I need to list all threads in the debuggee process so I can
understand the application's concurrency model and identify which threads are
active, waiting, or blocked.

**Why this priority**: Thread visibility is important for multi-threaded
applications but less critical than stack/variable inspection for basic
debugging workflows.

**Independent Test**: Can be tested by attaching to a multi-threaded process,
pausing execution, and invoking threads_list. All managed threads should be
returned with their states.

**Acceptance Scenarios**:

1. **Given** a debug session is active, **When** the AI invokes `threads_list`,
   **Then** the system returns all managed threads with their IDs, names, and
   current states.

2. **Given** a thread is stopped, **When** the AI lists threads, **Then** that
   thread includes its current location (function, file, line).

3. **Given** one thread hit a breakpoint, **When** the AI lists threads, **Then**
   that thread is marked as the "current" or "active" thread.

4. **Given** a thread pool worker thread, **When** the AI lists threads, **Then**
   the thread name reflects its role (e.g., ".NET ThreadPool Worker").

---

### User Story 4 - Evaluate Expression (Priority: P4)

As an AI assistant, I need to evaluate arbitrary expressions in the debuggee
context so I can compute derived values, call methods to inspect state, and
test hypotheses about the code behavior.

**Why this priority**: Expression evaluation is powerful but also the most
complex operation. Basic inspection (stack, variables) provides most debugging
value; evaluation adds advanced capabilities.

**Independent Test**: Can be tested by pausing at a breakpoint, invoking
evaluate with various expressions (variable access, property access, method
calls), and verifying correct results.

**Acceptance Scenarios**:

1. **Given** a debug session is paused, **When** the AI evaluates a simple
   variable expression (e.g., `userId`), **Then** the system returns the
   variable's current value.

2. **Given** a debug session is paused, **When** the AI evaluates a property
   access expression (e.g., `user.Name`), **Then** the system executes the
   property getter and returns the result.

3. **Given** a debug session is paused, **When** the AI evaluates a method
   call expression (e.g., `users.Count()`), **Then** the system executes
   the method in the debuggee and returns the result.

4. **Given** an expression has a syntax error, **When** the AI attempts
   evaluation, **Then** the system returns a clear error message explaining
   the syntax issue.

5. **Given** an expression throws an exception during evaluation, **When**
   the AI evaluates it, **Then** the system returns the exception details
   without crashing the debuggee.

6. **Given** expression evaluation would cause side effects (e.g., modifying
   state), **When** the AI evaluates it, **Then** the side effects are
   applied to the debuggee (with appropriate warning if detectable).

---

### User Story 5 - Pause Execution (Priority: P5)

As an AI assistant, I need to pause a running process so I can interrupt
execution at any point to inspect state, even when no breakpoint is set.

**Why this priority**: Pause is useful but less common than breakpoint-based
workflows. Most debugging starts with breakpoints; pause is for unexpected
situations.

**Independent Test**: Can be tested by attaching to a running process (not
paused), invoking debug_pause, and verifying the process stops and state
becomes inspectable.

**Acceptance Scenarios**:

1. **Given** a debug session with a running process, **When** the AI invokes
   `debug_pause`, **Then** all threads stop execution and the session state
   becomes "paused".

2. **Given** a process is paused via debug_pause, **When** the AI inspects
   the state, **Then** threads, stack traces, and variables are accessible.

3. **Given** a process is already paused, **When** the AI invokes debug_pause,
   **Then** the system returns success without error (idempotent).

---

### Edge Cases

- What happens when inspecting variables in optimized (release) code?
  Some variables may be unavailable or show incorrect values due to
  optimizations; the system should indicate when this occurs.

- What happens when evaluating an expression that causes an infinite loop?
  The system should support evaluation timeouts to prevent hanging.

- What happens when a thread is in native code (P/Invoke)?
  The system should show available managed frames and indicate native
  transition points.

- What happens when expanding a circular reference (object A references B
  which references A)?
  The system should detect cycles and prevent infinite expansion.

- What happens when the process exits while inspecting variables?
  The system should return appropriate error indicating the session ended.

- What happens when requesting stack trace for an invalid thread ID?
  The system should return a clear error that the thread was not found.

- What happens when evaluating an expression references a variable that's
  been optimized away?
  The system should report the variable is unavailable in the current context.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a `stacktrace_get` tool that returns the call
  stack for a specified thread with function names, source locations, and
  module information.

- **FR-002**: System MUST provide a `variables_get` tool that returns variables
  (locals, arguments, this) for a specified stack frame with types and values.

- **FR-003**: System MUST provide a `threads_list` tool that returns all managed
  threads with their IDs, names, states, and current locations.

- **FR-004**: System MUST provide an `evaluate` tool that evaluates expressions
  in the debuggee context and returns results.

- **FR-005**: System MUST provide a `debug_pause` tool that pauses execution
  of a running debuggee process.

- **FR-006**: System MUST support pagination for stack traces (start frame,
  max frames) to handle deep call stacks efficiently.

- **FR-007**: System MUST support variable expansion for complex objects,
  allowing hierarchical navigation of object graphs.

- **FR-008**: System MUST support scoped variable retrieval (locals only,
  arguments only, this only, or all).

- **FR-009**: System MUST handle evaluation timeouts to prevent hanging on
  long-running or infinite expressions.

- **FR-010**: System MUST return structured error responses when operations
  fail, including actionable error messages.

- **FR-011**: System MUST indicate when variables are unavailable due to
  code optimization.

- **FR-012**: System MUST identify the "current" thread when multiple threads
  exist (the thread that triggered the pause).

### Key Entities

- **Thread**: A managed thread in the debuggee. Key attributes: ID, name, state
  (running, stopped, waiting), current location (if stopped), is_current flag.

- **StackFrame**: A frame in the call stack. Key attributes: index, function
  name, source location (file, line, column), module name, is_external flag,
  arguments list.

- **Variable**: A named value in scope. Key attributes: name, type, value
  (display string), has_children flag, children_count, scope (local, argument,
  this, field).

- **EvaluationResult**: Result of expression evaluation. Key attributes: result
  value, type, has_children flag, error (if failed).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: AI assistants can retrieve a stack trace within 500 milliseconds
  of invocation for typical call stacks (under 50 frames).

- **SC-002**: AI assistants can inspect all variables in a frame within 1 second,
  including type information and display values.

- **SC-003**: Expression evaluation completes or times out within the specified
  timeout period (default 5 seconds).

- **SC-004**: Thread listing returns all managed threads within 200 milliseconds.

- **SC-005**: The debug_pause operation stops all threads within 1 second of
  invocation.

- **SC-006**: AI assistants can complete a full inspection workflow (pause,
  list threads, get stack, inspect variables) without manual intervention.

- **SC-007**: 100% of inspection operations return clear error messages when
  they fail, allowing the AI to understand and report the issue.

## Assumptions

- A debug session is already established (via 001-debug-session feature) before
  inspection operations can be used.

- The debuggee must be in a paused state for stack trace and variable inspection
  (except for threads_list which works in any state).

- Source files and debug symbols (PDB) are available for source location
  resolution; frames without symbols will show module/function only.

- Expression evaluation uses C# syntax compatible with the debuggee context.

- Variable values are formatted as display strings suitable for AI consumption;
  binary data is represented appropriately (hex, base64, or description).

- The system operates in a single-user debugging model; concurrent inspection
  requests from multiple callers are not expected.
