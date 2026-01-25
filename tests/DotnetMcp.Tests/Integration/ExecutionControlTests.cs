using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for execution control (continue, step).
/// These tests verify the complete execution control workflow with a real target process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class ExecutionControlTests : IAsyncLifetime
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

    public ExecutionControlTests()
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
    /// Verify ContinueAsync throws when no session is active.
    /// </summary>
    [Fact]
    public async Task ContinueAsync_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _sessionManager.ContinueAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active debug session*");
    }

    /// <summary>
    /// Verify StepAsync throws when no session is active.
    /// </summary>
    [Fact]
    public async Task StepAsync_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _sessionManager.StepAsync(StepMode.Over);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active debug session*");
    }

    /// <summary>
    /// Verify ContinueAsync throws when process is not paused.
    /// Note: Full E2E test requires attaching and then checking state.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ContinueAsync_WhenNotPaused_ThrowsInvalidOperationException()
    {
        // Arrange - start and attach to process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(30));

        // Process should be running, not paused
        _sessionManager.GetCurrentState().Should().Be(SessionState.Running);

        // Act
        var act = async () => await _sessionManager.ContinueAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not paused*");
    }

    /// <summary>
    /// Verify StepAsync throws when process is not paused.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StepAsync_WhenNotPaused_ThrowsInvalidOperationException()
    {
        // Arrange - start and attach to process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(30));

        // Process should be running, not paused
        _sessionManager.GetCurrentState().Should().Be(SessionState.Running);

        // Act
        var act = async () => await _sessionManager.StepAsync(StepMode.Over);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not paused*");
    }

    /// <summary>
    /// E2E test: Set breakpoint, hit it, then continue execution.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ContinueAsync_AfterBreakpointHit_ResumesExecution()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        _targetProcess.IsRunning.Should().BeTrue("test target should be running");

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();

        // Set breakpoint on MethodTarget.SayHello line 14 (the greeting assignment)
        var sourceFile = TestTargetProcess.GetSourceFilePath("MethodTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 14,
            column: null,
            condition: null,
            CancellationToken.None);

        breakpoint.Should().NotBeNull();

        // Start waiting for breakpoint hit
        var waitTask = _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        // Trigger the code path by sending "method" command
        await _targetProcess.SendCommandAsync("method");

        // Wait for breakpoint hit
        var hit = await waitTask;
        hit.Should().NotBeNull("breakpoint should have been hit");

        // Verify session is paused
        _sessionManager.GetCurrentState().Should().Be(SessionState.Paused);

        // Act - continue execution
        var updatedSession = await _sessionManager.ContinueAsync();

        // Assert
        updatedSession.Should().NotBeNull();
        updatedSession.State.Should().Be(SessionState.Running);
    }

    /// <summary>
    /// E2E test: Set breakpoint, hit it, step over.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StepOver_AfterBreakpointHit_StepsToNextLine()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();

        // Set breakpoint on LoopTarget.RunLoop line 17 (Console.WriteLine)
        var sourceFile = TestTargetProcess.GetSourceFilePath("LoopTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 17,
            column: null,
            condition: null,
            CancellationToken.None);

        // Trigger the code path
        await _targetProcess.SendCommandAsync("loop");

        // Wait for breakpoint hit
        var hit = await _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        hit.Should().NotBeNull();

        // Session should be paused at breakpoint
        _sessionManager.GetCurrentState().Should().Be(SessionState.Paused);

        // Act - step over
        var updatedSession = await _sessionManager.StepAsync(StepMode.Over);

        // Assert - step completed and process paused again
        updatedSession.Should().NotBeNull();
        updatedSession.State.Should().Be(SessionState.Paused);
        updatedSession.PauseReason.Should().Be(PauseReason.Step);
        // Note: Location details (line, function name) require PDB resolution
        // which is not yet implemented for step completion events
    }

    /// <summary>
    /// E2E test: Step into a method call.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StepIn_AtMethodCall_EntersMethod()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();

        // Set breakpoint on nested call (Level1 calling Level2)
        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 14, // Level2() call inside Level1
            column: null,
            condition: null,
            CancellationToken.None);

        // Trigger the code path
        await _targetProcess.SendCommandAsync("nested");

        // Wait for breakpoint hit
        var hit = await _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        hit.Should().NotBeNull();

        // Session should be paused at breakpoint
        _sessionManager.GetCurrentState().Should().Be(SessionState.Paused);

        // Act - step in
        var updatedSession = await _sessionManager.StepAsync(StepMode.In);

        // Assert - step completed and process paused again (stepped into method)
        updatedSession.Should().NotBeNull();
        updatedSession.State.Should().Be(SessionState.Paused);
        updatedSession.PauseReason.Should().Be(PauseReason.Step);
        // Note: Function name verification requires PDB resolution
        // which is not yet implemented for step completion events
    }

    /// <summary>
    /// E2E test: Step out of current method.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StepOut_InsideMethod_ReturnsToCallers()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();

        // Set breakpoint inside Level3 (innermost method)
        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 32, // Inside Level3 - Console.WriteLine
            column: null,
            condition: null,
            CancellationToken.None);

        // Trigger the code path
        await _targetProcess.SendCommandAsync("nested");

        // Wait for breakpoint hit
        var hit = await _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        hit.Should().NotBeNull();

        // Session should be paused at breakpoint
        _sessionManager.GetCurrentState().Should().Be(SessionState.Paused);

        // Act - step out
        var updatedSession = await _sessionManager.StepAsync(StepMode.Out);

        // Assert - step completed and process paused again (stepped out to caller)
        updatedSession.Should().NotBeNull();
        updatedSession.State.Should().Be(SessionState.Paused);
        updatedSession.PauseReason.Should().Be(PauseReason.Step);
        // Note: Function name verification requires PDB resolution
        // which is not yet implemented for step completion events
    }
}
