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
│   ├── DebugPauseTool.cs          # NEW - pause execution
│   ├── ThreadsListTool.cs         # NEW - list threads
│   ├── StacktraceGetTool.cs       # NEW - get stack trace
│   ├── VariablesGetTool.cs        # NEW - inspect variables
│   └── EvaluateTool.cs            # NEW - evaluate expressions
├── Services/            # Core debugging services
│   ├── DebugSessionManager.cs     # (existing - extended with pause)
│   ├── ProcessDebugger.cs         # (existing - extended with inspection methods)
│   │                              # Contains: GetThreads(), GetStackFrames(),
│   │                              #           GetVariables(), PauseAsync(),
│   │                              #           EvaluateAsync()
│   ├── IProcessDebugger.cs        # (existing - extended interface)
│   ├── Breakpoints/               # (existing from 002)
│   └── Inspection/                # Reserved for future extraction if needed
├── Models/              # Domain models
│   ├── DebugSession.cs            # (existing)
│   ├── SessionState.cs            # (existing)
│   ├── ProcessInfo.cs             # (existing)
│   ├── SourceLocation.cs          # (existing)
│   ├── Breakpoints/               # (existing from 002)
│   └── Inspection/                # NEW - inspection models
│       ├── ThreadInfo.cs          # Thread information record
│       ├── ThreadState.cs         # ThreadState enum
│       ├── StackFrame.cs          # Stack frame record
│       ├── Variable.cs            # Variable record
│       ├── VariableScope.cs       # VariableScope enum
│       ├── EvaluationResult.cs    # Expression result record
│       └── EvaluationError.cs     # Evaluation error record
└── Infrastructure/      # Cross-cutting concerns
    └── Logging.cs                 # (existing - extended with inspection events)

tests/DotnetMcp.Tests/
├── Contract/            # MCP schema validation tests
│   ├── DebugPauseContractTests.cs        # NEW
│   ├── ThreadsListContractTests.cs       # NEW
│   ├── StacktraceGetContractTests.cs     # NEW
│   ├── VariablesGetContractTests.cs      # NEW
│   └── EvaluateContractTests.cs          # NEW
├── Integration/         # End-to-end debugging tests (pending)
│   ├── StackInspectionTests.cs           # Pending
│   ├── VariableInspectionTests.cs        # Pending
│   ├── ThreadInspectionTests.cs          # Pending
│   ├── ExpressionEvaluationTests.cs      # Pending
│   └── PauseTests.cs                     # Pending
├── Performance/         # Performance validation tests
│   └── InspectionPerformanceTests.cs     # NEW - SC-001/002/004 tests
└── Unit/                # ProcessDebugger unit tests (pending)
    ├── StackWalkerTests.cs               # Pending
    ├── VariableInspectorTests.cs         # Pending
    ├── ThreadInspectorTests.cs           # Pending
    └── ExpressionEvaluatorTests.cs       # Pending
```

**Structure Decision**: Extends existing DotnetMcp project structure. All inspection
operations are implemented directly in ProcessDebugger as methods (GetThreads,
GetStackFrames, GetVariables, PauseAsync, EvaluateAsync) rather than separate service
classes. This design follows the Simplicity principle by avoiding unnecessary abstraction
layers - ProcessDebugger already has access to ICorDebug handles and the inspection
methods naturally belong with the core debugging logic. The Services/Inspection/
directory is reserved for potential future extraction if ProcessDebugger grows too large.

## Complexity Tracking

> No violations to justify - design follows Simplicity principle.
> Initial plan proposed four separate services in Inspection/ (ThreadInspector, StackWalker,
> VariableInspector, ExpressionEvaluator). During implementation, a simpler approach was chosen:
> all inspection methods are implemented directly in ProcessDebugger. This avoids:
> - Additional abstraction layers
> - Passing ICorDebug handles between services
> - Service interface ceremony for internal-only operations
> ProcessDebugger already manages the debugging lifecycle and has all necessary context.
> If ProcessDebugger grows too large, methods can be extracted to separate services later.
