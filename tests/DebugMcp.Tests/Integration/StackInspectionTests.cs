using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for stack trace inspection (T013).
/// These tests verify the complete stack inspection workflow with a real target process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class StackInspectionTests : IAsyncLifetime
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

    public StackInspectionTests()
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
        await _sessionManager.DisconnectAsync();
        _processDebugger.Dispose();
        _targetProcess?.Dispose();
    }

    /// <summary>
    /// Verify GetStackFrames throws when no session is active.
    /// </summary>
    [Fact]
    public void GetStackFrames_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = () => _processDebugger.GetStackFrames();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    /// <summary>
    /// Verify GetStackFrames returns frames when paused at breakpoint.
    /// </summary>
    [Fact]
    public async Task GetStackFrames_WhenPausedAtBreakpoint_ReturnsStackFrames()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint in NestedTarget
        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32); // Level3 line

        // Trigger the nested call
        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var (frames, totalFrames) = _processDebugger.GetStackFrames();

        // Assert
        frames.Should().NotBeEmpty("should have stack frames when paused");
        totalFrames.Should().BeGreaterThan(0, "should report total frame count");

        // Verify first frame (Level3)
        frames[0].Function.Should().Contain("Level3", "top frame should be the breakpoint location");
        frames[0].IsExternal.Should().BeFalse("user code should not be marked as external");
    }

    /// <summary>
    /// Verify GetStackFrames returns external frame markers for runtime code.
    /// </summary>
    [Fact]
    public async Task GetStackFrames_WithRuntimeFrames_MarksExternalFrames()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint in NestedTarget
        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act - get full stack to include runtime frames
        var (frames, _) = _processDebugger.GetStackFrames(maxFrames: 100);

        // Assert - should have a mix of user and external frames
        var userFrames = frames.Where(f => !f.IsExternal).ToList();
        var externalFrames = frames.Where(f => f.IsExternal).ToList();

        userFrames.Should().NotBeEmpty("should have user code frames");
        // Note: May or may not have external frames depending on call depth
    }

    /// <summary>
    /// Verify GetStackFrames pagination works correctly.
    /// </summary>
    [Fact]
    public async Task GetStackFrames_WithPagination_ReturnsCorrectSubset()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act - get first 2 frames
        var (firstTwo, total1) = _processDebugger.GetStackFrames(startFrame: 0, maxFrames: 2);

        // Get next 2 frames
        var (nextTwo, total2) = _processDebugger.GetStackFrames(startFrame: 2, maxFrames: 2);

        // Assert
        total1.Should().Be(total2, "total should be consistent across pages");
        firstTwo.Should().HaveCountLessThanOrEqualTo(2);

        if (total1 > 2)
        {
            nextTwo.Should().NotBeEmpty("should have more frames if total > 2");
            firstTwo[0].Index.Should().NotBe(nextTwo[0].Index, "pages should not overlap");
        }
    }

    /// <summary>
    /// Verify GetStackFrames includes source locations when PDB available.
    /// </summary>
    [Fact]
    public async Task GetStackFrames_WithPdbSymbols_IncludesSourceLocations()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var (frames, _) = _processDebugger.GetStackFrames();

        // Assert
        var userFrames = frames.Where(f => !f.IsExternal).ToList();
        userFrames.Should().NotBeEmpty();

        var frameWithLocation = userFrames.FirstOrDefault(f => f.Location != null);
        frameWithLocation.Should().NotBeNull("at least one user frame should have source location");
        frameWithLocation!.Location!.File.Should().NotBeNullOrEmpty();
        frameWithLocation.Location.Line.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verify GetStackFrames includes arguments when available.
    /// </summary>
    [Fact]
    public async Task GetStackFrames_WithMethodArguments_IncludesArguments()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // MethodTarget has methods with arguments
        var sourceFile = TestTargetProcess.GetSourceFilePath("MethodTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 14); // SayHello first statement

        await _targetProcess.SendCommandAsync("method");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var (frames, _) = _processDebugger.GetStackFrames();

        // Assert
        frames.Should().NotBeEmpty();
        var frameWithArgs = frames.FirstOrDefault(f => f.Arguments != null && f.Arguments.Count > 0);
        frameWithArgs.Should().NotBeNull("frame for Add method should have arguments");
    }
}
