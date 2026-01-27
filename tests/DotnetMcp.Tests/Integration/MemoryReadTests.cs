using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for memory read workflow (T033).
/// Tests ReadMemoryAsync with a real debugged process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class MemoryReadTests : IAsyncLifetime
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

    public MemoryReadTests()
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
    public async Task ReadMemoryAsync_WithObjectAddress_ReturnsBytes()
    {
        // Arrange - first get an object address via inspect
        await PauseAtObjectTarget();
        var inspection = await _sessionManager.InspectObjectAsync("this._currentUser");
        inspection.IsNull.Should().BeFalse();
        var address = inspection.Address;

        // Act - read memory at the object address
        var result = await _sessionManager.ReadMemoryAsync(address, 64);

        // Assert
        result.Should().NotBeNull();
        result.Address.Should().NotBeNullOrEmpty();
        result.RequestedSize.Should().Be(64);
        result.ActualSize.Should().BeGreaterThan(0);
        result.Bytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task ReadMemoryAsync_SmallSize_ReturnsRequestedAmount()
    {
        // Arrange
        await PauseAtObjectTarget();
        var inspection = await _sessionManager.InspectObjectAsync("this._currentUser");
        var address = inspection.Address;

        // Act - read just 16 bytes
        var result = await _sessionManager.ReadMemoryAsync(address, 16);

        // Assert
        result.Should().NotBeNull();
        result.RequestedSize.Should().Be(16);
        result.ActualSize.Should().BeLessThanOrEqualTo(16);
    }
}
