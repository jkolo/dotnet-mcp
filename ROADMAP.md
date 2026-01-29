# debug-mcp.net Roadmap

## Completed Features

| # | Feature | Description |
|---|---------|-------------|
| 001 | Debug Session | Launch, attach, disconnect, state query |
| 002 | Breakpoint Operations | Set/remove/enable/list breakpoints, exception breakpoints, wait for hit |
| 003 | Inspection Operations | Threads, stack traces, variables, expression evaluation |
| 004 | Memory Operations | Raw memory read, object inspect, references, type layout |
| 005 | Module Operations | List modules, browse types, get members, search |
| 006 | Debugger Bugfixes | Fixed ICorDebug interaction bugs |
| 007 | Debug Launch | Launch with env, cwd, args, stopAtEntry |
| 008 | Reqnroll E2E Tests | BDD end-to-end tests with Reqnroll/Gherkin |

## Proposed Features

### High Value (AI agent productivity)

#### 009 - Heap Snapshot / Diff
Capture heap object snapshots and compare two snapshots to find leaked or growing objects. Enables the agent to autonomously diagnose memory leaks: set breakpoint, snapshot, continue, snapshot, diff.

#### 010 - Logpoints / Tracepoints
Breakpoints that log a message instead of pausing execution. The agent can instrument running code without stopping the process, enabling much faster diagnostics for tracing control flow and variable values.

#### 011 - Thread Management
Freeze/thaw individual threads and set the active thread for inspection. Currently the agent can list threads but cannot control them, making race condition debugging difficult.

#### 012 - Edit and Continue (EnC)
Modify code while the process is paused and resume with the changes applied. The agent could fix a bug and verify the fix without restarting the process.

### Medium Value

#### 013 - Dump File Analysis
Load and analyze `.dmp` crash dump files offline. Enables post-mortem debugging of production crashes without a live process.

#### 014 - Symbol Server Integration
Automatically download PDB files from symbol servers (NuGet, Microsoft). Without this, the agent cannot set breakpoints or inspect source in third-party library code.

#### 015 - GC / Runtime Events
Subscribe to runtime events (GC collections, JIT compilations, exceptions thrown). The agent can observe runtime behavior without breakpoints, useful for performance diagnostics.

### Lower Priority

#### 016 - DAP Compatibility Layer
Debug Adapter Protocol adapter for IDE integration (VS Code, JetBrains). Exposes debug-mcp capabilities through the standard DAP interface.

#### 017 - Remote Debugging
TCP/network transport for debugging processes on remote machines or containers. Extends the architecture beyond local stdio.

#### 018 - Code Coverage
Track which lines/branches execute during a debug session. Useful for understanding test coverage or identifying dead code paths during debugging.
