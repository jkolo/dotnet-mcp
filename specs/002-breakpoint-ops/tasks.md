# Tasks: Breakpoint Operations

**Input**: Design documents from `/specs/002-breakpoint-ops/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Depends On**: 001-debug-session (requires active debug session infrastructure)

**Tests**: Included per Constitution's Test-First (TDD) principle.

**Organization**: Tasks grouped by user story for independent implementation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1, US2, US3, US4, US5, US6)
- File paths use project structure from plan.md

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add breakpoint infrastructure dependencies and base structure

- [x] T001 Add System.Reflection.Metadata package reference (if not already in-box with .NET 10) to DotnetMcp/DotnetMcp.csproj
- [x] T002 [P] Create DotnetMcp/Models/Breakpoints/ directory for breakpoint models
- [x] T003 [P] Create DotnetMcp/Services/Breakpoints/ directory for breakpoint services

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core breakpoint infrastructure required before ANY user story

**CRITICAL**: No user story work can begin until this phase is complete

### Breakpoint Models

- [x] T004 Create Breakpoint model in DotnetMcp/Models/Breakpoints/Breakpoint.cs
- [x] T005 [P] Create BreakpointState enum (Pending/Bound/Disabled) in DotnetMcp/Models/Breakpoints/BreakpointState.cs
- [x] T006 [P] Create BreakpointLocation record in DotnetMcp/Models/Breakpoints/BreakpointLocation.cs
- [x] T007 [P] Create BreakpointHit record in DotnetMcp/Models/Breakpoints/BreakpointHit.cs
- [x] T008 [P] Create ExceptionBreakpoint model in DotnetMcp/Models/Breakpoints/ExceptionBreakpoint.cs
- [x] T009 [P] Create ExceptionInfo record in DotnetMcp/Models/Breakpoints/ExceptionInfo.cs

### Breakpoint Services Interfaces

- [x] T010 Create IBreakpointManager interface in DotnetMcp/Services/Breakpoints/IBreakpointManager.cs
- [x] T011 [P] Create IPdbSymbolReader interface in DotnetMcp/Services/Breakpoints/IPdbSymbolReader.cs

### PDB Reading Infrastructure

- [x] T012 Implement PdbSymbolReader (source-to-IL mapping via System.Reflection.Metadata) in DotnetMcp/Services/Breakpoints/PdbSymbolReader.cs
- [x] T013 [P] Implement PdbSymbolCache (cache MetadataReaderProvider per assembly) in DotnetMcp/Services/Breakpoints/PdbSymbolCache.cs
- [x] T014 Implement FindSequencePoint algorithm with column support in PdbSymbolReader

### Breakpoint Registry

- [x] T015 Create BreakpointRegistry for tracking all breakpoints in DotnetMcp/Services/Breakpoints/BreakpointRegistry.cs
- [x] T016 Implement pending breakpoint storage in BreakpointRegistry
- [x] T017 Add DI registration for breakpoint services in DotnetMcp/Program.cs

**Checkpoint**: Foundation ready - user story implementation can begin

---

## Phase 3: User Story 1 - Set Breakpoint at Source Location (Priority: P1) MVP

**Goal**: Create breakpoints at file/line locations with optional column targeting

**Independent Test**: Attach to process, invoke breakpoint_set with file/line, continue execution, verify execution pauses when line is reached.

### Tests for User Story 1

> **NOTE: Write tests FIRST, ensure they FAIL before implementation**

- [x] T018 [P] [US1] Contract test for breakpoint_set schema in tests/DotnetMcp.Tests/Contract/BreakpointSetContractTests.cs
- [x] T019 [P] [US1] Unit test for PdbSymbolReader.FindILOffset in tests/DotnetMcp.Tests/Unit/PdbSymbolReaderTests.cs
- [x] T020 [P] [US1] Unit test for BreakpointManager.SetBreakpointAsync in tests/DotnetMcp.Tests/Unit/BreakpointManagerTests.cs
- [ ] T021 [US1] Integration test for set breakpoint workflow in tests/DotnetMcp.Tests/Integration/SetBreakpointTests.cs

### Implementation for User Story 1

- [x] T022 [US1] Implement source-to-IL offset resolution in PdbSymbolReader in DotnetMcp/Services/Breakpoints/PdbSymbolReader.cs
- [x] T023 [US1] Implement column-level sequence point matching in PdbSymbolReader for lambda targeting
- [x] T024 [US1] Implement BreakpointManager.SetBreakpointAsync using ICorDebugCode.CreateBreakpoint in DotnetMcp/Services/Breakpoints/BreakpointManager.cs
- [x] T025 [US1] Handle breakpoint activation with ICorDebugFunctionBreakpoint.Activate in BreakpointManager
- [x] T026 [US1] Implement duplicate breakpoint detection (return existing ID for same location) in BreakpointManager
- [x] T027 [US1] Create breakpoint_set MCP tool in DotnetMcp/Tools/BreakpointSetTool.cs
- [x] T028 [US1] Add INVALID_LINE error handling with nearest valid line suggestions in breakpoint_set
- [x] T029 [US1] Add INVALID_COLUMN error handling with available sequence points in breakpoint_set
- [x] T030 [US1] Add logging for breakpoint set operations in BreakpointSetTool

**Checkpoint**: User Story 1 complete - can set breakpoints at source locations

---

## Phase 4: User Story 2 - Wait for Breakpoint Hit (Priority: P2)

**Goal**: Block until a breakpoint is hit or timeout expires

**Independent Test**: Set breakpoint, continue execution, invoke breakpoint_wait. When breakpoint is hit, tool returns with location and context.

### Tests for User Story 2

- [x] T031 [P] [US2] Contract test for breakpoint_wait schema in tests/DotnetMcp.Tests/Contract/BreakpointWaitContractTests.cs
- [x] T032 [P] [US2] Unit test for hit queue and timeout in tests/DotnetMcp.Tests/Unit/BreakpointManagerTests.cs
- [ ] T033 [US2] Integration test for wait with hit in tests/DotnetMcp.Tests/Integration/WaitBreakpointTests.cs

### Implementation for User Story 2

- [x] T034 [US2] Create BreakpointHitQueue for queuing hit events in DotnetMcp/Services/Breakpoints/BreakpointHitQueue.cs
- [ ] T035 [US2] Implement ManagedCallback.OnBreakpoint handler to queue hits in DotnetMcp/Services/ProcessDebugger.cs
- [x] T036 [US2] Implement hit count tracking in BreakpointRegistry on each hit
- [ ] T037 [US2] Extract source location from ICorDebugILFrame.GetIP on breakpoint hit
- [x] T038 [US2] Implement BreakpointManager.WaitForHitAsync with timeout support in BreakpointManager.cs
- [x] T039 [US2] Create breakpoint_wait MCP tool in DotnetMcp/Tools/BreakpointWaitTool.cs
- [x] T040 [US2] Handle timeout response (hit=false, timeout=true) in breakpoint_wait
- [x] T041 [US2] Add logging for wait operations in BreakpointWaitTool

**Checkpoint**: User Stories 1 AND 2 complete - can set breakpoints and wait for hits

---

## Phase 5: User Story 3 - List Active Breakpoints (Priority: P3)

**Goal**: Return all breakpoints with their details (IDs, locations, enabled status, hit counts)

**Independent Test**: Set several breakpoints, invoke breakpoint_list, verify all breakpoints returned with correct details.

### Tests for User Story 3

- [x] T042 [P] [US3] Contract test for breakpoint_list schema in tests/DotnetMcp.Tests/Contract/BreakpointListContractTests.cs
- [x] T043 [P] [US3] Unit test for list with various breakpoint states in tests/DotnetMcp.Tests/Unit/BreakpointManagerTests.cs
- [ ] T044 [US3] Integration test for list operations in tests/DotnetMcp.Tests/Integration/ListBreakpointsTests.cs

### Implementation for User Story 3

- [x] T045 [US3] Implement BreakpointManager.ListBreakpointsAsync in BreakpointManager.cs
- [x] T046 [US3] Include pending/bound/disabled state in list output
- [x] T047 [US3] Create breakpoint_list MCP tool in DotnetMcp/Tools/BreakpointListTool.cs
- [x] T048 [US3] Handle empty list case (return empty array, count=0) in breakpoint_list
- [x] T049 [US3] Add logging for list operations in BreakpointListTool

**Checkpoint**: User Stories 1, 2, AND 3 complete - can set, wait, and list breakpoints

---

## Phase 6: User Story 4 - Remove Breakpoint (Priority: P4)

**Goal**: Remove breakpoints by ID to prevent unwanted pauses

**Independent Test**: Set breakpoint, confirm via list, remove it, confirm it no longer appears.

### Tests for User Story 4

- [x] T050 [P] [US4] Contract test for breakpoint_remove schema in tests/DotnetMcp.Tests/Contract/BreakpointRemoveContractTests.cs
- [x] T051 [P] [US4] Unit test for remove operations in tests/DotnetMcp.Tests/Unit/BreakpointManagerTests.cs
- [ ] T052 [US4] Integration test for remove workflow in tests/DotnetMcp.Tests/Integration/RemoveBreakpointTests.cs

### Implementation for User Story 4

- [x] T053 [US4] Implement BreakpointManager.RemoveBreakpointAsync using ICorDebugBreakpoint.Activate(false) in BreakpointManager.cs
- [x] T054 [US4] Remove breakpoint from BreakpointRegistry on successful removal
- [x] T055 [US4] Create breakpoint_remove MCP tool in DotnetMcp/Tools/BreakpointRemoveTool.cs
- [x] T056 [US4] Handle BREAKPOINT_NOT_FOUND error in breakpoint_remove
- [x] T057 [US4] Add logging for remove operations in BreakpointRemoveTool

**Checkpoint**: User Stories 1-4 complete - full basic breakpoint lifecycle

---

## Phase 7: User Story 5 - Conditional Breakpoints (Priority: P5)

**Goal**: Set breakpoints that only trigger when a condition is true

**Independent Test**: Set conditional breakpoint in loop (e.g., i > 5), run code, verify execution only pauses when condition is true.

### Tests for User Story 5

- [x] T058 [P] [US5] Unit test for condition storage in breakpoint model in tests/DotnetMcp.Tests/Unit/BreakpointTests.cs
- [x] T059 [P] [US5] Unit test for simple condition evaluation in tests/DotnetMcp.Tests/Unit/ConditionEvaluatorTests.cs
- [ ] T060 [US5] Integration test for conditional breakpoint in tests/DotnetMcp.Tests/Integration/ConditionalBreakpointTests.cs

### Implementation for User Story 5

- [x] T061 [US5] Create IConditionEvaluator interface in DotnetMcp/Services/Breakpoints/IConditionEvaluator.cs
- [x] T062 [US5] Implement simple condition parsing (hit count, literals) in DotnetMcp/Services/Breakpoints/SimpleConditionEvaluator.cs
- [ ] T063 [US5] Implement ICorDebugEval-based condition evaluation in DotnetMcp/Services/Breakpoints/DebuggerConditionEvaluator.cs
- [x] T064 [US5] Integrate condition evaluation in OnBreakpoint callback (silent continue if false)
- [x] T065 [US5] Add condition parameter to breakpoint_set tool
- [x] T066 [US5] Add INVALID_CONDITION error handling with syntax error position
- [x] T067 [US5] Handle EVAL_FAILED for undefined variables (report error, don't crash debuggee)

**Checkpoint**: User Stories 1-5 complete - can use conditional breakpoints

---

## Phase 8: User Story 6 - Exception Breakpoints (Priority: P6)

**Goal**: Break when specific exception types are thrown

**Independent Test**: Set exception breakpoint for NullReferenceException, run code that throws one, verify execution pauses at throw site.

### Tests for User Story 6

- [x] T068 [P] [US6] Contract test for breakpoint_set_exception schema in tests/DotnetMcp.Tests/Contract/BreakpointSetExceptionContractTests.cs
- [x] T069 [P] [US6] Unit test for exception breakpoint registration in tests/DotnetMcp.Tests/Unit/ExceptionBreakpointTests.cs
- [ ] T070 [US6] Integration test for exception breakpoint in tests/DotnetMcp.Tests/Integration/ExceptionBreakpointTests.cs

### Implementation for User Story 6

- [x] T071 [US6] Create ExceptionBreakpointRegistry in DotnetMcp/Services/Breakpoints/ExceptionBreakpointRegistry.cs (integrated into BreakpointRegistry)
- [ ] T072 [US6] Implement ManagedCallback2.Exception handler for first-chance/second-chance in DotnetMcp/Services/ProcessDebugger.cs
- [x] T073 [US6] Implement exception type matching (including subtypes via IsAssignableTo) in ExceptionBreakpointRegistry
- [ ] T074 [US6] Extract exception message from ICorDebugValue on exception hit
- [x] T075 [US6] Create breakpoint_set_exception MCP tool in DotnetMcp/Tools/BreakpointSetExceptionTool.cs
- [x] T076 [US6] Include ExceptionInfo in breakpoint_wait response when exception breakpoint hit
- [x] T077 [US6] Add logging for exception breakpoint operations

**Checkpoint**: All 6 user stories complete - full breakpoint functionality

---

## Phase 9: Pending Breakpoints & Module Loading

**Purpose**: Handle breakpoints set before target module loads

- [ ] T078 Implement pending breakpoint binding in OnModuleLoad callback in DotnetMcp/Services/ProcessDebugger.cs
- [ ] T079 [P] Transition breakpoint state Pending→Bound when module loads
- [ ] T080 [P] Transition breakpoint state Bound→Pending when module unloads
- [ ] T081 Notify breakpoint verified via status update when pending becomes bound
- [ ] T082 Add logging for pending breakpoint state transitions

---

## Phase 10: Enable/Disable Breakpoints

**Purpose**: Toggle breakpoint without removing

- [x] T083 [P] Contract test for breakpoint_enable schema in tests/DotnetMcp.Tests/Contract/BreakpointEnableContractTests.cs
- [x] T084 Implement BreakpointManager.EnableBreakpointAsync in BreakpointManager.cs
- [x] T085 Create breakpoint_enable MCP tool in DotnetMcp/Tools/BreakpointEnableTool.cs
- [x] T086 Add logging for enable/disable operations

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Improvements affecting multiple user stories

- [x] T087 [P] Validate JSON schema matches contracts in tests/DotnetMcp.Tests/Contract/SchemaValidationTests.cs
- [x] T088 [P] Add performance tests for SC-001 (set <2s) and SC-002 (wait response <100ms)
- [x] T089 [P] Update docs/MCP_TOOLS.md with breakpoint tools
- [x] T090 Run quickstart.md validation against implementation
- [x] T091 Code cleanup and XML documentation for all public APIs
- [x] T092 Verify multi-threaded application handling (correct thread ID in hit info)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories
- **User Stories (Phase 3-8)**: All depend on Foundational completion
  - Can proceed in priority order: P1 → P2 → P3 → P4 → P5 → P6
  - Or some can proceed in parallel (US3/US4 have no dependency on US2)
- **Pending Breakpoints (Phase 9)**: Depends on US1 (breakpoint set)
- **Enable/Disable (Phase 10)**: Depends on US1 (breakpoint set)
- **Polish (Phase 11)**: Depends on all user stories

### User Story Dependencies

- **US1 (Set)**: Foundation only - No other story dependencies
- **US2 (Wait)**: Foundation + US1 (needs breakpoints to wait on)
- **US3 (List)**: Foundation only - Can list even without other stories
- **US4 (Remove)**: Foundation + US1 (needs breakpoints to remove)
- **US5 (Conditional)**: Foundation + US1 + US2 (needs set and hit handling)
- **US6 (Exception)**: Foundation + US2 (needs hit/wait infrastructure)

### Within Each User Story

1. Tests MUST be written and FAIL before implementation
2. Models before services
3. Services before tools
4. Core logic before error handling
5. Error handling before logging

### Parallel Opportunities

**Setup Phase:**
```
T002, T003 can run in parallel (directory creation)
```

**Foundational Phase:**
```
T005, T006, T007, T008, T009 can run in parallel (all models)
T010, T011 can run in parallel (interfaces)
T012, T013 can run in parallel (PDB reader components)
```

**User Story 1:**
```
T018, T019, T020 can run in parallel (test files)
```

**User Story 2:**
```
T031, T032 can run in parallel (test files)
```

**User Story 3:**
```
T042, T043 can run in parallel (test files)
```

**User Story 4:**
```
T050, T051 can run in parallel (test files)
```

**User Story 5:**
```
T058, T059 can run in parallel (test files)
```

**User Story 6:**
```
T068, T069 can run in parallel (test files)
```

**Cross-story parallelism:**
After Foundational is complete, US1 must complete first. Then US2, US3, US4 can work in parallel.

---

## Implementation Strategy

### MVP First (User Story 1 + 2 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T017)
3. Complete Phase 3: User Story 1 - Set Breakpoint (T018-T030)
4. Complete Phase 4: User Story 2 - Wait for Hit (T031-T041)
5. **STOP and VALIDATE**: Test set+wait capability independently
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Set) → Test independently → Basic breakpoints
3. Add US2 (Wait) → Test independently → MVP!
4. Add US3 (List) → Test independently → Visibility
5. Add US4 (Remove) → Test independently → Cleanup
6. Add US5 (Conditional) → Test independently → Advanced filtering
7. Add US6 (Exception) → Test independently → Exception debugging
8. Each story adds value without breaking previous stories

---

## Task Summary

| Phase | Tasks | Parallel Opportunities |
|-------|-------|----------------------|
| Setup | 3 | 2 parallel |
| Foundational | 14 | 9 parallel |
| US1 (P1) Set | 13 | 4 parallel |
| US2 (P2) Wait | 11 | 3 parallel |
| US3 (P3) List | 8 | 3 parallel |
| US4 (P4) Remove | 8 | 3 parallel |
| US5 (P5) Conditional | 10 | 2 parallel |
| US6 (P6) Exception | 10 | 3 parallel |
| Pending BPs | 5 | 2 parallel |
| Enable/Disable | 4 | 1 parallel |
| Polish | 6 | 3 parallel |
| **Total** | **92** | |

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to user story for traceability
- Each user story is independently completable and testable
- Verify tests FAIL before implementing (TDD per Constitution)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Depends on 001-debug-session infrastructure being complete
