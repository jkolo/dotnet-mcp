using System.Diagnostics;
using DebugMcp.Models.Breakpoints;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Performance;

/// <summary>
/// Performance tests verifying success criteria from spec:
/// - SC-001: Breakpoint set verified within 2 seconds
/// - SC-002: Wait returns within 100ms of breakpoint hit
/// </summary>
public class BreakpointPerformanceTests
{
    private readonly BreakpointRegistry _registry;
    private readonly Mock<IPdbSymbolReader> _pdbReaderMock;
    private readonly Mock<IProcessDebugger> _processDebuggerMock;
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock;
    private readonly BreakpointManager _manager;

    public BreakpointPerformanceTests()
    {
        var registryLogger = new Mock<ILogger<BreakpointRegistry>>();
        _registry = new BreakpointRegistry(registryLogger.Object);

        _pdbReaderMock = new Mock<IPdbSymbolReader>();
        _processDebuggerMock = new Mock<IProcessDebugger>();
        _conditionEvaluatorMock = new Mock<IConditionEvaluator>();
        var managerLogger = new Mock<ILogger<BreakpointManager>>();

        // Default: conditions are valid and pass validation
        _conditionEvaluatorMock
            .Setup(x => x.ValidateCondition(It.IsAny<string?>()))
            .Returns(ConditionValidation.Valid());
        _conditionEvaluatorMock
            .Setup(x => x.Evaluate(It.IsAny<string?>(), It.IsAny<ConditionContext>()))
            .Returns(ConditionResult.Ok(true));

        _manager = new BreakpointManager(
            _registry,
            _pdbReaderMock.Object,
            _processDebuggerMock.Object,
            _conditionEvaluatorMock.Object,
            managerLogger.Object);
    }

    /// <summary>
    /// SC-001: Breakpoint set operation completes within 2 seconds.
    /// Tests the internal SetBreakpointAsync path (without ICorDebug).
    /// </summary>
    [Fact]
    public async Task SetBreakpointAsync_WithPendingBreakpoint_CompletesWithin2Seconds()
    {
        // Arrange - not attached, will create pending breakpoint
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var stopwatch = Stopwatch.StartNew();

        // Act - set a breakpoint (will be pending since no debugger attached)
        var breakpoint = await _manager.SetBreakpointAsync(
            "/path/to/TestFile.cs",
            42,
            column: null,
            condition: null,
            CancellationToken.None);

        stopwatch.Stop();

        // Assert - SC-001: within 2 seconds
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "SC-001: Breakpoint set should complete within 2 seconds");
        breakpoint.Should().NotBeNull();
        breakpoint.State.Should().Be(BreakpointState.Pending,
            "Without debugger, breakpoint should be pending");
    }

    /// <summary>
    /// SC-001: Multiple breakpoints can be set within acceptable time.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task SetMultipleBreakpoints_CompletesWithinAcceptableTime(int count)
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var stopwatch = Stopwatch.StartNew();

        // Act - set multiple breakpoints
        for (int i = 0; i < count; i++)
        {
            await _manager.SetBreakpointAsync(
                $"/path/to/TestFile{i}.cs",
                i + 1,
                column: null,
                condition: null,
                CancellationToken.None);
        }

        stopwatch.Stop();

        // Assert - average should be well under 2s per breakpoint
        var averageMs = stopwatch.ElapsedMilliseconds / (double)count;
        averageMs.Should().BeLessThan(100,
            $"Average time per breakpoint should be <100ms (got {averageMs:F1}ms for {count} breakpoints)");
    }

    /// <summary>
    /// SC-002: WaitForBreakpointAsync returns within 100ms of hit being queued.
    /// </summary>
    [Fact]
    public async Task WaitForBreakpointAsync_WhenHitQueued_ReturnsWithin100ms()
    {
        // Arrange - create a breakpoint first
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync(
            "/path/to/TestFile.cs",
            42,
            column: null,
            condition: null,
            CancellationToken.None);

        // Pre-queue a hit before starting the wait
        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1,
            ExceptionInfo: null);

        // Use internal method to simulate hit (this is what the debugger callback does)
        _manager.OnBreakpointHit(hit);

        // Act - start measuring when we call wait
        var stopwatch = Stopwatch.StartNew();
        var result = await _manager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        stopwatch.Stop();

        // Assert - SC-002: within 100ms of hit being queued
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
            "SC-002: Wait should return within 100ms of breakpoint hit");
        result.Should().NotBeNull();
        result!.BreakpointId.Should().Be(breakpoint.Id);
    }

    /// <summary>
    /// SC-002: WaitForBreakpointAsync returns quickly when hit occurs during wait.
    /// </summary>
    [Fact]
    public async Task WaitForBreakpointAsync_WhenHitOccursDuringWait_ReturnsWithin100ms()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync(
            "/path/to/TestFile.cs",
            42,
            column: null,
            condition: null,
            CancellationToken.None);

        // Start wait task
        var waitTask = _manager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        // Small delay to ensure wait is blocking
        await Task.Delay(50);

        // Queue the hit and measure how long until wait returns
        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1,
            ExceptionInfo: null);

        var stopwatch = Stopwatch.StartNew();
        _manager.OnBreakpointHit(hit);
        var result = await waitTask;
        stopwatch.Stop();

        // Assert - SC-002: within 100ms
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
            "SC-002: Wait should return within 100ms of breakpoint hit");
        result.Should().NotBeNull();
    }

    /// <summary>
    /// GetBreakpointsAsync performance with many breakpoints.
    /// </summary>
    [Fact]
    public async Task GetBreakpointsAsync_With100Breakpoints_CompletesQuickly()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        for (int i = 0; i < 100; i++)
        {
            await _manager.SetBreakpointAsync(
                $"/path/to/TestFile{i}.cs",
                i + 1,
                column: null,
                condition: null,
                CancellationToken.None);
        }

        // Act
        var stopwatch = Stopwatch.StartNew();
        var list = await _manager.GetBreakpointsAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert - list should be essentially instant
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50),
            "Listing breakpoints should be very fast");
        list.Should().HaveCount(100);
    }

    /// <summary>
    /// RemoveBreakpointAsync completes quickly.
    /// </summary>
    [Fact]
    public async Task RemoveBreakpointAsync_CompletesQuickly()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync(
            "/path/to/TestFile.cs",
            42,
            column: null,
            condition: null,
            CancellationToken.None);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var removed = await _manager.RemoveBreakpointAsync(breakpoint.Id, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50),
            "Removing breakpoint should be very fast");
        removed.Should().BeTrue();
    }

    /// <summary>
    /// Enable/disable breakpoint completes quickly.
    /// </summary>
    [Fact]
    public async Task SetBreakpointEnabledAsync_CompletesQuickly()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync(
            "/path/to/TestFile.cs",
            42,
            column: null,
            condition: null,
            CancellationToken.None);

        // Act - disable
        var stopwatch = Stopwatch.StartNew();
        var disabled = await _manager.SetBreakpointEnabledAsync(breakpoint.Id, false, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50),
            "Enabling/disabling breakpoint should be very fast");
        disabled.Should().NotBeNull();
        disabled!.Enabled.Should().BeFalse();
    }
}
