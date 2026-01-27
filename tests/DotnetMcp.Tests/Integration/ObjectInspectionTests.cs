using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for object inspection workflow (T019).
/// Tests InspectObjectAsync with a real debugged process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class ObjectInspectionTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<ILogger<BreakpointRegistry>> _registryLoggerMock;
    private readonly Mock<ILogger<BreakpointManager>> _bpManagerLoggerMock;
    private readonly Mock<ILogger<PdbSymbolReader>> _pdbLoggerMock;
    private readonly Mock<ILogger<PdbSymbolCache>> _pdbCacheLoggerMock;

    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private readonly BreakpointRegistry _breakpointRegistry;
    private readonly PdbSymbolCache _pdbCache;
    private readonly PdbSymbolReader _pdbReader;
    private readonly SimpleConditionEvaluator _conditionEvaluator;
    private readonly BreakpointManager _breakpointManager;

    private TestTargetProcess? _targetProcess;

    public ObjectInspectionTests()
    {
        _debuggerLoggerMock = new Mock<ILogger<ProcessDebugger>>();
        _managerLoggerMock = new Mock<ILogger<DebugSessionManager>>();
        _registryLoggerMock = new Mock<ILogger<BreakpointRegistry>>();
        _bpManagerLoggerMock = new Mock<ILogger<BreakpointManager>>();
        _pdbLoggerMock = new Mock<ILogger<PdbSymbolReader>>();
        _pdbCacheLoggerMock = new Mock<ILogger<PdbSymbolCache>>();

        _pdbCache = new PdbSymbolCache(_pdbCacheLoggerMock.Object);
        _pdbReader = new PdbSymbolReader(_pdbCache, _pdbLoggerMock.Object);
        _processDebugger = new ProcessDebugger(_debuggerLoggerMock.Object, _pdbReader);
        _sessionManager = new DebugSessionManager(_processDebugger, _managerLoggerMock.Object);

        _breakpointRegistry = new BreakpointRegistry(_registryLoggerMock.Object);
        _conditionEvaluator = new SimpleConditionEvaluator();
        _breakpointManager = new BreakpointManager(
            _breakpointRegistry,
            _pdbReader,
            _processDebugger,
            _conditionEvaluator,
            _bpManagerLoggerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _breakpointManager.ClearAllBreakpointsAsync(CancellationToken.None);
        try { await _sessionManager.DisconnectAsync(); }
        catch { /* ignore cleanup errors */ }
        _processDebugger.Dispose();
        _targetProcess?.Dispose();
    }

    private async Task PauseAtObjectTarget()
    {
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var sourceFile = TestTargetProcess.GetSourceFilePath("ObjectTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 25);
        await _targetProcess.SendCommandAsync("object");
        var hit = await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(10));
        hit.Should().NotBeNull("Breakpoint should be hit");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task InspectObjectAsync_WhenPausedAtBreakpoint_ReturnsObjectDetails()
    {
        // Arrange
        await PauseAtObjectTarget();

        // Act - inspect the _currentUser field
        var result = await _sessionManager.InspectObjectAsync("this._currentUser");

        // Assert
        result.Should().NotBeNull();
        result.IsNull.Should().BeFalse();
        result.TypeName.Should().Contain("Person");
        result.Fields.Should().NotBeEmpty();
        result.Address.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task InspectObjectAsync_WithDepth2_ReturnsNestedFields()
    {
        // Arrange
        await PauseAtObjectTarget();

        // Act - inspect with depth 2 to get nested Address fields
        var result = await _sessionManager.InspectObjectAsync("this._currentUser", depth: 2);

        // Assert
        result.Should().NotBeNull();
        result.IsNull.Should().BeFalse();
        result.Fields.Should().NotBeEmpty();
        // With depth 2, we should see expanded nested objects
        result.Fields.Should().Contain(f => f.Name == "HomeAddress" || f.Name == "<HomeAddress>k__BackingField");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task InspectObjectAsync_NullReference_ReturnsIsNullTrue()
    {
        // Arrange
        await PauseAtObjectTarget();

        // Act - inspect a null property (WorkAddress is null)
        var result = await _sessionManager.InspectObjectAsync("this._currentUser.WorkAddress");

        // Assert
        result.Should().NotBeNull();
        result.IsNull.Should().BeTrue("WorkAddress is null in the test target");
    }
}
