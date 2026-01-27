# Test Contract: Reattachment (Bug #3)

**Priority**: P1 (Highest)
**Feature**: Fix Debugger Bugs
**Related Requirement**: FR-001, FR-002, FR-003, FR-004

## Contract Overview

Verify that the debugger can attach to, disconnect from, and reattach to processes without requiring MCP server restart.

## Preconditions

- MCP server is running
- At least one .NET process is available for debugging
- Test target application (`DebugTestApp`) is built and runnable

## Test Scenarios

### T001: Basic Reattachment Cycle

**Given** MCP server is running and `DebugTestApp` is started
**When** I attach, disconnect, then attach again
**Then** both attach operations succeed

```json
// Step 1: First attach
{
  "tool": "debug_attach",
  "input": { "pid": "$PID_A" }
}
// Expected: { "success": true, "attached_pid": "$PID_A" }

// Step 2: Disconnect
{
  "tool": "debug_disconnect"
}
// Expected: { "success": true }

// Step 3: Second attach (same or different process)
{
  "tool": "debug_attach",
  "input": { "pid": "$PID_B" }
}
// Expected: { "success": true, "attached_pid": "$PID_B" }
```

### T002: Multiple Cycle Stress Test

**Given** MCP server is running
**When** I perform 10 attach/disconnect cycles
**Then** all cycles complete successfully

```
FOR i = 1 TO 10:
    1. Start DebugTestApp process
    2. debug_attach(pid)  → expect success
    3. debug_pause()      → expect success
    4. debug_disconnect() → expect success
    5. Stop DebugTestApp process
```

### T003: Reattach After Process Termination

**Given** debugger is attached to a process
**When** the process terminates unexpectedly
**And** I attempt to attach to a new process
**Then** attachment succeeds

```json
// Step 1: Attach
{ "tool": "debug_attach", "input": { "pid": "$PID_A" } }

// Step 2: Process terminates (kill externally)
// (Process exits)

// Step 3: Attach to new process
{ "tool": "debug_attach", "input": { "pid": "$PID_B" } }
// Expected: { "success": true }
```

### T004: Disconnect While Operation In Progress

**Given** debugger is attached and an evaluation is running
**When** disconnect is called
**Then** disconnect completes gracefully without crash

## Error Scenarios

### E001: Attach While Already Attached

**Given** debugger is attached to process A
**When** I try to attach to process B without disconnecting
**Then** clear error is returned

```json
{ "tool": "debug_attach", "input": { "pid": "$PID_B" } }
// Expected: { "success": false, "error": "Already attached to process..." }
```

## Acceptance Criteria

- [ ] T001: Basic reattachment works
- [ ] T002: 10 consecutive cycles complete without failure
- [ ] T003: Recovery after process termination works
- [ ] T004: Graceful disconnect during operations
- [ ] E001: Proper error when already attached

## Implementation Notes

The fix involves terminating `_corDebug` in `DetachAsync`:

```csharp
// In DetachAsync, after _process = null:
if (_corDebug != null)
{
    try { _corDebug.Terminate(); } catch { }
    _corDebug = null;
}
```
