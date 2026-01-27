# Quickstart: Fix Debugger Bugs

**Feature Branch**: `006-fix-debugger-bugs`
**Date**: 2026-01-26

## Purpose

This guide helps developers verify that the three bug fixes work correctly.

## Prerequisites

1. .NET 10.0 SDK installed
2. Project built: `dotnet build`
3. Test application available: `tests/DebugTestApp`

## Build and Run

```bash
# Build the project
cd /home/jurek/src/Own/DotnetMcp
dotnet build

# Start test application (in separate terminal)
cd tests/DebugTestApp
dotnet run
# Note the PID printed on startup
```

## Verification Steps

### Bug #3: Reattachment (P1)

**Test Scenario**: Attach → Disconnect → Reattach

```bash
# Using MCP tools (via Claude Code or MCP client)

# 1. First attach
debug_attach(pid: <PID>)
# Expected: success

# 2. Pause and verify
debug_pause()
# Expected: process paused

# 3. Disconnect
debug_disconnect()
# Expected: success

# 4. Reattach (same or new process)
debug_attach(pid: <PID>)
# Expected: success (previously failed with ERROR_INVALID_PARAMETER)
```

**Pass Criteria**: Step 4 succeeds without requiring MCP restart.

---

### Bug #1: Nested Property Inspection (P2)

**Test Scenario**: Inspect nested property path

```bash
# 1. Attach and pause
debug_attach(pid: <PID>)
debug_pause()

# 2. Get to Application.Run() frame
stacktrace_get()
# Find frame with Application context

# 3. Inspect nested property
object_inspect(object_ref: "this._currentUser.HomeAddress", depth: 1)
# Expected: Returns Address object with City, Street fields
# Previously failed: "Invalid reference: could not resolve..."
```

**Pass Criteria**: Nested property path resolves to correct Address object.

---

### Bug #2: Expression Evaluation (P3)

**Test Scenario**: Evaluate member access expression

```bash
# 1. Attach and pause (if not already)
debug_attach(pid: <PID>)
debug_pause()

# 2. Evaluate nested expression
evaluate(expression: "_currentUser.HomeAddress.City")
# Expected: Returns "Seattle" (or actual city value)
# Previously failed: "Unrecognized expression"

# 3. Test inherited property
evaluate(expression: "_currentUser.Id")
# Expected: Returns integer value (inherited from base class)
```

**Pass Criteria**: Member access expressions evaluate correctly.

---

## Running Automated Tests

```bash
# Run all tests
dotnet test

# Run only bug fix tests
dotnet test --filter "Category=BugFix"

# Run specific bug tests
dotnet test --filter "FullyQualifiedName~ReattachmentTests"
dotnet test --filter "FullyQualifiedName~NestedInspectionTests"
dotnet test --filter "FullyQualifiedName~ExpressionEvaluationTests"
```

## Success Criteria Summary

| Bug | Success Indicator |
|-----|-------------------|
| #3 Reattachment | 10 attach/disconnect cycles without failure |
| #1 Nested Inspection | `this._currentUser.HomeAddress` returns Address object |
| #2 Expression Eval | `_currentUser.HomeAddress.City` returns string value |

## Troubleshooting

### "Process not found"
- Ensure DebugTestApp is running
- Verify PID matches running process

### "Cannot evaluate: process is not paused"
- Call `debug_pause()` before inspection/evaluation

### Tests timeout on Linux
- Check ptrace_scope: `cat /proc/sys/kernel/yama/ptrace_scope`
- If value is 1, run: `echo 0 | sudo tee /proc/sys/kernel/yama/ptrace_scope`

### Memory issues after many cycles
- Monitor process memory during stress tests
- Memory should return to baseline after disconnect
