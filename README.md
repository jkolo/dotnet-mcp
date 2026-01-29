# debug-mcp.net

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-Compatible-blue)](https://modelcontextprotocol.io/)
[![License](https://img.shields.io/badge/License-AGPL--3.0-blue)](LICENSE)

**MCP server for .NET debugging** — enable AI agents to debug .NET applications interactively.

## What is debug-mcp?

debug-mcp is a [Model Context Protocol](https://modelcontextprotocol.io/) server that exposes .NET debugging capabilities as structured API tools. It allows AI assistants like Claude, GPT, or Copilot to:

- Attach to running .NET processes
- Set breakpoints and wait for them to trigger
- Step through code line by line
- Inspect variables and evaluate expressions
- Analyze stack traces across threads

Unlike similar tools that use external debuggers via DAP protocol, debug-mcp interfaces **directly with the .NET runtime** using ICorDebug APIs — the same approach used by JetBrains Rider.

## Quick Start

### Run

```bash
# No installation needed (.NET 10+)
dnx debug-mcp

# Or one-shot execution
dotnet tool exec debug-mcp
```

### Install (optional)

```bash
# Global tool
dotnet tool install -g debug-mcp

# Local tool (per-project)
dotnet new tool-manifest   # if not already present
dotnet tool install debug-mcp
```

### Requirements

- .NET 10 SDK or later
- Windows, Linux, or macOS

### Configure with Claude Desktop

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "dotnet-debugger": {
      "command": "dnx",
      "args": ["debug-mcp"]
    }
  }
}
```

### Example Conversation

```
You: Debug my ASP.NET app and find why GetUser returns null

Claude: I'll attach to your application and investigate.
        [Calls debug_attach with PID 12345]
        [Calls breakpoint_set at UserService.cs:42]
        [Calls debug_continue]
        [Calls breakpoint_wait with 30s timeout]

        The breakpoint was hit. Let me check the variables.
        [Calls variables_get for current frame]

        I found the issue: the `userId` parameter is an empty string.
        The bug is in the calling code at line 28 where...
```

## Features

| Category | Tools |
|----------|-------|
| **Session** | `debug_launch`, `debug_attach`, `debug_disconnect`, `debug_state` |
| **Breakpoints** | `breakpoint_set`, `breakpoint_remove`, `breakpoint_list`, `breakpoint_wait` |
| **Execution** | `debug_continue`, `debug_pause`, `debug_step_over`, `debug_step_into`, `debug_step_out` |
| **Inspection** | `threads_list`, `stacktrace_get`, `variables_get`, `evaluate` |

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — System design and components
- [How Debugging Works](docs/DEBUGGER.md) — ICorDebug internals explained
- [MCP Tools Reference](docs/MCP_TOOLS.md) — Complete API documentation
- [Development Guide](docs/DEVELOPMENT.md) — Building, testing, contributing

## Similar Projects

| Project | Language | Approach | .NET Support |
|---------|----------|----------|--------------|
| [mcp-debugger](https://github.com/debugmcp/mcp-debugger) | TypeScript | DAP | Via external debugger |
| [dap-mcp](https://github.com/KashunCheng/dap_mcp) | Python | DAP | Via external debugger |
| [LLDB MCP](https://lldb.llvm.org/use/mcp.html) | C++ | Native | No |
| **debug-mcp** | C# | ICorDebug | Native, direct |

## License

AGPL-3.0 — see [LICENSE](LICENSE) for details.
