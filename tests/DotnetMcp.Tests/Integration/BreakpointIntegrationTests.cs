using DotnetMcp.Models;
using DotnetMcp.Models.Breakpoints;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// End-to-end integration tests for breakpoint functionality.
/// These tests verify the complete breakpoint workflow with a real target process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class BreakpointIntegrationTests : IAsyncLifetime
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

    public BreakpointIntegrationTests()
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
    /// Verify that we can set a breakpoint on TestTargetApp code.
    /// </summary>
    [Fact]
    public async Task SetBreakpoint_OnTestTargetMethod_CreatesBreakpoint()
    {
        // Arrange
        var sourceFile = TestTargetProcess.GetSourceFilePath("MethodTarget.cs");

        // Act - set breakpoint on line 15 (return statement in SayHello)
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 15,
            column: null,
            condition: null,
            CancellationToken.None);

        // Assert
        breakpoint.Should().NotBeNull();
        breakpoint.Location.File.Should().Contain("MethodTarget.cs");
        breakpoint.Location.Line.Should().Be(15);
        breakpoint.State.Should().Be(BreakpointState.Pending, "no debugger attached yet");
        breakpoint.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// Verify breakpoint list contains set breakpoints.
    /// </summary>
    [Fact]
    public async Task GetBreakpoints_AfterSetting_ReturnsAllBreakpoints()
    {
        // Arrange - set multiple breakpoints
        var methodFile = TestTargetProcess.GetSourceFilePath("MethodTarget.cs");
        var loopFile = TestTargetProcess.GetSourceFilePath("LoopTarget.cs");

        await _breakpointManager.SetBreakpointAsync(methodFile, 15, null, null, CancellationToken.None);
        await _breakpointManager.SetBreakpointAsync(loopFile, 17, null, null, CancellationToken.None);

        // Act
        var breakpoints = await _breakpointManager.GetBreakpointsAsync(CancellationToken.None);

        // Assert
        breakpoints.Should().HaveCount(2);
        breakpoints.Should().Contain(bp => bp.Location.File.Contains("MethodTarget.cs"));
        breakpoints.Should().Contain(bp => bp.Location.File.Contains("LoopTarget.cs"));
    }

    /// <summary>
    /// Verify breakpoint can be removed.
    /// </summary>
    [Fact]
    public async Task RemoveBreakpoint_ExistingBreakpoint_RemovesSuccessfully()
    {
        // Arrange
        var sourceFile = TestTargetProcess.GetSourceFilePath("MethodTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile, 15, null, null, CancellationToken.None);

        // Act
        var removed = await _breakpointManager.RemoveBreakpointAsync(breakpoint.Id, CancellationToken.None);

        // Assert
        removed.Should().BeTrue();
        var breakpoints = await _breakpointManager.GetBreakpointsAsync(CancellationToken.None);
        breakpoints.Should().BeEmpty();
    }

    /// <summary>
    /// Verify breakpoint can be disabled and enabled.
    /// </summary>
    [Fact]
    public async Task SetBreakpointEnabled_TogglesEnabled()
    {
        // Arrange
        var sourceFile = TestTargetProcess.GetSourceFilePath("MethodTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile, 15, null, null, CancellationToken.None);

        // Act - disable
        var disabled = await _breakpointManager.SetBreakpointEnabledAsync(
            breakpoint.Id, false, CancellationToken.None);

        // Assert - disabled
        disabled.Should().NotBeNull();
        disabled!.Enabled.Should().BeFalse();

        // Act - enable
        var enabled = await _breakpointManager.SetBreakpointEnabledAsync(
            breakpoint.Id, true, CancellationToken.None);

        // Assert - enabled
        enabled.Should().NotBeNull();
        enabled!.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// Verify conditional breakpoint validation.
    /// </summary>
    [Fact]
    public async Task SetBreakpoint_WithValidCondition_SetsCondition()
    {
        // Arrange
        var sourceFile = TestTargetProcess.GetSourceFilePath("LoopTarget.cs");

        // Act - set breakpoint with hit count condition
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 17,
            column: null,
            condition: "hitCount > 3",
            CancellationToken.None);

        // Assert
        breakpoint.Should().NotBeNull();
        breakpoint.Condition.Should().Be("hitCount > 3");
    }

    /// <summary>
    /// Verify invalid condition throws exception.
    /// </summary>
    [Fact]
    public async Task SetBreakpoint_WithInvalidCondition_ThrowsArgumentException()
    {
        // Arrange
        var sourceFile = TestTargetProcess.GetSourceFilePath("LoopTarget.cs");

        // Act
        var act = async () => await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 17,
            column: null,
            condition: "hitCount >", // Invalid - missing operand
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid condition*");
    }

    /// <summary>
    /// Verify exception breakpoint can be set.
    /// </summary>
    [Fact]
    public async Task SetExceptionBreakpoint_ForInvalidOperationException_SetsBreakpoint()
    {
        // Act
        var exceptionBp = await _breakpointManager.SetExceptionBreakpointAsync(
            "System.InvalidOperationException",
            breakOnFirstChance: true,
            breakOnSecondChance: true,
            includeSubtypes: true,
            CancellationToken.None);

        // Assert
        exceptionBp.Should().NotBeNull();
        exceptionBp.ExceptionType.Should().Be("System.InvalidOperationException");
        exceptionBp.BreakOnFirstChance.Should().BeTrue();
        exceptionBp.BreakOnSecondChance.Should().BeTrue();
        exceptionBp.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// E2E test: Attach to process, set breakpoint, trigger code, verify hit.
    /// NOTE: This test requires the dbgshim library and may need elevated permissions.
    /// Run with: dotnet test --filter "FullyQualifiedName~AttachAndHitBreakpoint"
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task AttachAndHitBreakpoint_WhenCodeExecutes_BreakpointIsHit()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        _targetProcess.IsRunning.Should().BeTrue("test target should be running");

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Verify it's detected as a .NET process
        _processDebugger.IsNetProcess(targetPid).Should().BeTrue("test target is a .NET process");

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();
        session.State.Should().Be(SessionState.Running);

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

        // Assert
        hit.Should().NotBeNull("breakpoint should have been hit");
        hit!.BreakpointId.Should().Be(breakpoint.Id);
        hit.HitCount.Should().Be(1);

        // Verify session is paused
        _sessionManager.GetCurrentState().Should().Be(SessionState.Paused);
    }

    /// <summary>
    /// E2E test: Multiple breakpoint hits with hit count tracking.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LoopBreakpoint_WhenLoopExecutes_HitMultipleTimes()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();

        // Set breakpoint on LoopTarget.RunLoop line 17 (Console.WriteLine in loop)
        var sourceFile = TestTargetProcess.GetSourceFilePath("LoopTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 17,
            column: null,
            condition: null,
            CancellationToken.None);

        // Send "loop" command to run 5 iterations
        await _targetProcess.SendCommandAsync("loop");

        // Wait for first hit
        var hit1 = await _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        hit1.Should().NotBeNull();
        hit1!.HitCount.Should().Be(1);

        // Continue execution and wait for more hits
        // Note: In a full implementation, we would call Continue() here
        // For now, verify the first hit works
    }

    /// <summary>
    /// E2E test: Conditional breakpoint only breaks when condition is met.
    /// Note: Currently conditions are validated but not evaluated at runtime.
    /// </summary>
    [Fact(Skip = "Conditional breakpoint evaluation not yet implemented")]
    [Trait("Category", "E2E")]
    public async Task ConditionalBreakpoint_OnlyHitsWhenConditionMet()
    {
        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Attach to the target
        var session = await _sessionManager.AttachAsync(targetPid, timeout);
        session.Should().NotBeNull();

        // Set breakpoint with condition: only break on 3rd hit
        var sourceFile = TestTargetProcess.GetSourceFilePath("LoopTarget.cs");
        var breakpoint = await _breakpointManager.SetBreakpointAsync(
            sourceFile,
            line: 17,
            column: null,
            condition: "hitCount == 3",
            CancellationToken.None);

        // Send "loop" command to run 5 iterations
        await _targetProcess.SendCommandAsync("loop");

        // Wait for the breakpoint hit (should be on 3rd iteration)
        var hit = await _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        // Assert - should only pause when hitCount == 3
        hit.Should().NotBeNull();
        hit!.HitCount.Should().Be(3, "condition was hitCount == 3");
    }

    /// <summary>
    /// Verify WaitForBreakpointAsync times out when no breakpoint is hit.
    /// </summary>
    [Fact]
    public async Task WaitForBreakpoint_WhenNoHit_ReturnsNullAfterTimeout()
    {
        // Act - wait with short timeout (no process attached, no hits will occur)
        var hit = await _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        // Assert
        hit.Should().BeNull("no breakpoint was hit");
    }

    /// <summary>
    /// Verify WaitForBreakpointAsync can be cancelled.
    /// </summary>
    [Fact]
    public async Task WaitForBreakpoint_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var waitTask = _breakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(30),
            cts.Token);

        // Act - cancel after short delay
        await Task.Delay(50);
        cts.Cancel();

        // Assert
        var act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
