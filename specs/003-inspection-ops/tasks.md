# Tasks: Inspection Operations

**Input**: Design documents from `/specs/003-inspection-ops/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Depends On**: 001-debug-session (requires active debug session), 002-breakpoint-ops (requires paused state)

**Tests**: Included per Constitution's Test-First (TDD) principle.

**Organization**: Tasks grouped by user story for independent implementation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1, US2, US3, US4, US5)
- File paths use project structure from plan.md

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create inspection infrastructure directories and base structure

- [X] T001 Create DotnetMcp/Models/Inspection/ directory for inspection models
- [X] T002 [P] Create DotnetMcp/Services/Inspection/ directory for inspection services
- [X] T003 [P] Add DI registration for inspection services in DotnetMcp/Program.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core inspection infrastructure required before ANY user story

**CRITICAL**: No user story work can begin until this phase is complete

### Shared Models

- [X] T004 Create VariableScope enum in DotnetMcp/Models/Inspection/VariableScope.cs
- [X] T005 [P] Create Variable record in DotnetMcp/Models/Inspection/Variable.cs
- [X] T006 [P] Create EvaluationError record in DotnetMcp/Models/Inspection/EvaluationError.cs

### ProcessDebugger Extensions

- [X] T007 Add GetThreads() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [X] T008 [P] Add GetStackFrames() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [X] T009 [P] Add GetVariables() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [X] T010 [P] Add Pause() method signature to DotnetMcp/Services/IProcessDebugger.cs

**Checkpoint**: Foundation ready - user story implementation can begin

---

## Phase 3: User Story 1 - Get Stack Trace (Priority: P1) MVP

**Goal**: AI assistants can retrieve call stacks when paused to understand execution flow

**Independent Test**: Attach to process, hit breakpoint, invoke stacktrace_get. Tool returns complete call chain with source locations.

### Tests for User Story 1

> **NOTE: Write tests FIRST, ensure they FAIL before implementation**

- [X] T011 [P] [US1] Contract test for stacktrace_get schema in tests/DotnetMcp.Tests/Contract/StacktraceGetContractTests.cs
- [X] T012 [P] [US1] Unit test for GetStackFrames in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [X] T013 [US1] Integration test for stack trace workflow in tests/DotnetMcp.Tests/Integration/StackInspectionTests.cs

### Implementation for User Story 1

- [X] T014 [P] [US1] Create StackFrame record in DotnetMcp/Models/Inspection/StackFrame.cs
- [X] T015 [US1] Create IStackWalker interface in DotnetMcp/Services/Inspection/IStackWalker.cs (implemented directly in IProcessDebugger)
- [X] T016 [US1] Implement StackWalker.GetFrames() using ICorDebugThread chains in DotnetMcp/Services/Inspection/StackWalker.cs (implemented in ProcessDebugger)
- [X] T017 [US1] Implement frame IL offset to source location mapping using PdbSymbolReader
- [X] T018 [US1] Implement pagination support (start_frame, max_frames) in StackWalker
- [X] T019 [US1] Implement external frame detection (is_external flag for framework code)
- [X] T020 [US1] Create stacktrace_get MCP tool in DotnetMcp/Tools/StacktraceGetTool.cs
- [X] T021 [US1] Add INVALID_THREAD error handling in stacktrace_get
- [X] T022 [US1] Add NOT_PAUSED error handling in stacktrace_get
- [X] T023 [US1] Add logging for stack trace operations in StacktraceGetTool

**Checkpoint**: User Story 1 complete - can get stack traces when paused

---

## Phase 4: User Story 2 - Inspect Variables (Priority: P2)

**Goal**: AI assistants can inspect variable values to diagnose bugs

**Independent Test**: Pause at breakpoint, invoke variables_get. Tool returns locals, arguments, and this with types and values.

### Tests for User Story 2

- [X] T024 [P] [US2] Contract test for variables_get schema in tests/DotnetMcp.Tests/Contract/VariablesGetContractTests.cs
- [X] T025 [P] [US2] Unit test for GetVariables in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [X] T026 [US2] Integration test for variable inspection in tests/DotnetMcp.Tests/Integration/VariableInspectionTests.cs

### Implementation for User Story 2

- [X] T027 [US2] Create IVariableInspector interface (implemented directly in ProcessDebugger per Simplicity principle)
- [X] T028 [US2] Implement GetLocals() using ICorDebugILFrame in ProcessDebugger.cs
- [X] T029 [US2] Implement GetArguments() using ICorDebugILFrame in ProcessDebugger.cs
- [X] T030 [US2] Implement GetThisReference() for instance methods in ProcessDebugger.cs
- [X] T031 [US2] Implement ICorDebugValue to display string formatting (FormatValue in ProcessDebugger.cs)
- [X] T032 [US2] Implement object expansion (TryGetFieldValue for complex types)
- [X] T033 [US2] Implement array/collection expansion (FormatValue handles arrays)
- [X] T034 [US2] Implement circular reference detection during expansion
- [X] T035 [US2] Implement scoped retrieval (locals only, arguments only, this only, all)
- [X] T036 [US2] Implement truncation for large values (strings >100 chars, arrays >100 elements)
- [X] T037 [US2] Create variables_get MCP tool in DotnetMcp/Tools/VariablesGetTool.cs
- [X] T038 [US2] Add INVALID_FRAME error handling in variables_get
- [X] T039 [US2] Add VARIABLE_UNAVAILABLE handling for optimized code
- [X] T040 [US2] Add logging for variable inspection operations

**Checkpoint**: User Stories 1 AND 2 complete - can get stacks and variables

---

## Phase 5: User Story 3 - List Threads (Priority: P3)

**Goal**: AI assistants can see all threads and their states

**Independent Test**: Attach to multi-threaded process, pause, invoke threads_list. All managed threads returned with states.

### Tests for User Story 3

- [X] T041 [P] [US3] Contract test for threads_list schema in tests/DotnetMcp.Tests/Contract/ThreadsListContractTests.cs
- [X] T042 [P] [US3] Unit test for GetThreads in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [X] T043 [US3] Integration test for thread listing in tests/DotnetMcp.Tests/Integration/ThreadInspectionTests.cs

### Implementation for User Story 3

- [X] T044 [P] [US3] Create ThreadState enum in DotnetMcp/Models/Inspection/ThreadState.cs
- [X] T045 [P] [US3] Create ThreadInfo record in DotnetMcp/Models/Inspection/ThreadInfo.cs
- [X] T046 [US3] Create IThreadInspector interface (implemented directly in ProcessDebugger per Simplicity principle)
- [X] T047 [US3] Implement GetThreads() using ICorDebugProcess.Threads in ProcessDebugger.cs
- [X] T048 [US3] Implement thread state mapping from CorDebugUserState to ThreadState enum (MapThreadState)
- [X] T049 [US3] Implement thread name resolution via Thread.Name property (via direct _name field access)
- [X] T050 [US3] Implement current thread detection (is_current flag)
- [X] T051 [US3] Implement thread location extraction for stopped threads (GetLocationForThread)
- [X] T052 [US3] Create threads_list MCP tool in DotnetMcp/Tools/ThreadsListTool.cs
- [X] T053 [US3] Add NOT_ATTACHED error handling in threads_list
- [X] T054 [US3] Add logging for thread listing operations

**Checkpoint**: User Stories 1, 2, AND 3 complete - full basic inspection

---

## Phase 6: User Story 4 - Evaluate Expression (Priority: P4)

**Goal**: AI assistants can evaluate C# expressions in debuggee context

**Independent Test**: Pause at breakpoint, invoke evaluate with expressions (variable, property, method call). Tool returns correct results.

### Tests for User Story 4

- [X] T055 [P] [US4] Contract test for evaluate schema in tests/DotnetMcp.Tests/Contract/EvaluateContractTests.cs
- [X] T056 [P] [US4] Unit test for EvaluateAsync in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [X] T057 [US4] Integration test for expression evaluation in tests/DotnetMcp.Tests/Integration/ExpressionTests.cs

### Implementation for User Story 4

- [X] T058 [P] [US4] Create EvaluationResult record in DotnetMcp/Models/Inspection/EvaluationResult.cs
- [X] T059 [US4] Create IExpressionEvaluator interface (implemented directly in ProcessDebugger per Simplicity principle)
- [X] T060 [US4] Implement simple expression parser (tokenize variable.property.method patterns)
- [X] T061 [US4] Implement variable lookup from current frame locals/arguments
- [X] T062 [US4] Implement property getter calls via ICorDebugEval.CallFunction
- [X] T063 [US4] Implement method calls via ICorDebugEval.CallFunction
- [X] T064 [US4] Implement ICorDebugEval result handling (EvalComplete callback)
- [X] T065 [US4] Implement evaluation timeout handling via ICorDebugEval.Abort
- [X] T066 [US4] Implement exception handling for eval (EvalException callback)
- [X] T067 [US4] Create evaluate MCP tool in DotnetMcp/Tools/EvaluateTool.cs
- [X] T068 [US4] Add SYNTAX_ERROR handling for invalid expressions
- [X] T069 [US4] Add EVAL_TIMEOUT error response
- [X] T070 [US4] Add EVAL_EXCEPTION error response with exception details
- [X] T071 [US4] Add logging for expression evaluation operations

**Checkpoint**: User Stories 1-4 complete - full inspection with evaluation

---

## Phase 7: User Story 5 - Pause Execution (Priority: P5)

**Goal**: AI assistants can pause running processes without breakpoints

**Independent Test**: Attach to running process, invoke debug_pause. Process stops and state becomes inspectable.

### Tests for User Story 5

- [X] T072 [P] [US5] Contract test for debug_pause schema in tests/DotnetMcp.Tests/Contract/DebugPauseContractTests.cs
- [X] T073 [US5] Integration test for pause workflow in tests/DotnetMcp.Tests/Integration/PauseTests.cs

### Implementation for User Story 5

- [X] T074 [US5] Implement ProcessDebugger.PauseAsync() using ICorDebugProcess.Stop in DotnetMcp/Services/ProcessDebugger.cs
- [X] T075 [US5] Handle already-paused case (idempotent - return success)
- [X] T076 [US5] Implement current thread selection after pause
- [X] T077 [US5] Update session state to Paused with reason "pause" after Stop()
- [X] T078 [US5] Create debug_pause MCP tool in DotnetMcp/Tools/DebugPauseTool.cs
- [X] T079 [US5] Add NOT_ATTACHED error handling in debug_pause
- [X] T080 [US5] Return thread summary with locations in pause response
- [X] T081 [US5] Add logging for pause operations

**Checkpoint**: All 5 user stories complete - full inspection functionality

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements affecting multiple user stories

- [X] T082 [P] Validate JSON schemas match contracts in tests/DotnetMcp.Tests/Contract/SchemaValidationTests.cs
- [X] T083 [P] Add performance tests for SC-001 (stack <500ms), SC-002 (vars <1s), SC-004 (threads <200ms)
- [X] T084 [P] Update docs/MCP_TOOLS.md with inspection tools documentation
- [X] T085 Run quickstart.md validation against implementation
- [X] T086 Code cleanup and XML documentation for all public APIs
- [X] T087 Verify multi-threaded application handling (correct thread state reporting)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories
- **User Stories (Phase 3-7)**: All depend on Foundational completion
  - Can proceed in priority order: P1 → P2 → P3 → P4 → P5
  - US1 and US3 can proceed in parallel (no shared components)
  - US2 depends on some US1 patterns (StackFrame contains Variables)
  - US4 is most complex, depends on US2 variable patterns
- **Polish (Phase 8)**: Depends on all user stories

### User Story Dependencies

- **US1 (Stack Trace)**: Foundation only - No other story dependencies
- **US2 (Variables)**: Foundation + shares Variable model with US1
- **US3 (Threads)**: Foundation only - Can parallel with US1/US2
- **US4 (Evaluate)**: Foundation + US2 variable patterns + ICorDebugEval
- **US5 (Pause)**: Foundation only - Can parallel with others

### Within Each User Story

1. Tests MUST be written and FAIL before implementation
2. Models/enums before services
3. Services before tools
4. Core logic before error handling
5. Error handling before logging

### Parallel Opportunities

**Setup Phase:**
```
T002, T003 can run in parallel
```

**Foundational Phase:**
```
T005, T006 can run in parallel (models)
T008, T009, T010 can run in parallel (interface methods)
```

**User Story 1:**
```
T011, T012 can run in parallel (test files)
T014 can run in parallel with tests
```

**User Story 2:**
```
T024, T025 can run in parallel (test files)
```

**User Story 3:**
```
T041, T042 can run in parallel (test files)
T044, T045 can run in parallel (models)
```

**User Story 4:**
```
T055, T056 can run in parallel (test files)
T058 can run in parallel with tests
```

**Cross-story parallelism:**
After Foundational is complete:
- US1 and US3 can work in parallel
- US2 can start after Variable model is defined
- US4 should start after US2 patterns established
- US5 can work in parallel with others

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T010)
3. Complete Phase 3: User Story 1 - Stack Trace (T011-T023)
4. **STOP and VALIDATE**: Test stack trace capability independently
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Stack) → Test independently → Basic inspection
3. Add US2 (Variables) → Test independently → MVP inspection!
4. Add US3 (Threads) → Test independently → Multi-thread visibility
5. Add US4 (Evaluate) → Test independently → Advanced inspection
6. Add US5 (Pause) → Test independently → Full capability
7. Each story adds value without breaking previous stories

---

## Task Summary

| Phase | Tasks | Parallel Opportunities |
|-------|-------|----------------------|
| Setup | 3 | 2 parallel |
| Foundational | 7 | 4 parallel |
| US1 (P1) Stack | 13 | 3 parallel |
| US2 (P2) Variables | 17 | 2 parallel |
| US3 (P3) Threads | 14 | 4 parallel |
| US4 (P4) Evaluate | 17 | 3 parallel |
| US5 (P5) Pause | 10 | 1 parallel |
| Polish | 6 | 3 parallel |
| **Total** | **87** | |

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to user story for traceability
- Each user story is independently completable and testable
- Verify tests FAIL before implementing (TDD per Constitution)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Depends on 001-debug-session and 002-breakpoint-ops infrastructure
