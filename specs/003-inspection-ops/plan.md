# Implementation Plan: Inspection Operations

**Branch**: `003-inspection-ops` | **Date**: 2026-01-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-inspection-ops/spec.md`
**Depends On**: 001-debug-session (requires active debug session), 002-breakpoint-ops (requires paused state)

## Summary

Implement MCP tools for runtime inspection during debugging: `stacktrace_get`, `variables_get`,
`threads_list`, `evaluate`, and `debug_pause`. These tools enable AI assistants to examine
program state when paused: navigate call stacks, inspect variable values, understand thread
activity, and evaluate expressions. Implementation uses ICorDebug APIs directly (ICorDebugThread,
ICorDebugFrame, ICorDebugValue, ICorDebugEval) building on the existing ProcessDebugger
infrastructure.

## Technical Context

**Language/Version**: C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK
**Primary Dependencies**: ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for PDB parsing),
  ModelContextProtocol SDK (existing)
**Storage**: N/A (in-memory state within debug session)
**Testing**: xUnit, Moq, FluentAssertions (consistent with 001/002 features)
**Target Platform**: Windows, Linux, macOS (cross-platform via .NET)
**Project Type**: Single project (extends existing DotnetMcp)
**Performance Goals**: Stack trace <500ms (SC-001), variables <1s (SC-002), thread list <200ms (SC-004)
**Constraints**: Requires paused state for stack/variables, active session required
**Scale/Scope**: Supports typical debugging scenarios (deep stacks, complex objects)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Requirement | Status |
|-----------|-------------|--------|
| I. Native First | Use ICorDebug APIs directly for thread/frame/value inspection | ✅ PASS - Using ICorDebugThread, ICorDebugFrame, ICorDebugValue, ICorDebugEval |
| II. MCP Compliance | `noun_verb` naming, JSON Schema params, structured responses | ✅ PASS - Tools: stacktrace_get, variables_get, threads_list, evaluate, debug_pause |
| III. Test-First | TDD mandatory, integration tests for inspection workflows | ✅ PASS - Test plan includes contract + integration tests |
| IV. Simplicity | Max 3 indirection levels, YAGNI | ✅ PASS - Direct ICorDebug usage, minimal abstractions |
| V. Observability | Structured logging, inspection operations logged | ✅ PASS - Logging requirements included |

**MCP Tool Standards Check**:
- Naming: `stacktrace_get`, `variables_get`, `threads_list`, `evaluate`, `debug_pause` ✅
- Parameters: thread_id as int, frame_index as int, expression as string, timeout optional with 30s default ✅
- Responses: Structured JSON with frames, variables, threads, or error objects ✅

## Project Structure

### Documentation (this feature)

```text
specs/003-inspection-ops/
├── plan.md              # This file
├── research.md          # Phase 0: ICorDebug inspection APIs
├── data-model.md        # Phase 1: Thread, StackFrame, Variable entities
├── quickstart.md        # Phase 1: Usage examples
├── contracts/           # Phase 1: MCP tool schemas
└── tasks.md             # Phase 2: Implementation tasks (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DotnetMcp/
├── Program.cs           # MCP server entry point (existing)
├── Tools/               # MCP tool implementations
│   ├── DebugAttachTool.cs         # (existing from 001)
│   ├── DebugLaunchTool.cs         # (existing from 001)
│   ├── DebugDisconnectTool.cs     # (existing from 001)
│   ├── DebugStateTool.cs          # (existing from 001)
│   ├── DebugContinueTool.cs       # (existing from 002)
│   ├── DebugStepTool.cs           # (existing from 002)
│   ├── BreakpointSetTool.cs       # (existing from 002)
│   ├── BreakpointRemoveTool.cs    # (existing from 002)
│   ├── BreakpointListTool.cs      # (existing from 002)
│   ├── BreakpointWaitTool.cs      # (existing from 002)
│   ├── DebugPauseTool.cs          # NEW
│   ├── ThreadsListTool.cs         # NEW
│   ├── StacktraceGetTool.cs       # NEW
│   ├── VariablesGetTool.cs        # NEW
│   └── EvaluateTool.cs            # NEW
├── Services/            # Core debugging services
│   ├── DebugSessionManager.cs     # (existing - extend with pause)
│   ├── ProcessDebugger.cs         # (existing - extend with thread/frame/value inspection)
│   ├── IProcessDebugger.cs        # (existing - extend interface)
│   ├── Breakpoints/               # (existing from 002)
│   └── Inspection/                # NEW - inspection services
│       ├── IThreadInspector.cs    # NEW - thread enumeration interface
│       ├── ThreadInspector.cs     # NEW - thread enumeration
│       ├── IStackWalker.cs        # NEW - stack frame traversal interface
│       ├── StackWalker.cs         # NEW - stack frame traversal
│       ├── IVariableInspector.cs  # NEW - variable reading interface
│       ├── VariableInspector.cs   # NEW - ICorDebugValue traversal
│       ├── IExpressionEvaluator.cs # NEW - expression evaluation interface
│       └── ExpressionEvaluator.cs # NEW - ICorDebugEval wrapper
├── Models/              # Domain models
│   ├── DebugSession.cs            # (existing)
│   ├── SessionState.cs            # (existing)
│   ├── ProcessInfo.cs             # (existing)
│   ├── SourceLocation.cs          # (existing)
│   ├── Breakpoints/               # (existing from 002)
│   └── Inspection/                # NEW - inspection models
│       ├── ThreadInfo.cs          # NEW
│       ├── ThreadState.cs         # NEW (enum)
│       ├── StackFrame.cs          # NEW
│       ├── Variable.cs            # NEW
│       ├── VariableScope.cs       # NEW (enum)
│       └── EvaluationResult.cs    # NEW
└── Infrastructure/      # Cross-cutting concerns
    └── Logging.cs                 # (existing - extend with inspection events)

tests/DotnetMcp.Tests/
├── Contract/            # MCP schema validation tests
│   ├── DebugPauseContractTests.cs        # NEW
│   ├── ThreadsListContractTests.cs       # NEW
│   ├── StacktraceGetContractTests.cs     # NEW
│   ├── VariablesGetContractTests.cs      # NEW
│   └── EvaluateContractTests.cs          # NEW
├── Integration/         # End-to-end debugging tests
│   ├── ThreadInspectionTests.cs          # NEW
│   ├── StackInspectionTests.cs           # NEW
│   ├── VariableInspectionTests.cs        # NEW
│   └── ExpressionEvaluationTests.cs      # NEW
└── Unit/                # Service unit tests
    ├── ThreadInspectorTests.cs           # NEW
    ├── StackWalkerTests.cs               # NEW
    ├── VariableInspectorTests.cs         # NEW
    └── ExpressionEvaluatorTests.cs       # NEW
```

**Structure Decision**: Extends existing DotnetMcp project structure. New inspection-related
tools in Tools/, new Inspection/ service subdirectory for thread/stack/variable/expression
services, new Inspection/ model subdirectory for inspection entities. Services are split
for single responsibility (ThreadInspector, StackWalker, VariableInspector, ExpressionEvaluator)
rather than one monolithic InspectionManager.

## Complexity Tracking

> No violations to justify - design follows Simplicity principle.
> Four new services in Inspection/ may seem like more than needed, but each handles a distinct
> ICorDebug API surface (threads, frames, values, eval) and keeps complexity manageable.
> Alternative of one InspectionManager would exceed readable size and violate single-responsibility.
