using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for layout inspection workflow (T055).
/// Tests GetTypeLayoutAsync with a real debugged process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class LayoutInspectionTests : IAsyncLifetime
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

    public LayoutInspectionTests()
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
    public async Task GetTypeLayoutAsync_ForKnownType_ReturnsLayout()
    {
        // Arrange
        await PauseAtObjectTarget();

        // Act - get layout for Person type
        var result = await _sessionManager.GetTypeLayoutAsync("TestTargetApp.Person");

        // Assert
        result.Should().NotBeNull();
        result.TypeName.Should().Contain("Person");
        result.TotalSize.Should().BeGreaterThan(0);
        result.Fields.Should().NotBeEmpty();
        result.IsValueType.Should().BeFalse("Person is a class");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task GetTypeLayoutAsync_WithInheritedFields_IncludesBaseFields()
    {
        // Arrange
        await PauseAtObjectTarget();

        // Act - Person inherits from BaseEntity (has Id, CreatedAt)
        var result = await _sessionManager.GetTypeLayoutAsync(
            "TestTargetApp.Person", includeInherited: true);

        // Assert
        result.Should().NotBeNull();
        result.Fields.Should().NotBeEmpty();
        // Should include both Person fields and BaseEntity fields
        result.Fields.Count.Should().BeGreaterThanOrEqualTo(2, "should have Person + inherited fields");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task GetTypeLayoutAsync_ForValueType_ReportsIsValueType()
    {
        // Arrange
        await PauseAtObjectTarget();

        // Act - get layout for a struct (Int32 is a value type)
        var result = await _sessionManager.GetTypeLayoutAsync("System.Int32");

        // Assert
        result.Should().NotBeNull();
        result.IsValueType.Should().BeTrue("Int32 is a value type");
        result.HeaderSize.Should().Be(0, "value types have no object header");
    }
}
