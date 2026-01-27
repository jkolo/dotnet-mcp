# Implementation Plan: Fix Debugger Bugs

**Branch**: `006-fix-debugger-bugs` | **Date**: 2026-01-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-fix-debugger-bugs/spec.md`

## Summary

Fix three bugs in the .NET debugger MCP: (1) reattachment failure after disconnect requiring MCP restart, (2) nested property access not working in `object_inspect`, and (3) member access expressions not recognized in `evaluate`. Root causes identified: stale ICorDebug instance reuse, missing property getter invocation in object inspection, and insufficient base type traversal in property lookup.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK, System.Reflection.Metadata
**Storage**: N/A (in-memory debug session state)
**Testing**: xUnit with integration tests against live .NET processes
**Target Platform**: Linux (primary), Windows, macOS
**Project Type**: Single project (MCP server library)
**Performance Goals**: Property path resolution < 500ms for 5 levels depth
**Constraints**: Must use ICorDebug APIs directly (Native First principle)
**Scale/Scope**: Bug fixes in existing ProcessDebugger service

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Native First | ✅ PASS | All fixes use ICorDebug APIs directly |
| II. MCP Compliance | ✅ PASS | No changes to MCP tool interfaces (internal fixes) |
| III. Test-First | ⚠️ REQUIRED | Tests must be written before implementation |
| IV. Simplicity | ✅ PASS | Fixes are direct solutions to identified root causes |
| V. Observability | ✅ PASS | Existing logging infrastructure will be used |

## Project Structure

### Documentation (this feature)

```text
specs/006-fix-debugger-bugs/
├── plan.md              # This file
├── research.md          # Phase 0 output - root cause analysis
├── data-model.md        # Phase 1 output - affected types
├── quickstart.md        # Phase 1 output - verification steps
├── contracts/           # Phase 1 output - test contracts
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DotnetMcp/
│   ├── Services/
│   │   └── ProcessDebugger.cs  # Primary file for all 3 bug fixes
│   └── Tools/
│       ├── ObjectInspectTool.cs
│       └── EvaluateTool.cs
tests/
├── DotnetMcp.Tests/
│   └── Integration/
│       ├── ReattachmentTests.cs      # New: Bug #3 tests
│       ├── NestedInspectionTests.cs  # New: Bug #1 tests
│       └── ExpressionEvaluationTests.cs  # New: Bug #2 tests
└── DebugTestApp/                     # Existing test target application
```

**Structure Decision**: Single project structure maintained. All fixes are in `ProcessDebugger.cs` with new integration tests added.

## Complexity Tracking

No constitution violations requiring justification.
