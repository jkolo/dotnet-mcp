# DebugMcp Development Guidelines

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
- N/A (in-memory debug session state) (006-fix-debugger-bugs)
- C# / .NET 10.0 + ClrDebug 0.3.4 (ICorDebug wrappers), ModelContextProtocol SDK 0.1.0-preview.13, Microsoft.Diagnostics.DbgShim.linux-x64 9.0.661903 (007-debug-launch)
- C# / .NET 10.0 + Reqnroll 3.3.2, Reqnroll.xUnit, Reqnroll.Tools.MsBuild.Generation, FluentAssertions 8.0.0 (008-reqnroll-e2e-tests)
- N/A (test-only feature) (008-reqnroll-e2e-tests)
- C# / .NET 10.0 + Reqnroll 3.3.3, Reqnroll.xUnit, FluentAssertions 8.0.0, xUnit (009-comprehensive-e2e-coverage)
- C# / .NET 10.0 + ModelContextProtocol SDK 0.1.0-preview.13, ClrDebug 0.3.4, Microsoft.Diagnostics.DbgShim.linux-x64 9.0.661903 (010-dotnet-tool-packaging)
- C# / .NET 10.0 (pinned via `global.json`) + GitHub Actions, `dotnet` CLI, NuGet.org, GitHub Packages (011-ci-cd-pipeline)

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
- 011-ci-cd-pipeline: Added C# / .NET 10.0 (pinned via `global.json`) + GitHub Actions, `dotnet` CLI, NuGet.org, GitHub Packages
- 010-dotnet-tool-packaging: Added C# / .NET 10.0 + ModelContextProtocol SDK 0.1.0-preview.13, ClrDebug 0.3.4, Microsoft.Diagnostics.DbgShim.linux-x64 9.0.661903
- 009-comprehensive-e2e-coverage: Added C# / .NET 10.0 + Reqnroll 3.3.3, Reqnroll.xUnit, FluentAssertions 8.0.0, xUnit


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
