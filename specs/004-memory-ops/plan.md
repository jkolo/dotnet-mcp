# Implementation Plan: Memory Inspection Operations

**Branch**: `004-memory-ops` | **Date**: 2026-01-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-memory-ops/spec.md`
**Depends On**: 001-debug-session (requires active debug session), 003-inspection-ops (requires variable inspection infrastructure)

## Summary

Implement MCP tools for memory inspection during debugging: `object_inspect`, `memory_read`,
`references_get`, and `layout_get`. These tools enable AI assistants to examine heap objects,
read raw memory, analyze object reference graphs, and understand memory layout. Implementation
uses ICorDebug APIs directly (ICorDebugValue hierarchy, ICorDebugProcess.ReadMemory,
ICorDebugObjectValue) building on the existing ProcessDebugger and variable inspection
infrastructure from 003-inspection-ops.

## Technical Context

**Language/Version**: C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK
**Primary Dependencies**: ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for metadata),
  ModelContextProtocol SDK (existing)
**Storage**: N/A (in-memory state within debug session)
**Testing**: xUnit, Moq, FluentAssertions (consistent with existing features)
**Target Platform**: Windows, Linux, macOS (cross-platform via .NET)
**Project Type**: Single project (extends existing DotnetMcp)
**Performance Goals**: Object inspection <2s (SC-001), memory read <1s (SC-002), layout <1s (SC-004)
**Constraints**: Requires paused state, valid object references, memory access limits (64KB)
**Scale/Scope**: Supports typical debugging scenarios (complex objects, nested structures)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Requirement | Status |
|-----------|-------------|--------|
| I. Native First | Use ICorDebug APIs directly for memory/object inspection | ✅ PASS - Using ICorDebugValue, ICorDebugObjectValue, ICorDebugProcess.ReadMemory |
| II. MCP Compliance | `noun_verb` naming, JSON Schema params, structured responses | ✅ PASS - Tools: object_inspect, memory_read, references_get, layout_get |
| III. Test-First | TDD mandatory, integration tests for memory workflows | ✅ PASS - Test plan includes contract + integration tests |
| IV. Simplicity | Max 3 indirection levels, YAGNI | ✅ PASS - Direct ICorDebug usage, builds on existing infrastructure |
| V. Observability | Structured logging, memory operations logged | ✅ PASS - Logging requirements included |

**MCP Tool Standards Check**:
- Naming: `object_inspect`, `memory_read`, `references_get`, `layout_get` ✅
- Parameters: object_ref as string, address as long, size as int, depth as int with defaults ✅
- Responses: Structured JSON with fields, bytes, references, or error objects ✅

## Project Structure

### Documentation (this feature)

```text
specs/004-memory-ops/
├── plan.md              # This file
├── research.md          # Phase 0: ICorDebug memory APIs
├── data-model.md        # Phase 1: ObjectInspection, MemoryRegion entities
├── quickstart.md        # Phase 1: Usage examples
├── contracts/           # Phase 1: MCP tool schemas
└── tasks.md             # Phase 2: Implementation tasks (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DotnetMcp/
├── Program.cs           # MCP server entry point (existing)
├── Tools/               # MCP tool implementations
│   ├── [existing tools from 001-003]
│   ├── ObjectInspectTool.cs      # NEW - inspect heap object
│   ├── MemoryReadTool.cs         # NEW - read raw memory
│   ├── ReferencesGetTool.cs      # NEW - analyze references
│   └── LayoutGetTool.cs          # NEW - get memory layout
├── Services/            # Core debugging services
│   ├── ProcessDebugger.cs        # (existing - extended with memory methods)
│   │                             # Contains: InspectObject(), ReadMemory(),
│   │                             #           GetReferences(), GetTypeLayout()
│   ├── IProcessDebugger.cs       # (existing - extended interface)
│   └── [existing services]
├── Models/              # Domain models
│   ├── [existing models]
│   └── Memory/                   # NEW - memory inspection models
│       ├── ObjectInspection.cs   # Object inspection result
│       ├── FieldDetail.cs        # Field with offset, size, value
│       ├── MemoryRegion.cs       # Raw memory bytes
│       ├── ReferenceInfo.cs      # Object reference relationship
│       └── TypeLayout.cs         # Type memory layout
└── Infrastructure/      # Cross-cutting concerns
    └── Logging.cs                # (existing - extended with memory events)

tests/DotnetMcp.Tests/
├── Contract/            # MCP schema validation tests
│   ├── ObjectInspectContractTests.cs    # NEW
│   ├── MemoryReadContractTests.cs       # NEW
│   ├── ReferencesGetContractTests.cs    # NEW
│   └── LayoutGetContractTests.cs        # NEW
├── Integration/         # End-to-end debugging tests
│   ├── ObjectInspectionTests.cs         # NEW
│   ├── MemoryReadTests.cs               # NEW
│   ├── ReferenceAnalysisTests.cs        # NEW
│   └── LayoutInspectionTests.cs         # NEW
├── Performance/         # Performance validation tests
│   └── MemoryPerformanceTests.cs        # NEW - SC-001/002/004 tests
└── Unit/                # ProcessDebugger unit tests
    ├── ObjectInspectorTests.cs          # NEW
    ├── MemoryReaderTests.cs             # NEW
    └── LayoutAnalyzerTests.cs           # NEW
```

**Structure Decision**: Extends existing DotnetMcp project structure. Memory inspection
operations are implemented directly in ProcessDebugger as methods (InspectObject,
ReadMemory, GetReferences, GetTypeLayout) following the same pattern as 003-inspection-ops.
This builds on the existing ICorDebugValue handling infrastructure and variable inspection code.

## Complexity Tracking

> No violations to justify - design follows Simplicity principle and reuses existing infrastructure.
