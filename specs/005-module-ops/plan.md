# Implementation Plan: Module and Assembly Inspection

**Branch**: `005-module-ops` | **Date**: 2026-01-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-module-ops/spec.md`
**Depends On**: 001-debug-session (requires active debug session)

## Summary

Implement MCP tools for module and assembly inspection during debugging: `modules_list`,
`types_get`, `members_get`, and `modules_search`. These tools enable AI assistants to
explore loaded modules, browse types by namespace, inspect type members, and search across
modules. Implementation uses ICorDebug APIs (ICorDebugAppDomain.EnumerateAssemblies,
ICorDebugAssembly, ICorDebugModule) combined with System.Reflection.Metadata for type/member
enumeration. Unlike inspection operations, module queries work with both running and paused
debug sessions.

## Technical Context

**Language/Version**: C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK
**Primary Dependencies**: ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for metadata reading),
  ModelContextProtocol SDK (existing)
**Storage**: N/A (in-memory, reads module metadata on demand)
**Testing**: xUnit, Moq, FluentAssertions (consistent with existing features)
**Target Platform**: Windows, Linux, macOS (cross-platform via .NET)
**Project Type**: Single project (extends existing DotnetMcp)
**Performance Goals**: Module list <1s (SC-001), type browse <2s (SC-002), member inspect <500ms (SC-003), search <3s (SC-004)
**Constraints**: Requires active debug session (not necessarily paused), result limits for large modules
**Scale/Scope**: Supports modules with 1000+ types, applications with 100+ loaded assemblies

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Requirement | Status |
|-----------|-------------|--------|
| I. Native First | Use ICorDebug APIs directly for module enumeration | ✅ PASS - Using ICorDebugAppDomain, ICorDebugAssembly, ICorDebugModule |
| II. MCP Compliance | `noun_verb` naming, JSON Schema params, structured responses | ✅ PASS - Tools: modules_list, types_get, members_get, modules_search |
| III. Test-First | TDD mandatory, integration tests for module workflows | ✅ PASS - Test plan includes contract + integration tests |
| IV. Simplicity | Max 3 indirection levels, YAGNI | ✅ PASS - Direct metadata access, reuses existing session infrastructure |
| V. Observability | Structured logging, module operations logged | ✅ PASS - Logging requirements included |

**MCP Tool Standards Check**:
- Naming: `modules_list`, `types_get`, `members_get`, `modules_search` ✅
- Parameters: module_name as string, type_name as string, pattern as string with defaults ✅
- Responses: Structured JSON with modules, types, members, or error objects ✅

## Project Structure

### Documentation (this feature)

```text
specs/005-module-ops/
├── plan.md              # This file
├── research.md          # Phase 0: ICorDebug module/metadata APIs
├── data-model.md        # Phase 1: ModuleInfo, TypeInfo entities
├── quickstart.md        # Phase 1: Usage examples
├── contracts/           # Phase 1: MCP tool schemas
└── tasks.md             # Phase 2: Implementation tasks (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DotnetMcp/
├── Program.cs           # MCP server entry point (existing)
├── Tools/               # MCP tool implementations
│   ├── [existing tools from 001-004]
│   ├── ModulesListTool.cs        # NEW - list loaded modules
│   ├── TypesGetTool.cs           # NEW - browse types in module
│   ├── MembersGetTool.cs         # NEW - inspect type members
│   └── ModulesSearchTool.cs      # NEW - search across modules
├── Services/            # Core debugging services
│   ├── ProcessDebugger.cs        # (existing - extended with module methods)
│   │                             # Contains: GetModules(), GetTypes(),
│   │                             #           GetMembers(), SearchModules()
│   ├── IProcessDebugger.cs       # (existing - extended interface)
│   └── [existing services]
├── Models/              # Domain models
│   ├── [existing models]
│   └── Modules/                  # NEW - module inspection models
│       ├── ModuleInfo.cs         # Module with name, version, path, symbols
│       ├── TypeInfo.cs           # Type with name, kind, visibility
│       ├── MethodInfo.cs         # Method signature
│       ├── PropertyInfo.cs       # Property info
│       ├── FieldInfo.cs          # Field info
│       └── SearchResult.cs       # Search result with matches
└── Infrastructure/      # Cross-cutting concerns
    └── Logging.cs                # (existing - extended with module events)

tests/DotnetMcp.Tests/
├── Contract/            # MCP schema validation tests
│   ├── ModulesListContractTests.cs     # NEW
│   ├── TypesGetContractTests.cs        # NEW
│   ├── MembersGetContractTests.cs      # NEW
│   └── ModulesSearchContractTests.cs   # NEW
├── Integration/         # End-to-end debugging tests
│   ├── ModuleListTests.cs              # NEW
│   ├── TypeBrowsingTests.cs            # NEW
│   ├── MemberInspectionTests.cs        # NEW
│   └── ModuleSearchTests.cs            # NEW
├── Performance/         # Performance validation tests
│   └── ModulePerformanceTests.cs       # NEW - SC-001/002/003/004 tests
└── Unit/                # ProcessDebugger unit tests
    └── ModuleInspectorTests.cs         # NEW
```

**Structure Decision**: Extends existing DotnetMcp project structure. Module inspection
operations are implemented directly in ProcessDebugger as methods (GetModules, GetTypes,
GetMembers, SearchModules) following the established pattern. Uses ICorDebugAssembly for
module enumeration and System.Reflection.Metadata for detailed type/member information.

## Complexity Tracking

> No violations to justify - design follows Simplicity principle and reuses existing infrastructure.
