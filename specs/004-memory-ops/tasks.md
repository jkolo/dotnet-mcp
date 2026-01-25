# Tasks: Memory Inspection Operations

**Input**: Design documents from `/specs/004-memory-ops/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Depends On**: 001-debug-session (requires active debug session), 003-inspection-ops (requires paused state, variable inspection)

**Tests**: Included per Constitution's Test-First (TDD) principle.

**Organization**: Tasks grouped by user story for independent implementation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1, US2, US3, US4)
- File paths use project structure from plan.md

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create memory inspection infrastructure directories and base structure

- [x] T001 Create DotnetMcp/Models/Memory/ directory for memory inspection models
- [x] T002 [P] Add error codes for memory operations to DotnetMcp/Models/ErrorCodes.cs
- [x] T003 [P] Add DI registration for memory inspection services in DotnetMcp/Program.cs (if needed)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core memory inspection infrastructure required before ANY user story

**CRITICAL**: No user story work can begin until this phase is complete

### Shared Models

- [x] T004 Create ReferenceType enum in DotnetMcp/Models/Memory/ReferenceType.cs
- [x] T005 [P] Create FieldDetail record in DotnetMcp/Models/Memory/FieldDetail.cs
- [x] T006 [P] Create ObjectInspection record in DotnetMcp/Models/Memory/ObjectInspection.cs
- [x] T007 [P] Create MemoryRegion record in DotnetMcp/Models/Memory/MemoryRegion.cs
- [x] T008 [P] Create ReferenceInfo record in DotnetMcp/Models/Memory/ReferenceInfo.cs
- [x] T009 [P] Create ReferencesResult record in DotnetMcp/Models/Memory/ReferencesResult.cs
- [x] T010 [P] Create LayoutField record in DotnetMcp/Models/Memory/LayoutField.cs
- [x] T011 [P] Create PaddingRegion record in DotnetMcp/Models/Memory/PaddingRegion.cs
- [x] T012 [P] Create TypeLayout record in DotnetMcp/Models/Memory/TypeLayout.cs

### ProcessDebugger Extensions

- [x] T013 Add InspectObject() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [x] T014 [P] Add ReadMemory() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [x] T015 [P] Add GetOutboundReferences() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [x] T016 [P] Add GetTypeLayout() method signature to DotnetMcp/Services/IProcessDebugger.cs

**Checkpoint**: Foundation ready - user story implementation can begin

---

## Phase 3: User Story 1 - Inspect Heap Object (Priority: P1) MVP

**Goal**: AI assistants can inspect heap object contents to understand object state

**Independent Test**: Attach to process, hit breakpoint, invoke object_inspect. Tool returns complete field list with types and values.

### Tests for User Story 1

> **NOTE: Write tests FIRST, ensure they FAIL before implementation**

- [ ] T017 [P] [US1] Contract test for object_inspect schema in tests/DotnetMcp.Tests/Contract/ObjectInspectContractTests.cs
- [ ] T018 [P] [US1] Unit test for InspectObject in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [ ] T019 [US1] Integration test for object inspection workflow in tests/DotnetMcp.Tests/Integration/ObjectInspectionTests.cs

### Implementation for User Story 1

- [x] T020 [US1] Implement InspectObject() using ICorDebugObjectValue in DotnetMcp/Services/ProcessDebugger.cs
- [x] T021 [US1] Implement field enumeration using type metadata in ProcessDebugger
- [x] T022 [US1] Implement value formatting for primitive types (int, string, bool, etc.) in ProcessDebugger
- [x] T023 [US1] Implement value formatting for complex types (arrays, collections) in ProcessDebugger
- [x] T024 [US1] Implement circular reference detection with address tracking in ProcessDebugger
- [x] T025 [US1] Implement depth-limited recursive expansion for nested objects in ProcessDebugger
- [x] T026 [US1] Create object_inspect MCP tool in DotnetMcp/Tools/ObjectInspectTool.cs
- [x] T027 [US1] Add NOT_PAUSED error handling in object_inspect
- [x] T028 [US1] Add INVALID_REFERENCE error handling in object_inspect
- [x] T029 [US1] Add NULL_REFERENCE handling in object_inspect (return isNull: true)
- [x] T030 [US1] Add logging for object inspection operations in ObjectInspectTool

**Checkpoint**: User Story 1 complete - can inspect heap objects when paused

---

## Phase 4: User Story 2 - Read Raw Memory (Priority: P2)

**Goal**: AI assistants can read raw memory bytes at any address

**Independent Test**: Pause at breakpoint, get memory address, invoke memory_read. Tool returns hex dump with ASCII view.

### Tests for User Story 2

- [ ] T031 [P] [US2] Contract test for memory_read schema in tests/DotnetMcp.Tests/Contract/MemoryReadContractTests.cs
- [ ] T032 [P] [US2] Unit test for ReadMemory in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [ ] T033 [US2] Integration test for memory read workflow in tests/DotnetMcp.Tests/Integration/MemoryReadTests.cs

### Implementation for User Story 2

- [x] T034 [US2] Implement ReadMemory() using ICorDebugProcess.ReadMemory in DotnetMcp/Services/ProcessDebugger.cs
- [x] T035 [US2] Implement hex formatting with 16 bytes per line in ProcessDebugger
- [x] T036 [US2] Implement ASCII representation (printable chars, '.' for others) in ProcessDebugger
- [x] T037 [US2] Implement size limit validation (max 64KB) in ProcessDebugger
- [x] T038 [US2] Implement partial read handling (return actualSize < requestedSize) in ProcessDebugger
- [x] T039 [US2] Create memory_read MCP tool in DotnetMcp/Tools/MemoryReadTool.cs
- [x] T040 [US2] Add INVALID_ADDRESS error handling in memory_read
- [x] T041 [US2] Add SIZE_EXCEEDED error handling in memory_read
- [x] T042 [US2] Add logging for memory read operations in MemoryReadTool

**Checkpoint**: User Story 2 complete - can read raw memory when paused

---

## Phase 5: User Story 3 - Analyze Object References (Priority: P3)

**Goal**: AI assistants can analyze object reference graphs for memory leak debugging

**Independent Test**: Pause at breakpoint, get object reference, invoke references_get. Tool returns outbound references with paths.

### Tests for User Story 3

- [ ] T043 [P] [US3] Contract test for references_get schema in tests/DotnetMcp.Tests/Contract/ReferencesGetContractTests.cs
- [ ] T044 [P] [US3] Unit test for GetOutboundReferences in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [ ] T045 [US3] Integration test for reference analysis in tests/DotnetMcp.Tests/Integration/ReferenceAnalysisTests.cs

### Implementation for User Story 3

- [x] T046 [US3] Implement GetOutboundReferences() by enumerating reference-type fields in DotnetMcp/Services/ProcessDebugger.cs
- [x] T047 [US3] Implement array element reference enumeration in ProcessDebugger
- [x] T048 [US3] Implement reference path tracking (field names, array indices) in ProcessDebugger
- [x] T049 [US3] Implement result limiting (max 100 references) with truncation indicator in ProcessDebugger
- [x] T050 [US3] Create references_get MCP tool in DotnetMcp/Tools/ReferencesGetTool.cs
- [x] T051 [US3] Add direction parameter handling (outbound/inbound/both) in references_get
- [x] T052 [US3] Add logging for reference analysis operations in ReferencesGetTool

**Checkpoint**: User Story 3 complete - can analyze object references when paused

---

## Phase 6: User Story 4 - Get Object Memory Layout (Priority: P4)

**Goal**: AI assistants can understand type memory layout for optimization

**Independent Test**: Pause at breakpoint, provide type name or object, invoke layout_get. Tool returns field layout with offsets.

### Tests for User Story 4

- [ ] T053 [P] [US4] Contract test for layout_get schema in tests/DotnetMcp.Tests/Contract/LayoutGetContractTests.cs
- [ ] T054 [P] [US4] Unit test for GetTypeLayout in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [ ] T055 [US4] Integration test for layout inspection in tests/DotnetMcp.Tests/Integration/LayoutInspectionTests.cs

### Implementation for User Story 4

- [x] T056 [US4] Implement GetTypeLayout() using type metadata and ICorDebugType in DotnetMcp/Services/ProcessDebugger.cs
- [x] T057 [US4] Implement field offset calculation for LayoutKind.Auto types in ProcessDebugger
- [x] T058 [US4] Implement inherited field enumeration in ProcessDebugger
- [x] T059 [US4] Implement padding detection and reporting in ProcessDebugger
- [x] T060 [US4] Implement object header size calculation (reference vs value types) in ProcessDebugger
- [x] T061 [US4] Create layout_get MCP tool in DotnetMcp/Tools/LayoutGetTool.cs
- [x] T062 [US4] Add TYPE_NOT_FOUND error handling in layout_get
- [x] T063 [US4] Add logging for layout operations in LayoutGetTool

**Checkpoint**: User Story 4 complete - can get type memory layout when paused

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Performance validation and documentation updates

- [x] T064 [P] Performance test for object inspection <2s (SC-001) in tests/DotnetMcp.Tests/Performance/MemoryPerformanceTests.cs
- [x] T065 [P] Performance test for memory read <1s (SC-002) in tests/DotnetMcp.Tests/Performance/MemoryPerformanceTests.cs
- [x] T066 [P] Performance test for layout <1s (SC-004) in tests/DotnetMcp.Tests/Performance/MemoryPerformanceTests.cs
- [x] T067 Update docs/MCP_TOOLS.md with object_inspect documentation
- [x] T068 [P] Update docs/MCP_TOOLS.md with memory_read documentation
- [x] T069 [P] Update docs/MCP_TOOLS.md with references_get documentation
- [x] T070 [P] Update docs/MCP_TOOLS.md with layout_get documentation
- [x] T071 Run quickstart.md validation scenarios

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories
- **User Stories (Phase 3-6)**: All depend on Foundational phase completion
  - User stories can proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3 → P4)
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational - Independent of US1
- **User Story 3 (P3)**: Can start after Foundational - May reuse US1 inspection patterns
- **User Story 4 (P4)**: Can start after Foundational - Independent of other stories

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Interface changes before implementation
- Core implementation before tool wrapper
- Error handling before logging
- Story complete before moving to next priority

### Parallel Opportunities

- All models in Phase 2 can be created in parallel (T005-T012)
- All interface methods can be added in parallel (T013-T016)
- All contract tests within each story can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: Phase 2 Models

```bash
# Launch all models together:
Task: "Create ReferenceType enum in DotnetMcp/Models/Memory/ReferenceType.cs"
Task: "Create FieldDetail record in DotnetMcp/Models/Memory/FieldDetail.cs"
Task: "Create ObjectInspection record in DotnetMcp/Models/Memory/ObjectInspection.cs"
Task: "Create MemoryRegion record in DotnetMcp/Models/Memory/MemoryRegion.cs"
Task: "Create ReferenceInfo record in DotnetMcp/Models/Memory/ReferenceInfo.cs"
Task: "Create ReferencesResult record in DotnetMcp/Models/Memory/ReferencesResult.cs"
Task: "Create LayoutField record in DotnetMcp/Models/Memory/LayoutField.cs"
Task: "Create PaddingRegion record in DotnetMcp/Models/Memory/PaddingRegion.cs"
Task: "Create TypeLayout record in DotnetMcp/Models/Memory/TypeLayout.cs"
```

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all US1 tests together:
Task: "Contract test for object_inspect schema in tests/DotnetMcp.Tests/Contract/ObjectInspectContractTests.cs"
Task: "Unit test for InspectObject in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1 (Inspect Heap Object)
4. **STOP and VALIDATE**: Test object_inspect independently
5. Deploy/demo if ready - AI can now inspect objects!

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy (MVP! Object inspection)
3. Add User Story 2 → Test independently → Deploy (Raw memory reading)
4. Add User Story 3 → Test independently → Deploy (Reference analysis)
5. Add User Story 4 → Test independently → Deploy (Memory layout)
6. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:
1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (Object Inspection)
   - Developer B: User Story 2 (Memory Read)
   - Developer C: User Story 3 (References)
   - Developer D: User Story 4 (Layout)
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- All memory operations require paused debug session (handled by existing infrastructure)
