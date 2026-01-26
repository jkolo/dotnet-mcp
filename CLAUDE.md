# DotnetMcp Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-17

## Active Technologies
- C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK, (002-breakpoint-ops)
- N/A (in-memory breakpoint registry within session) (002-breakpoint-ops)
- C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK, System.Reflection.Metadata (PDB reading) (002-breakpoint-ops)
- C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK, System.Reflection.Metadata (in-box for PDB reading) (002-breakpoint-ops)
- C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK + ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for PDB parsing), (003-inspection-ops)
- N/A (in-memory state within debug session) (003-inspection-ops)
- C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK + ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for metadata), (004-memory-ops)
- C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK + ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for metadata reading), (005-module-ops)
- N/A (in-memory, reads module metadata on demand) (005-module-ops)

- C# / .NET 10.0 + Microsoft.Diagnostics.Runtime (ClrMD), System.Text.Json, (001-debug-session)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# / .NET 10.0

## Code Style

C# / .NET 10.0: Follow standard conventions

## Recent Changes
- 005-module-ops: Added C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK + ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for metadata reading),
- 004-memory-ops: Added C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK + ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for metadata),
- 003-inspection-ops: Added C# / .NET 10.0 + ClrDebug (ICorDebug wrappers), ModelContextProtocol SDK + ClrDebug (for ICorDebug APIs), System.Reflection.Metadata (for PDB parsing),


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
