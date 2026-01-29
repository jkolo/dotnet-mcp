# Development Guide

## Prerequisites

- .NET 10 SDK
- Visual Studio 2022, Rider, or VS Code
- Git

## Building

```bash
# Clone repository
git clone https://github.com/yourname/DebugMcp
cd DebugMcp

# Restore and build
dotnet build

# Run tests
dotnet test

# Run locally
dotnet run --project src/DebugMcp
```

## Project Structure

```
DebugMcp/
├── DebugMcp.slnx                 # Solution file
├── README.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── DEBUGGER.md
│   ├── MCP_TOOLS.md
│   └── DEVELOPMENT.md
├── src/
│   └── DebugMcp/
│       ├── DebugMcp.csproj       # Main project
│       ├── Program.cs             # Entry point
│       ├── Tools/                 # MCP tool implementations
│       │   ├── SessionTools.cs
│       │   ├── BreakpointTools.cs
│       │   ├── ExecutionTools.cs
│       │   └── InspectionTools.cs
│       ├── Debugger/              # Core debugging logic
│       │   ├── DebugSession.cs
│       │   ├── DebugEventHandler.cs
│       │   ├── BreakpointManager.cs
│       │   ├── ExpressionEvaluator.cs
│       │   └── SourceMapper.cs
│       └── Infrastructure/        # Platform support
│           ├── DbgShimLoader.cs
│           └── CorDebugFactory.cs
└── tests/
    └── DebugMcp.Tests/
        ├── DebugMcp.Tests.csproj
        ├── Tools/                 # Tool unit tests
        ├── Debugger/              # Debugger tests
        └── Integration/           # Integration tests
```

## Testing

### Unit Tests

```bash
dotnet test --filter Category=Unit
```

### Integration Tests

Integration tests require a running sample application:

```bash
# Build test target
dotnet build tests/TestTarget

# Run integration tests
dotnet test --filter Category=Integration
```

### Manual Testing with MCP Inspector

```bash
# Install MCP Inspector
npm install -g @modelcontextprotocol/inspector

# Run DebugMcp with inspector
mcp-inspector dotnet run --project src/DebugMcp
```

## Debugging DebugMcp Itself

### VS Code

1. Open in VS Code
2. Select "DebugMcp (Debug)" launch configuration
3. Set breakpoints in Tool or Debugger code
4. F5 to start debugging

### Rider

1. Open solution in Rider
2. Select DebugMcp run configuration
3. Set breakpoints
4. Debug (Shift+F9)

### Testing with Claude Desktop

1. Build: `dotnet build -c Release`
2. Configure in `claude_desktop_config.json`:
   ```json
   {
     "mcpServers": {
       "dotnet-debugger-dev": {
         "command": "dotnet",
         "args": ["run", "--project", "/path/to/DebugMcp/src/DebugMcp"]
       }
     }
   }
   ```
3. Restart Claude Desktop
4. Test by asking Claude to debug a .NET app

## Adding a New Tool

### 1. Create tool method

Add to appropriate class in `Tools/*.cs`:

```csharp
[McpTool("my_new_tool", "Description of what it does")]
public async Task<MyResponse> MyNewToolAsync(
    [McpParameter("param1", "Description", required: true)] string param1,
    [McpParameter("param2", "Description")] int? param2 = null,
    CancellationToken cancellationToken = default)
{
    // Validate session state
    if (_session.State == DebugState.NotAttached)
        return new MyResponse(false, "not_attached", "No debugging session");

    // Perform operation
    var result = await _session.DoSomethingAsync(param1, cancellationToken);

    // Return structured response
    return new MyResponse(true, null, result);
}
```

### 2. Add response model

```csharp
public record MyResponse(
    bool Success,
    string? ErrorCode,
    string Message);
```

### 3. Write tests

```csharp
[Fact]
[Trait("Category", "Unit")]
public async Task MyNewTool_WithValidInput_ReturnsSuccess()
{
    // Arrange
    var mockSession = new Mock<IDebugSession>();
    mockSession.Setup(s => s.State).Returns(DebugState.Stopped);
    mockSession.Setup(s => s.DoSomethingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync("result");

    var tools = new MyToolsClass(mockSession.Object);

    // Act
    var result = await tools.MyNewToolAsync("test", 42);

    // Assert
    Assert.True(result.Success);
    Assert.Null(result.ErrorCode);
}

[Fact]
[Trait("Category", "Unit")]
public async Task MyNewTool_WhenNotAttached_ReturnsError()
{
    // Arrange
    var mockSession = new Mock<IDebugSession>();
    mockSession.Setup(s => s.State).Returns(DebugState.NotAttached);

    var tools = new MyToolsClass(mockSession.Object);

    // Act
    var result = await tools.MyNewToolAsync("test");

    // Assert
    Assert.False(result.Success);
    Assert.Equal("not_attached", result.ErrorCode);
}
```

### 4. Document

Add entry to `docs/MCP_TOOLS.md` with:
- Description
- Parameters table
- Example request
- Example response
- Error codes

## Code Style

### General

- Follow [.NET naming conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use nullable reference types (`#nullable enable`)
- Prefer records for DTOs and immutable data
- Use `CancellationToken` for async operations

### Async

- Async methods end with `Async` suffix
- Always pass `CancellationToken` through the call chain
- Prefer `ValueTask` for hot paths that often complete synchronously

### Logging

```csharp
// Use structured logging with semantic parameters
_logger.LogDebug("Setting breakpoint at {File}:{Line}", file, line);
_logger.LogWarning("Breakpoint {Id} not verified: {Reason}", id, reason);
_logger.LogError(ex, "Failed to evaluate expression {Expression}", expr);
```

**Important:** Logs go to stderr (not stdout, which is reserved for MCP JSON-RPC).

### Error Handling

```csharp
// Use result types for expected failures
public record BreakpointResult(
    bool Success,
    int? Id = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

// Throw exceptions for unexpected failures
if (process == null)
    throw new InvalidOperationException("Process unexpectedly null");
```

## Debugging Tips

### COM Interop Issues

- ICorDebug requires STA thread for callbacks
- Use `Marshal.GetLastWin32Error()` after failed P/Invoke
- Check HRESULT values with `Marshal.GetExceptionForHR()`

### Debugging Hangs

- Check if `Continue()` was called after callback
- Verify callback handler doesn't block
- Use `process.Stop(0)` with timeout for diagnosis

### Symbol Loading

- Enable PDB loading diagnostics: `DOTNET_STARTUP_HOOKS`
- Check symbol cache location
- Verify PDB matches assembly version

## Contributing

### Workflow

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make changes with tests
4. Run all tests: `dotnet test`
5. Commit with descriptive message
6. Push: `git push origin feature/my-feature`
7. Create Pull Request

### Commit Messages

Follow conventional commits:

```
feat: add conditional breakpoint support
fix: handle null thread in stacktrace
docs: update MCP_TOOLS with evaluate examples
refactor: extract SourceMapper from BreakpointManager
test: add integration tests for attach scenario
```

### Pull Request Checklist

- [ ] All tests pass
- [ ] New code has tests
- [ ] Documentation updated
- [ ] No breaking changes (or documented if necessary)
- [ ] Changelog entry added

## Release Process

### Versioning

Follow [SemVer](https://semver.org/):
- MAJOR: Breaking API changes
- MINOR: New features, backward compatible
- PATCH: Bug fixes, backward compatible

### Release Steps

```bash
# Update version in DebugMcp.csproj
# <Version>1.2.3</Version>

# Update CHANGELOG.md

# Commit and tag
git add .
git commit -m "release: v1.2.3"
git tag v1.2.3
git push origin main v1.2.3
```

GitHub Actions will:
- Run tests
- Build NuGet package
- Publish to nuget.org
- Create GitHub Release

## Troubleshooting

### "Cannot find dbgshim"

Ensure the correct platform package is installed:
```xml
<PackageReference Include="Microsoft.Diagnostics.DbgShim.linux-x64" Version="9.0.652701" />
```

### "Process already has a debugger attached"

Only one debugger can attach at a time. Detach VS/Rider first.

### "Breakpoint not bound"

- Check if module is loaded (`debug_state` shows loaded modules)
- Verify source file path matches exactly
- Check PDB is available and matches assembly

### "Expression evaluation timeout"

- Complex expressions take time
- LINQ queries may enumerate collections
- Method calls execute in debuggee context
