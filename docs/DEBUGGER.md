# How .NET Debugging Works

This document explains the .NET debugging infrastructure that DebugMcp uses.

## Overview

.NET applications run on the Common Language Runtime (CLR). The CLR exposes debugging functionality through a set of COM interfaces collectively known as **ICorDebug**. These interfaces allow external processes (debuggers) to:

- Control process execution (start, stop, step)
- Set breakpoints in managed code
- Inspect the runtime state (threads, stacks, variables)
- Evaluate expressions

## The Debugging Stack

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Debugger (DebugMcp)                            │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                    ClrDebug                                   │  │
│  │            Managed wrappers for COM interfaces                │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                 │
                                 │ P/Invoke / COM Interop
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         dbgshim.dll                                 │
│                   Debugging Shim Library                            │
│  - Locates target CLR runtime                                       │
│  - Creates ICorDebug instance for specific runtime version          │
│  - Entry point: CreateDebuggingInterfaceFromVersion()               │
└─────────────────────────────────────────────────────────────────────┘
                                 │
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         mscordbi.dll                                │
│                  CLR Debugging Interface                            │
│  - Implements ICorDebug* interfaces                                 │
│  - Part of the .NET runtime                                         │
│  - Communicates with runtime via DAC (Data Access Component)        │
└─────────────────────────────────────────────────────────────────────┘
                                 │
                                 │ In-process communication
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Target .NET Process                            │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                        CLR                                    │  │
│  │   JIT Compiler | Garbage Collector | Thread Manager           │  │
│  └──────────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                    User Application                           │  │
│  │                 (managed assemblies)                          │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

## Key ICorDebug Interfaces

### Process & Thread Control

| Interface | Purpose |
|-----------|---------|
| `ICorDebug` | Entry point. Manages debugger initialization |
| `ICorDebugProcess` | Controls a debugged process (continue, stop, terminate) |
| `ICorDebugThread` | Represents a managed thread |
| `ICorDebugController` | Base for process/appdomain control |

### Code Inspection

| Interface | Purpose |
|-----------|---------|
| `ICorDebugModule` | Represents a loaded assembly |
| `ICorDebugAssembly` | Assembly-level information |
| `ICorDebugFunction` | A method in the debuggee |
| `ICorDebugCode` | IL or native code for a function |

### Stack & Variables

| Interface | Purpose |
|-----------|---------|
| `ICorDebugFrame` | A stack frame (base interface) |
| `ICorDebugILFrame` | IL frame with locals and arguments |
| `ICorDebugNativeFrame` | Native (JITted) frame |
| `ICorDebugChain` | Chain of frames (managed/native) |
| `ICorDebugValue` | Runtime value (variable, field, etc.) |
| `ICorDebugObjectValue` | Object instance |
| `ICorDebugStringValue` | String value |
| `ICorDebugArrayValue` | Array value |

### Breakpoints

| Interface | Purpose |
|-----------|---------|
| `ICorDebugBreakpoint` | Base breakpoint interface |
| `ICorDebugFunctionBreakpoint` | Breakpoint at IL offset in function |
| `ICorDebugModuleBreakpoint` | Breakpoint on module load |
| `ICorDebugStepperBreakpoint` | One-shot for stepping |

### Expression Evaluation

| Interface | Purpose |
|-----------|---------|
| `ICorDebugEval` | Execute code in debuggee context |
| `ICorDebugEval2` | Extended evaluation (generics, etc.) |

## Debugging Session Lifecycle

### 1. Initialize Debugger

```csharp
// Load dbgshim and get ICorDebug
var clrDebug = DbgShim.CreateDebuggingInterfaceFromVersion(
    runtimeVersion,    // e.g., "v4.0.30319" or ".NET 8.0"
    runtimePath        // Path to runtime directory
);

// Initialize
clrDebug.Initialize();

// Set callback handler
clrDebug.SetManagedHandler(new DebugEventHandler());
```

### 2. Attach or Launch

**Launch:**
```csharp
clrDebug.CreateProcess(
    applicationPath,
    commandLine,
    processAttributes,
    threadAttributes,
    inheritHandles: false,
    creationFlags: DEBUG_ONLY_THIS_PROCESS,
    environment,
    currentDirectory,
    startupInfo,
    out processInfo,
    debuggingFlags,
    out ICorDebugProcess process
);
```

**Attach:**
```csharp
clrDebug.DebugActiveProcess(
    processId,
    win32Attach: false,
    out ICorDebugProcess process
);
```

### 3. Handle Events

The debugger receives callbacks through `ICorDebugManagedCallback`:

```csharp
public class DebugEventHandler : ICorDebugManagedCallback
{
    void Breakpoint(
        ICorDebugAppDomain appDomain,
        ICorDebugThread thread,
        ICorDebugBreakpoint breakpoint)
    {
        // Process is now stopped
        // Inspect state, then call Continue()
    }

    void StepComplete(
        ICorDebugAppDomain appDomain,
        ICorDebugThread thread,
        ICorDebugStepper stepper,
        CorDebugStepReason reason)
    {
        // Step operation completed
    }

    void Exception(
        ICorDebugAppDomain appDomain,
        ICorDebugThread thread,
        int unhandled)
    {
        // Exception thrown
    }

    // Many more callbacks...
}
```

### 4. Set Breakpoints

```csharp
// Get the function
module.GetFunctionFromToken(methodToken, out ICorDebugFunction function);
function.GetILCode(out ICorDebugCode code);

// Create breakpoint at IL offset
code.CreateBreakpoint(ilOffset, out ICorDebugBreakpoint breakpoint);
breakpoint.Activate(true);
```

### 5. Control Execution

```csharp
// Continue
process.Continue(outOfBand: false);

// Stop/Break
process.Stop(timeout: 0);

// Step over
thread.CreateStepper(out ICorDebugStepper stepper);
stepper.SetInterceptMask(CorDebugIntercept.INTERCEPT_ALL);
stepper.Step(bStepIn: false);  // Step over
process.Continue(false);
```

### 6. Inspect State

**Get Stack Trace:**
```csharp
thread.EnumerateChains(out ICorDebugChainEnum chains);
while (chains.Next(1, out ICorDebugChain chain, out _) == 0)
{
    chain.EnumerateFrames(out ICorDebugFrameEnum frames);
    while (frames.Next(1, out ICorDebugFrame frame, out _) == 0)
    {
        if (frame is ICorDebugILFrame ilFrame)
        {
            // Get function info, IL offset, etc.
        }
    }
}
```

**Get Local Variables:**
```csharp
ilFrame.EnumerateLocalVariables(out ICorDebugValueEnum locals);
while (locals.Next(1, out ICorDebugValue value, out _) == 0)
{
    // Read variable value based on type
    var genericValue = value as ICorDebugGenericValue;
    genericValue.GetValue(out object val);
}
```

**Evaluate Expression:**
```csharp
thread.CreateEval(out ICorDebugEval eval);

// For simple property access: obj.Property
eval.CallFunction(
    propertyGetterFunction,
    new[] { objectValue }
);

process.Continue(false);
// Wait for EvalComplete callback
// Get result from ICorDebugEval.GetResult()
```

## Source Mapping

To map source code lines to IL offsets, debuggers use symbol files:

### PDB Formats

| Format | Extension | Description |
|--------|-----------|-------------|
| Windows PDB | `.pdb` | Traditional format, Windows only |
| Portable PDB | `.pdb` | Cross-platform, part of .NET Core |
| Embedded PDB | In DLL | PDB embedded in assembly |

### Mapping Process

```
Source: UserService.cs, Line 42
         │
         ▼
┌─────────────────────────────────────────────┐
│           PDB Symbol File                    │
│  - Document references (source files)        │
│  - Sequence points (IL offset <-> line)      │
│  - Local variable scopes                     │
└─────────────────────────────────────────────┘
         │
         ▼
Method: UserService.GetUser
IL Offset: 0x15
```

### Reading Portable PDBs

```csharp
using System.Reflection.Metadata;

var reader = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
var metadata = reader.GetMetadataReader();

// Get method debug info
var debugInfo = metadata.GetMethodDebugInformation(methodHandle);

// Get sequence points (source <-> IL mapping)
foreach (var sp in debugInfo.GetSequencePoints())
{
    // sp.StartLine, sp.EndLine
    // sp.Offset (IL offset)
}
```

## Challenges & Solutions

### Async/Await Code

**Problem:** Async methods are rewritten by the compiler into state machines.

**Solution:**
- Track state machine types (`<Method>d__X`)
- Map back to original source using attributes
- Step through continuation points

### Just My Code

**Problem:** User doesn't want to step into framework code.

**Solution:**
- Use `[DebuggerNonUserCode]` and `[DebuggerStepThrough]` attributes
- Filter modules by assembly metadata
- Configure stepper with `SetUnmappedStopMask`

### Optimized Code

**Problem:** Release builds inline methods, eliminate variables.

**Solution:**
- Detect optimized code via module flags
- Warn user about limited debugging
- Use ICorDebugILFrame2 for better variable access

### Generic Types

**Problem:** Generic instantiations create new types at runtime.

**Solution:**
- Use `ICorDebugType` interface for type parameters
- Handle open vs closed generic types
- Special handling for value type instantiations

## Performance Considerations

1. **Minimize Continue/Stop cycles** — Each stop/continue has overhead
2. **Cache symbol information** — PDB reading is expensive
3. **Batch variable reads** — Enumerate once, process all
4. **Use conditional breakpoints sparingly** — Evaluated on every hit
5. **Expression evaluation is slow** — Requires code execution in debuggee

## References

- [ICorDebug Interface - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/icordebug-interface)
- [.NET Debugging API Reference](https://learn.microsoft.com/en-us/dotnet/core/unmanaged-api/debugging/)
- [ClrDebug GitHub](https://github.com/lordmilko/ClrDebug)
- [Writing a .NET Debugger](https://lowleveldesign.org/2010/10/11/writing-a-net-debugger-part-1-starting-the-debugging-session/)
