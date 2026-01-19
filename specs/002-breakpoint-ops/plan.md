# Implementation Plan: Breakpoint Operations

**Branch**: `002-breakpoint-ops` | **Date**: 2026-01-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-breakpoint-ops/spec.md`
**Depends On**: 001-debug-session (requires active debug session)

## Summary

Implement MCP tools for breakpoint operations: `breakpoint_set`, `breakpoint_remove`,
`breakpoint_list`, `breakpoint_wait`, `breakpoint_set_exception`. These tools enable
AI assistants to set breakpoints at source locations, wait for breakpoints to be hit,
manage breakpoint lifecycle, and handle conditional/exception breakpoints. Implementation
uses ICorDebug APIs directly (ICorDebugBreakpoint, ICorDebugFunctionBreakpoint) with
PDB symbol resolution for source-to-IL mapping.

## Technical Context

**Language/Version**: C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK,
**Primary Dependencies**: ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for PDB parsing),
  ModelContextProtocol SDK
**Storage**: N/A (in-memory breakpoint registry within session)
**Testing**: xUnit, Moq, FluentAssertions (consistent with 001-debug-session)
**Target Platform**: Windows, Linux, macOS (cross-platform via .NET)
**Project Type**: Single project (extends existing DotnetMcp)
**Performance Goals**: Breakpoint set verified within 2s (SC-001), wait returns within 100ms of hit (SC-002)
**Constraints**: Requires active debug session from 001-debug-session, PDB symbols required for source mapping
**Scale/Scope**: Supports typical debugging scenarios (100s of breakpoints per session)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Requirement | Status |
|-----------|-------------|--------|
| I. Native First | Use ICorDebug breakpoint APIs directly | ✅ PASS - Using ICorDebugBreakpoint, ICorDebugFunctionBreakpoint |
| II. MCP Compliance | `noun_verb` naming, JSON Schema params, structured responses | ✅ PASS - Tools: breakpoint_set, breakpoint_remove, etc. |
| III. Test-First | TDD mandatory, integration tests for breakpoint lifecycle | ✅ PASS - Test plan includes contract + integration tests |
| IV. Simplicity | Max 3 indirection levels, YAGNI | ✅ PASS - Direct ICorDebug usage, no abstractions |
| V. Observability | Structured logging, breakpoint events logged | ✅ PASS - Logging requirements included |

**MCP Tool Standards Check**:
- Naming: `breakpoint_set`, `breakpoint_remove`, `breakpoint_list`, `breakpoint_wait`, `breakpoint_set_exception` ✅
- Parameters: file/line/column, breakpoint ID as string, timeout optional with 30s default ✅
- Responses: Structured JSON with breakpoint details, hit info, or error objects ✅

## Project Structure

### Documentation (this feature)

```text
specs/002-breakpoint-ops/
├── plan.md              # This file
├── research.md          # Phase 0: ICorDebug breakpoint APIs, PDB sequence points
├── data-model.md        # Phase 1: Breakpoint, BreakpointHit entities
├── quickstart.md        # Phase 1: Usage examples
├── contracts/           # Phase 1: MCP tool schemas
└── tasks.md             # Phase 2: Implementation tasks (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DotnetMcp/
├── Program.cs           # MCP server entry point (existing)
├── Tools/               # MCP tool implementations
│   ├── DebugAttachTool.cs      # (existing from 001)
│   ├── DebugLaunchTool.cs      # (existing from 001)
│   ├── DebugDisconnectTool.cs  # (existing from 001)
│   ├── DebugStateTool.cs       # (existing from 001)
│   ├── BreakpointSetTool.cs    # NEW
│   ├── BreakpointRemoveTool.cs # NEW
│   ├── BreakpointListTool.cs   # NEW
│   ├── BreakpointWaitTool.cs   # NEW
│   └── BreakpointSetExceptionTool.cs # NEW
├── Services/            # Core debugging services
│   ├── DebugSessionManager.cs  # (existing - may need extension)
│   ├── ProcessDebugger.cs      # (existing - may need extension)
│   ├── IBreakpointManager.cs   # NEW - interface
│   ├── BreakpointManager.cs    # NEW - breakpoint registry & operations
│   └── SymbolResolver.cs       # NEW - PDB/source mapping
├── Models/              # Domain models
│   ├── DebugSession.cs         # (existing)
│   ├── SessionState.cs         # (existing)
│   ├── ProcessInfo.cs          # (existing)
│   ├── Breakpoint.cs           # NEW
│   ├── BreakpointHit.cs        # NEW
│   ├── BreakpointLocation.cs   # NEW
│   ├── BreakpointType.cs       # NEW (enum: Source, Exception)
│   └── BreakpointState.cs      # NEW (enum: Pending, Verified, Invalid)
└── Infrastructure/      # Cross-cutting concerns
    └── Logging.cs              # (existing - extend with breakpoint events)

tests/DotnetMcp.Tests/
├── Contract/            # MCP schema validation tests
│   ├── DebugAttachContractTests.cs  # (existing)
│   ├── BreakpointSetContractTests.cs    # NEW
│   ├── BreakpointRemoveContractTests.cs # NEW
│   ├── BreakpointListContractTests.cs   # NEW
│   ├── BreakpointWaitContractTests.cs   # NEW
│   └── BreakpointSetExceptionContractTests.cs # NEW
├── Integration/         # End-to-end debugging tests
│   ├── AttachTests.cs          # (existing)
│   ├── BreakpointSetTests.cs   # NEW
│   ├── BreakpointHitTests.cs   # NEW
│   ├── BreakpointLifecycleTests.cs # NEW
│   └── ConditionalBreakpointTests.cs # NEW
└── Unit/                # Service unit tests
    ├── DebugSessionManagerTests.cs  # (existing)
    ├── BreakpointManagerTests.cs    # NEW
    └── SymbolResolverTests.cs       # NEW
```

**Structure Decision**: Extends existing DotnetMcp project structure from 001-debug-session.
New breakpoint-related tools in Tools/, new BreakpointManager service for breakpoint
registry and ICorDebug breakpoint operations, new SymbolResolver for PDB sequence point
resolution. Models extended with Breakpoint-related entities.

## Complexity Tracking

> No violations to justify - design follows Simplicity principle.
> BreakpointManager adds one service but is necessary for breakpoint state management.
> SymbolResolver adds one service but is necessary for source-to-IL mapping.
