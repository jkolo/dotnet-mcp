# Tasks: Module and Assembly Inspection

**Input**: Design documents from `/specs/005-module-ops/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Depends On**: 001-debug-session (requires active debug session)

**Tests**: Included per Constitution's Test-First (TDD) principle.

**Organization**: Tasks grouped by user story for independent implementation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1, US2, US3, US4)
- File paths use project structure from plan.md

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create module inspection infrastructure directories and base structure

- [x] T001 Create DotnetMcp/Models/Modules/ directory for module inspection models
- [x] T002 [P] Add error codes for module operations to DotnetMcp/Models/ErrorCodes.cs
- [x] T003 [P] Add DI registration for module inspection services in DotnetMcp/Program.cs (if needed)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core module inspection infrastructure required before ANY user story

**CRITICAL**: No user story work can begin until this phase is complete

### Shared Models

- [x] T004 Create TypeKind enum in DotnetMcp/Models/Modules/TypeKind.cs
- [x] T005 [P] Create Visibility enum in DotnetMcp/Models/Modules/Visibility.cs
- [x] T006 [P] Create ModuleInfo record in DotnetMcp/Models/Modules/ModuleInfo.cs
- [x] T007 [P] Create TypeInfo record in DotnetMcp/Models/Modules/TypeInfo.cs
- [x] T008 [P] Create MethodMemberInfo record in DotnetMcp/Models/Modules/MethodMemberInfo.cs
- [x] T009 [P] Create PropertyMemberInfo record in DotnetMcp/Models/Modules/PropertyMemberInfo.cs
- [x] T010 [P] Create FieldMemberInfo record in DotnetMcp/Models/Modules/FieldMemberInfo.cs
- [x] T011 [P] Create EventMemberInfo record in DotnetMcp/Models/Modules/EventMemberInfo.cs
- [x] T012 [P] Create ParameterInfo record in DotnetMcp/Models/Modules/ParameterInfo.cs
- [x] T013 [P] Create TypeMembersResult record in DotnetMcp/Models/Modules/TypeMembersResult.cs
- [x] T014 [P] Create TypesResult record in DotnetMcp/Models/Modules/TypesResult.cs
- [x] T015 [P] Create SearchResult record in DotnetMcp/Models/Modules/SearchResult.cs
- [x] T016 [P] Create NamespaceNode record in DotnetMcp/Models/Modules/NamespaceNode.cs

### ProcessDebugger Extensions

- [x] T017 Add GetModulesAsync() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [x] T018 [P] Add GetTypesAsync() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [x] T019 [P] Add GetMembersAsync() method signature to DotnetMcp/Services/IProcessDebugger.cs
- [x] T020 [P] Add SearchModulesAsync() method signature to DotnetMcp/Services/IProcessDebugger.cs

**Checkpoint**: Foundation ready - user story implementation can begin

---

## Phase 3: User Story 1 - List Loaded Modules (Priority: P1) MVP

**Goal**: AI assistants can list all loaded modules with names, versions, paths, and symbol status

**Independent Test**: Attach to process, invoke modules_list. Tool returns all loaded assemblies with complete info.

### Tests for User Story 1

> **NOTE: Write tests FIRST, ensure they FAIL before implementation**

- [x] T021 [P] [US1] Contract test for modules_list schema in tests/DotnetMcp.Tests/Contract/ModulesListContractTests.cs
- [x] T022 [P] [US1] Unit test for GetModulesAsync in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [x] T023 [US1] Integration test for module listing workflow in tests/DotnetMcp.Tests/Integration/ModuleListTests.cs

### Implementation for User Story 1

- [x] T024 [US1] Implement GetModulesAsync() using ICorDebugAppDomain.EnumerateAssemblies in DotnetMcp/Services/ProcessDebugger.cs
- [x] T025 [US1] Implement assembly version extraction from metadata in ProcessDebugger
- [x] T026 [US1] Implement symbol (PDB) status detection via ICorDebugModule.GetSymbolReader in ProcessDebugger
- [x] T027 [US1] Implement module path extraction (handle in-memory modules) in ProcessDebugger
- [x] T028 [US1] Create modules_list MCP tool in DotnetMcp/Tools/ModulesListTool.cs
- [x] T029 [US1] Add include_system parameter handling for system assembly filtering in ModulesListTool
- [x] T030 [US1] Add name_filter pattern matching support in ModulesListTool
- [x] T031 [US1] Add NO_SESSION error handling in modules_list
- [x] T032 [US1] Add logging for module listing operations in ModulesListTool

**Checkpoint**: User Story 1 complete - can list loaded modules (running or paused)

---

## Phase 4: User Story 2 - Browse Types in Module (Priority: P2)

**Goal**: AI assistants can explore types in a module organized by namespace

**Independent Test**: Attach to process, list modules, select module, invoke types_get. Tool returns types grouped by namespace.

### Tests for User Story 2

- [x] T033 [P] [US2] Contract test for types_get schema in tests/DotnetMcp.Tests/Contract/TypesGetContractTests.cs
- [x] T034 [P] [US2] Unit test for GetTypesAsync in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [x] T035 [US2] Integration test for type browsing workflow in tests/DotnetMcp.Tests/Integration/TypeBrowsingTests.cs

### Implementation for User Story 2

- [x] T036 [US2] Implement GetTypesAsync() using System.Reflection.Metadata.MetadataReader in DotnetMcp/Services/ProcessDebugger.cs
- [x] T037 [US2] Implement type enumeration from PE metadata (TypeDefinitions) in ProcessDebugger
- [x] T038 [US2] Implement TypeKind detection (class/interface/struct/enum/delegate) in ProcessDebugger
- [x] T039 [US2] Implement visibility detection from TypeAttributes in ProcessDebugger
- [x] T040 [US2] Implement generic type parameter extraction in ProcessDebugger
- [x] T041 [US2] Implement namespace hierarchy computation in ProcessDebugger
- [x] T042 [US2] Implement namespace filter pattern matching in ProcessDebugger
- [x] T043 [US2] Implement pagination with continuation tokens in ProcessDebugger
- [x] T044 [US2] Create types_get MCP tool in DotnetMcp/Tools/TypesGetTool.cs
- [x] T045 [US2] Add MODULE_NOT_FOUND error handling in types_get
- [x] T046 [US2] Add logging for type browsing operations in TypesGetTool

**Checkpoint**: User Story 2 complete - can browse types in any module

---

## Phase 5: User Story 3 - Inspect Type Members (Priority: P3)

**Goal**: AI assistants can inspect methods, properties, fields, and events of a type

**Independent Test**: Attach to process, identify type, invoke members_get. Tool returns all members with signatures.

### Tests for User Story 3

- [x] T047 [P] [US3] Contract test for members_get schema in tests/DotnetMcp.Tests/Contract/MembersGetContractTests.cs
- [x] T048 [P] [US3] Unit test for GetMembersAsync in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [x] T049 [US3] Integration test for member inspection workflow in tests/DotnetMcp.Tests/Integration/MemberInspectionTests.cs

### Implementation for User Story 3

- [x] T050 [US3] Implement GetMembersAsync() using MetadataReader method enumeration in DotnetMcp/Services/ProcessDebugger.cs
- [x] T051 [US3] Implement method signature formatting (return type, parameters) in ProcessDebugger
- [x] T052 [US3] Implement parameter extraction with names, types, optional/out/ref in ProcessDebugger
- [x] T053 [US3] Implement property enumeration with getter/setter detection in ProcessDebugger
- [x] T054 [US3] Implement field enumeration with readonly/const detection in ProcessDebugger
- [x] T055 [US3] Implement event enumeration in ProcessDebugger
- [x] T056 [US3] Implement inherited member enumeration (include_inherited flag) in ProcessDebugger
- [x] T057 [US3] Implement generic method parameter extraction in ProcessDebugger
- [x] T058 [US3] Create members_get MCP tool in DotnetMcp/Tools/MembersGetTool.cs
- [x] T059 [US3] Add TYPE_NOT_FOUND error handling in members_get
- [x] T060 [US3] Add member_kinds filtering in MembersGetTool
- [x] T061 [US3] Add visibility filtering in MembersGetTool
- [x] T062 [US3] Add logging for member inspection operations in MembersGetTool

**Checkpoint**: User Story 3 complete - can inspect any type's members

---

## Phase 6: User Story 4 - Search Across Modules (Priority: P4)

**Goal**: AI assistants can search for types/methods by pattern across all modules

**Independent Test**: Attach to process, invoke modules_search with pattern. Tool returns matching types/methods from all modules.

### Tests for User Story 4

- [x] T063 [P] [US4] Contract test for modules_search schema in tests/DotnetMcp.Tests/Contract/ModulesSearchContractTests.cs
- [x] T064 [P] [US4] Unit test for SearchModulesAsync in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs
- [x] T065 [US4] Integration test for search workflow in tests/DotnetMcp.Tests/Integration/ModuleSearchTests.cs

### Implementation for User Story 4

- [x] T066 [US4] Implement SearchModulesAsync() with parallel module scanning in DotnetMcp/Services/ProcessDebugger.cs
- [x] T067 [US4] Implement wildcard pattern matching (*prefix, suffix*, *contains*) in ProcessDebugger
- [x] T068 [US4] Implement case-insensitive matching option in ProcessDebugger
- [x] T069 [US4] Implement type search across all modules in ProcessDebugger
- [x] T070 [US4] Implement method search across all modules in ProcessDebugger
- [x] T071 [US4] Implement result limiting (max 100) with truncation indicator in ProcessDebugger
- [x] T072 [US4] Implement module filter pattern matching in ProcessDebugger
- [x] T073 [US4] Create modules_search MCP tool in DotnetMcp/Tools/ModulesSearchTool.cs
- [x] T074 [US4] Add INVALID_PATTERN error handling in modules_search
- [x] T075 [US4] Add logging for search operations in ModulesSearchTool

**Checkpoint**: User Story 4 complete - can search types/methods across all modules

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Performance validation and documentation updates

- [x] T076 [P] Performance test for module list <1s (SC-001) in tests/DotnetMcp.Tests/Performance/ModulePerformanceTests.cs
- [x] T077 [P] Performance test for type browse <2s (SC-002) in tests/DotnetMcp.Tests/Performance/ModulePerformanceTests.cs
- [x] T078 [P] Performance test for member inspect <500ms (SC-003) in tests/DotnetMcp.Tests/Performance/ModulePerformanceTests.cs
- [x] T079 [P] Performance test for search <3s (SC-004) in tests/DotnetMcp.Tests/Performance/ModulePerformanceTests.cs
- [x] T080 Update docs/MCP_TOOLS.md with modules_list documentation
- [x] T081 [P] Update docs/MCP_TOOLS.md with types_get documentation
- [x] T082 [P] Update docs/MCP_TOOLS.md with members_get documentation
- [x] T083 [P] Update docs/MCP_TOOLS.md with modules_search documentation
- [x] T084 Run quickstart.md validation scenarios

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
- **User Story 2 (P2)**: Can start after Foundational - Uses GetModulesAsync from US1 for module lookup
- **User Story 3 (P3)**: Can start after Foundational - Uses GetTypesAsync from US2 for type lookup
- **User Story 4 (P4)**: Can start after Foundational - Builds on US1+US2+US3 but independently testable

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Interface changes before implementation
- Core implementation before tool wrapper
- Error handling before logging
- Story complete before moving to next priority

### Parallel Opportunities

- All models in Phase 2 can be created in parallel (T005-T016)
- All interface methods can be added in parallel (T017-T020)
- All contract tests within each story can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: Phase 2 Models

```bash
# Launch all models together:
Task: "Create TypeKind enum in DotnetMcp/Models/Modules/TypeKind.cs"
Task: "Create Visibility enum in DotnetMcp/Models/Modules/Visibility.cs"
Task: "Create ModuleInfo record in DotnetMcp/Models/Modules/ModuleInfo.cs"
Task: "Create TypeInfo record in DotnetMcp/Models/Modules/TypeInfo.cs"
Task: "Create MethodMemberInfo record in DotnetMcp/Models/Modules/MethodMemberInfo.cs"
Task: "Create PropertyMemberInfo record in DotnetMcp/Models/Modules/PropertyMemberInfo.cs"
Task: "Create FieldMemberInfo record in DotnetMcp/Models/Modules/FieldMemberInfo.cs"
Task: "Create EventMemberInfo record in DotnetMcp/Models/Modules/EventMemberInfo.cs"
Task: "Create ParameterInfo record in DotnetMcp/Models/Modules/ParameterInfo.cs"
Task: "Create TypeMembersResult record in DotnetMcp/Models/Modules/TypeMembersResult.cs"
Task: "Create TypesResult record in DotnetMcp/Models/Modules/TypesResult.cs"
Task: "Create SearchResult record in DotnetMcp/Models/Modules/SearchResult.cs"
Task: "Create NamespaceNode record in DotnetMcp/Models/Modules/NamespaceNode.cs"
```

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all US1 tests together:
Task: "Contract test for modules_list schema in tests/DotnetMcp.Tests/Contract/ModulesListContractTests.cs"
Task: "Unit test for GetModulesAsync in tests/DotnetMcp.Tests/Unit/ProcessDebuggerTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1 (List Loaded Modules)
4. **STOP and VALIDATE**: Test modules_list independently
5. Deploy/demo if ready - AI can now list loaded modules!

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy (MVP! Module listing)
3. Add User Story 2 → Test independently → Deploy (Type browsing)
4. Add User Story 3 → Test independently → Deploy (Member inspection)
5. Add User Story 4 → Test independently → Deploy (Cross-module search)
6. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:
1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (Module Listing)
   - Developer B: User Story 2 (Type Browsing)
   - Developer C: User Story 3 (Member Inspection)
   - Developer D: User Story 4 (Search)
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Module operations work with RUNNING or PAUSED sessions (unlike inspection ops)
- Use System.Reflection.Metadata (in-box) for PE metadata reading
