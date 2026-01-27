# Tasks: Fix Debugger Bugs

**Input**: Design documents from `/specs/006-fix-debugger-bugs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included per Constitution III (Test-First is NON-NEGOTIABLE)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Source**: `DotnetMcp/Services/ProcessDebugger.cs`
- **Tests**: `tests/DotnetMcp.Tests/Integration/`
- **Test Target**: `tests/DebugTestApp/`

---

## Phase 1: Setup

**Purpose**: Ensure test infrastructure and target application are ready

- [x] T001 Verify DebugTestApp builds and contains required test classes in tests/DebugTestApp/Program.cs
- [x] T002 [P] Verify existing test infrastructure runs in tests/DotnetMcp.Tests/
- [x] T003 [P] Create Integration test folder if not exists at tests/DotnetMcp.Tests/Integration/

---

## Phase 2: Foundational (Shared Helper)

**Purpose**: Create shared utility method used by both US2 and US3

**⚠️ CRITICAL**: US2 and US3 depend on this helper - must complete before those stories

- [x] T004 Add TryGetMemberValueAsync helper method signature to DotnetMcp/Services/ProcessDebugger.cs
- [x] T005 Implement TryGetMemberValueAsync: field lookup in DotnetMcp/Services/ProcessDebugger.cs
- [x] T006 Implement TryGetMemberValueAsync: backing field lookup (`<Name>k__BackingField`) in DotnetMcp/Services/ProcessDebugger.cs
- [x] T007 Implement TryGetMemberValueAsync: property getter fallback in DotnetMcp/Services/ProcessDebugger.cs

**Checkpoint**: Shared helper ready - user story implementation can begin

---

## Phase 3: User Story 1 - Reattach Debugger Without Restart (Priority: P1) 🎯 MVP

**Goal**: Enable attach/disconnect/reattach cycles without MCP server restart

**Independent Test**: Attach to process A, disconnect, attach to process B - both succeed

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation (Constitution III)**

- [x] T008 [US1] Create ReattachmentTests.cs with test class skeleton in tests/DotnetMcp.Tests/Integration/ReattachmentTests.cs
- [x] T009 [US1] Write test BasicReattachmentCycle_ShouldSucceed in tests/DotnetMcp.Tests/Integration/ReattachmentTests.cs
- [x] T010 [US1] Write test MultipleCycles_TenTimes_AllSucceed in tests/DotnetMcp.Tests/Integration/ReattachmentTests.cs
- [x] T011 [US1] Write test ReattachAfterProcessTermination_ShouldSucceed in tests/DotnetMcp.Tests/Integration/ReattachmentTests.cs
- [x] T012 [US1] Run tests and verify they FAIL (Red phase) in tests/DotnetMcp.Tests/Integration/ReattachmentTests.cs

### Implementation for User Story 1

- [x] T013 [US1] Add _corDebug.Terminate() call in DetachAsync after _process = null in DotnetMcp/Services/ProcessDebugger.cs:268
- [x] T014 [US1] Add _corDebug = null assignment after Terminate in DotnetMcp/Services/ProcessDebugger.cs:269
- [x] T015 [US1] Add try/catch around Terminate to handle CORDBG_E_ILLEGAL_SHUTDOWN_ORDER in DotnetMcp/Services/ProcessDebugger.cs
- [x] T016 [US1] Add logging for ICorDebug lifecycle events in DotnetMcp/Services/ProcessDebugger.cs
- [x] T017 [US1] Run tests and verify they PASS (Green phase) in tests/DotnetMcp.Tests/Integration/ReattachmentTests.cs

**Checkpoint**: User Story 1 complete - reattachment works without MCP restart

---

## Phase 4: User Story 2 - Inspect Nested Object Properties (Priority: P2)

**Goal**: Enable `object_inspect` with dot-notation paths like `this._currentUser.HomeAddress`

**Independent Test**: Pause at breakpoint, inspect `this._currentUser.HomeAddress` - returns Address object

**Depends on**: Phase 2 (TryGetMemberValueAsync helper)

### Tests for User Story 2 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation (Constitution III)**

- [x] T018 [US2] Create NestedInspectionTests.cs with test class skeleton in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs
- [x] T019 [P] [US2] Write test SingleLevelFieldAccess_ShouldSucceed in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs
- [x] T020 [P] [US2] Write test TwoLevelPropertyAccess_ShouldReturnAddressObject in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs
- [x] T021 [P] [US2] Write test ThreeLevelAccess_ShouldReturnStringValue in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs
- [x] T022 [P] [US2] Write test NullIntermediate_ShouldReturnClearError in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs
- [x] T023 [P] [US2] Write test InvalidMember_ShouldReturnMemberNotFound in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs
- [x] T024 [US2] Run tests and verify they FAIL (Red phase) in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs

### Implementation for User Story 2

- [x] T025 [US2] Modify ResolveExpressionToValue to use TryGetMemberValueAsync in DotnetMcp/Services/ProcessDebugger.cs:3097-3102
- [x] T026 [US2] Add thread parameter to ResolveExpressionToValue for property getter calls in DotnetMcp/Services/ProcessDebugger.cs:3074
- [x] T027 [US2] Add null check with descriptive error message for intermediate values in DotnetMcp/Services/ProcessDebugger.cs
- [x] T028 [US2] Add member-not-found error with type name in error message in DotnetMcp/Services/ProcessDebugger.cs
- [x] T029 [US2] Update InspectObjectAsync to pass thread to ResolveExpressionToValue in DotnetMcp/Services/ProcessDebugger.cs:3059
- [x] T030 [US2] Run tests and verify they PASS (Green phase) in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs

**Checkpoint**: User Story 2 complete - nested property inspection works ✅

---

## Phase 5: User Story 3 - Evaluate Member Access Expressions (Priority: P3)

**Goal**: Enable `evaluate` with expressions like `_currentUser.HomeAddress.City` including inherited properties

**Independent Test**: Evaluate `_currentUser.Id` (inherited from BaseEntity) - returns value

**Depends on**: Phase 2 (TryGetMemberValueAsync helper)

### Tests for User Story 3 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation (Constitution III)**

- [x] T031 [US3] Create BaseTypeExpressionTests.cs with test class skeleton in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs
- [x] T032 [P] [US3] Write test DirectPropertyAccess_ShouldReturnValue in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs
- [x] T033 [P] [US3] Write test ThisKeywordAccess_ShouldReturnValue in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs
- [x] T034 [P] [US3] Write test BaseTypePropertyAccess_ShouldReturnInheritedValue in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs
- [x] T035 [P] [US3] Write test NestedPropertyChain_ShouldResolveAllLevels in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs
- [x] T036 [P] [US3] Write test NonExistentMember_ShouldReturnError in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs
- [x] T037 [US3] Run tests and verify they FAIL (Red phase) in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs

### Implementation for User Story 3

- [x] T038 [US3] Add base type traversal loop to FindPropertyGetter in DotnetMcp/Services/ProcessDebugger.cs
- [x] T039 [US3] Get base type token via GetTypeDefProps().ptkExtends in DotnetMcp/Services/ProcessDebugger.cs
- [x] T040 [US3] Handle cross-module base types (TypeRef vs TypeDef) in DotnetMcp/Services/ProcessDebugger.cs
- [x] T041 [US3] Add base type traversal to TryGetFieldValue for field lookup in DotnetMcp/Services/ProcessDebugger.cs
- [x] T042 [US3] Run tests and verify they PASS (Green phase) in tests/DotnetMcp.Tests/Integration/BaseTypeExpressionTests.cs

**Checkpoint**: User Story 3 complete - expression evaluation with inherited properties works ✅

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and documentation updates

- [x] T043 [P] Update TestTargetApp with BaseEntity inheritance for testing US3 in tests/TestTargetApp/ObjectTarget.cs
- [x] T044 [P] Add 5-level nesting test case to TestTargetApp in tests/TestTargetApp/ObjectTarget.cs
- [x] T045 Run full test suite and verify all tests pass (14 bug-fix tests: 4 US1 + 5 US2 + 5 US3)
- [x] T046 [P] Run quickstart.md manual verification steps from specs/006-fix-debugger-bugs/quickstart.md
- [x] T047 [P] Update BUGS.md to mark bugs as resolved in BUGS.md
- [x] T048 Memory leak test: 10 attach/disconnect cycles pass (MultipleCycles_TenTimes_AllSucceed)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS US2 and US3
- **User Story 1 (Phase 3)**: Depends on Setup only - can run in parallel with Phase 2
- **User Story 2 (Phase 4)**: Depends on Foundational (Phase 2) completion
- **User Story 3 (Phase 5)**: Depends on Foundational (Phase 2) completion
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

```
Setup (Phase 1)
    ├─► Foundational (Phase 2) ─┬─► US2 (Phase 4)
    │                           └─► US3 (Phase 5)
    └─► US1 (Phase 3) [INDEPENDENT - can run in parallel with Phase 2]
            │
            ▼
        MVP Complete!
```

- **User Story 1 (P1)**: Independent - only needs Setup, can start immediately
- **User Story 2 (P2)**: Needs TryGetMemberValueAsync from Phase 2
- **User Story 3 (P3)**: Needs TryGetMemberValueAsync from Phase 2 + FindPropertyGetter base traversal

### Within Each User Story

1. Tests MUST be written and FAIL before implementation (Constitution III)
2. Implementation in order specified
3. Tests MUST PASS after implementation
4. Story complete before moving to next priority

### Parallel Opportunities

**Phase 1 (Setup)**:
- T002 and T003 can run in parallel

**Phase 3 (US1 Tests)**:
- No parallel - tests must be sequential for clear failure verification

**Phase 4 (US2 Tests)**:
- T019, T020, T021, T022, T023 can all run in parallel (different test methods)

**Phase 5 (US3 Tests)**:
- T032, T033, T034, T035, T036 can all run in parallel (different test methods)

**Phase 6 (Polish)**:
- T043, T044, T046, T047 can run in parallel

---

## Parallel Example: User Story 2 Tests

```bash
# Launch all US2 tests in parallel:
Task: "Write test SingleLevelFieldAccess_ShouldSucceed in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs"
Task: "Write test TwoLevelPropertyAccess_ShouldReturnAddressObject in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs"
Task: "Write test ThreeLevelAccess_ShouldReturnStringValue in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs"
Task: "Write test NullIntermediate_ShouldReturnClearError in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs"
Task: "Write test InvalidMember_ShouldReturnMemberNotFound in tests/DotnetMcp.Tests/Integration/NestedInspectionTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 3: User Story 1 (tests → implementation)
3. **STOP and VALIDATE**: Test reattachment manually
4. Deploy/demo if ready - reattachment now works!

### Incremental Delivery

1. Complete Setup + US1 → MVP (reattachment works)
2. Complete Foundational → Helper ready
3. Add US2 → Nested inspection works
4. Add US3 → Expression evaluation enhanced
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Developer A: Setup → US1 (independent path to MVP)
2. Developer B: Setup → Foundational → US2
3. Developer C: Setup → Foundational → US3 (after B's helper is done)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- All tests follow Red-Green-Refactor per Constitution III
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- US1 can deliver MVP without waiting for US2/US3
