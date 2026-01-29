using DebugMcp.Models.Breakpoints;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Unit;

/// <summary>
/// Unit tests for BreakpointManager breakpoint lifecycle operations.
/// </summary>
public class BreakpointManagerTests
{
    private readonly BreakpointRegistry _registry;
    private readonly Mock<IPdbSymbolReader> _pdbReaderMock;
    private readonly Mock<IProcessDebugger> _processDebuggerMock;
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock;
    private readonly BreakpointManager _manager;

    public BreakpointManagerTests()
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
    /// SetBreakpointAsync creates a new pending breakpoint when not attached.
    /// </summary>
    [Fact]
    public async Task SetBreakpointAsync_NotAttached_CreatesPendingBreakpoint()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);

        // Act
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        // Assert
        breakpoint.Should().NotBeNull();
        breakpoint.Id.Should().StartWith("bp-");
        breakpoint.Location.File.Should().EndWith("Program.cs");
        breakpoint.Location.Line.Should().Be(10);
        breakpoint.State.Should().Be(BreakpointState.Pending);
        breakpoint.Enabled.Should().BeTrue();
        breakpoint.Verified.Should().BeFalse();
        breakpoint.HitCount.Should().Be(0);
        breakpoint.Message.Should().Contain("No active debug session");
    }

    /// <summary>
    /// SetBreakpointAsync returns existing breakpoint for duplicate location.
    /// </summary>
    [Fact]
    public async Task SetBreakpointAsync_DuplicateLocation_ReturnsExisting()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var first = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        // Act
        var second = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        // Assert
        second.Id.Should().Be(first.Id, "duplicate location should return existing breakpoint");
        _registry.Count.Should().Be(1, "only one breakpoint should exist");
    }

    /// <summary>
    /// SetBreakpointAsync updates condition on duplicate location.
    /// </summary>
    [Fact]
    public async Task SetBreakpointAsync_DuplicateWithDifferentCondition_UpdatesCondition()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var first = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "x > 5");

        // Act
        var second = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "x > 10");

        // Assert
        second.Id.Should().Be(first.Id);
        second.Condition.Should().Be("x > 10", "condition should be updated");
    }

    /// <summary>
    /// SetBreakpointAsync includes column when specified.
    /// </summary>
    [Fact]
    public async Task SetBreakpointAsync_WithColumn_IncludesColumnInLocation()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);

        // Act
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10, column: 15);

        // Assert
        breakpoint.Location.Column.Should().Be(15);
    }

    /// <summary>
    /// RemoveBreakpointAsync removes existing breakpoint and returns true.
    /// </summary>
    [Fact]
    public async Task RemoveBreakpointAsync_ExistingBreakpoint_ReturnsTrue()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        // Act
        var result = await _manager.RemoveBreakpointAsync(breakpoint.Id);

        // Assert
        result.Should().BeTrue();
        _registry.Count.Should().Be(0);
    }

    /// <summary>
    /// RemoveBreakpointAsync returns false for non-existent breakpoint.
    /// </summary>
    [Fact]
    public async Task RemoveBreakpointAsync_NonExistent_ReturnsFalse()
    {
        // Act
        var result = await _manager.RemoveBreakpointAsync("bp-nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// GetBreakpointsAsync returns all breakpoints.
    /// </summary>
    [Fact]
    public async Task GetBreakpointsAsync_ReturnsAllBreakpoints()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        await _manager.SetBreakpointAsync("/app/Program.cs", 10);
        await _manager.SetBreakpointAsync("/app/Program.cs", 20);
        await _manager.SetBreakpointAsync("/app/Service.cs", 5);

        // Act
        var breakpoints = await _manager.GetBreakpointsAsync();

        // Assert
        breakpoints.Should().HaveCount(3);
    }

    /// <summary>
    /// GetBreakpointAsync returns specific breakpoint by ID.
    /// </summary>
    [Fact]
    public async Task GetBreakpointAsync_ExistingId_ReturnsBreakpoint()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var created = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        // Act
        var retrieved = await _manager.GetBreakpointAsync(created.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    /// <summary>
    /// GetBreakpointAsync returns null for non-existent ID.
    /// </summary>
    [Fact]
    public async Task GetBreakpointAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _manager.GetBreakpointAsync("bp-nonexistent");

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// SetBreakpointEnabledAsync disables breakpoint.
    /// </summary>
    [Fact]
    public async Task SetBreakpointEnabledAsync_Disable_UpdatesState()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        // Act
        var updated = await _manager.SetBreakpointEnabledAsync(breakpoint.Id, false);

        // Assert
        updated.Should().NotBeNull();
        updated!.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// SetBreakpointEnabledAsync re-enables breakpoint.
    /// </summary>
    [Fact]
    public async Task SetBreakpointEnabledAsync_Enable_UpdatesState()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10);
        await _manager.SetBreakpointEnabledAsync(breakpoint.Id, false);

        // Act
        var updated = await _manager.SetBreakpointEnabledAsync(breakpoint.Id, true);

        // Assert
        updated.Should().NotBeNull();
        updated!.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// WaitForBreakpointAsync returns null on timeout.
    /// </summary>
    [Fact]
    public async Task WaitForBreakpointAsync_Timeout_ReturnsNull()
    {
        // Act
        var result = await _manager.WaitForBreakpointAsync(TimeSpan.FromMilliseconds(50));

        // Assert
        result.Should().BeNull("no breakpoint was hit");
    }

    /// <summary>
    /// WaitForBreakpointAsync returns hit when breakpoint is triggered.
    /// </summary>
    [Fact]
    public async Task WaitForBreakpointAsync_BreakpointHit_ReturnsHit()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1);

        // Simulate hit in background
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            _manager.OnBreakpointHit(hit);
        });

        // Act
        var result = await _manager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Assert
        result.Should().NotBeNull();
        result!.BreakpointId.Should().Be(breakpoint.Id);
        result.ThreadId.Should().Be(1);
    }

    /// <summary>
    /// SetExceptionBreakpointAsync creates exception breakpoint.
    /// </summary>
    [Fact]
    public async Task SetExceptionBreakpointAsync_CreatesExceptionBreakpoint()
    {
        // Act
        var ebp = await _manager.SetExceptionBreakpointAsync(
            "System.NullReferenceException",
            breakOnFirstChance: true,
            breakOnSecondChance: false,
            includeSubtypes: true);

        // Assert
        ebp.Should().NotBeNull();
        ebp.Id.Should().StartWith("ebp-");
        ebp.ExceptionType.Should().Be("System.NullReferenceException");
        ebp.BreakOnFirstChance.Should().BeTrue();
        ebp.BreakOnSecondChance.Should().BeFalse();
        ebp.IncludeSubtypes.Should().BeTrue();
        ebp.Enabled.Should().BeTrue();
        ebp.Verified.Should().BeTrue();
    }

    /// <summary>
    /// RemoveExceptionBreakpointAsync removes exception breakpoint.
    /// </summary>
    [Fact]
    public async Task RemoveExceptionBreakpointAsync_ExistingBreakpoint_ReturnsTrue()
    {
        // Arrange
        var ebp = await _manager.SetExceptionBreakpointAsync("System.Exception");

        // Act
        var result = await _manager.RemoveExceptionBreakpointAsync(ebp.Id);

        // Assert
        result.Should().BeTrue();
        _registry.ExceptionCount.Should().Be(0);
    }

    /// <summary>
    /// ClearAllBreakpointsAsync removes all breakpoints.
    /// </summary>
    [Fact]
    public async Task ClearAllBreakpointsAsync_RemovesAllBreakpoints()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        await _manager.SetBreakpointAsync("/app/Program.cs", 10);
        await _manager.SetBreakpointAsync("/app/Program.cs", 20);
        await _manager.SetExceptionBreakpointAsync("System.Exception");

        // Act
        await _manager.ClearAllBreakpointsAsync();

        // Assert
        _registry.Count.Should().Be(0);
        _registry.ExceptionCount.Should().Be(0);
    }

    /// <summary>
    /// SetBreakpointAsync throws ArgumentException for invalid condition.
    /// </summary>
    [Fact]
    public async Task SetBreakpointAsync_InvalidCondition_ThrowsArgumentException()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        _conditionEvaluatorMock
            .Setup(x => x.ValidateCondition("invalid syntax"))
            .Returns(ConditionValidation.Invalid("Syntax error", 5));

        // Act & Assert
        var act = () => _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "invalid syntax");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid condition at position 5*");
    }

    /// <summary>
    /// OnBreakpointHit returns false when condition evaluates to false.
    /// </summary>
    [Fact]
    public async Task OnBreakpointHit_ConditionFalse_ReturnsFalse()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "hitCount > 5");

        // Setup condition evaluator to return false
        _conditionEvaluatorMock
            .Setup(x => x.Evaluate("hitCount > 5", It.IsAny<ConditionContext>()))
            .Returns(ConditionResult.Ok(false));

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1);

        // Act
        var shouldPause = _manager.OnBreakpointHit(hit);

        // Assert
        shouldPause.Should().BeFalse("condition was false, should continue");
    }

    /// <summary>
    /// OnBreakpointHit returns true when condition evaluates to true.
    /// </summary>
    [Fact]
    public async Task OnBreakpointHit_ConditionTrue_ReturnsTrue()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "hitCount > 5");

        // Setup condition evaluator to return true
        _conditionEvaluatorMock
            .Setup(x => x.Evaluate("hitCount > 5", It.IsAny<ConditionContext>()))
            .Returns(ConditionResult.Ok(true));

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 6);

        // Act
        var shouldPause = _manager.OnBreakpointHit(hit);

        // Assert
        shouldPause.Should().BeTrue("condition was true, should pause");
    }

    /// <summary>
    /// OnBreakpointHit increments hit count before evaluating condition.
    /// </summary>
    [Fact]
    public async Task OnBreakpointHit_IncrementsHitCountBeforeCondition()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "hitCount == 1");

        ConditionContext? capturedContext = null;
        _conditionEvaluatorMock
            .Setup(x => x.Evaluate("hitCount == 1", It.IsAny<ConditionContext>()))
            .Callback<string?, ConditionContext>((_, ctx) => capturedContext = ctx)
            .Returns(ConditionResult.Ok(true));

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 0);

        // Act
        _manager.OnBreakpointHit(hit);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.HitCount.Should().Be(1, "hit count should be incremented before evaluation");
    }

    /// <summary>
    /// OnBreakpointHit returns true (pauses) when condition evaluation fails.
    /// </summary>
    [Fact]
    public async Task OnBreakpointHit_ConditionError_ReturnsTrue()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "unknownVar");

        // Setup condition evaluator to return error
        _conditionEvaluatorMock
            .Setup(x => x.Evaluate("unknownVar", It.IsAny<ConditionContext>()))
            .Returns(ConditionResult.Error("Unknown variable"));

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1);

        // Act
        var shouldPause = _manager.OnBreakpointHit(hit);

        // Assert
        shouldPause.Should().BeTrue("should pause on error so user sees the problem");
    }

    // ========== Multi-threading tests (T092) ==========

    /// <summary>
    /// SC-006: BreakpointHit preserves correct thread ID for multi-threaded apps.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(42)]
    [InlineData(9999)]
    public async Task OnBreakpointHit_MultipleThreadIds_PreservesCorrectThreadId(int threadId)
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10);

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: threadId,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1);

        // Act
        _manager.OnBreakpointHit(hit);
        var result = await _manager.WaitForBreakpointAsync(TimeSpan.FromSeconds(1));

        // Assert
        result.Should().NotBeNull();
        result!.ThreadId.Should().Be(threadId,
            "thread ID must be preserved correctly for multi-threaded debugging");
    }

    /// <summary>
    /// ConditionContext includes correct thread ID for condition evaluation.
    /// </summary>
    [Fact]
    public async Task OnBreakpointHit_ConditionEvaluation_ReceivesCorrectThreadId()
    {
        // Arrange
        _processDebuggerMock.Setup(x => x.IsAttached).Returns(false);
        const int testThreadId = 42;
        var breakpoint = await _manager.SetBreakpointAsync("/app/Program.cs", 10, condition: "threadId == 42");

        ConditionContext? capturedContext = null;
        _conditionEvaluatorMock
            .Setup(x => x.Evaluate("threadId == 42", It.IsAny<ConditionContext>()))
            .Callback<string?, ConditionContext>((_, ctx) => capturedContext = ctx)
            .Returns(ConditionResult.Ok(true));

        var hit = new BreakpointHit(
            BreakpointId: breakpoint.Id,
            ThreadId: testThreadId,
            Timestamp: DateTime.UtcNow,
            Location: breakpoint.Location,
            HitCount: 1);

        // Act
        _manager.OnBreakpointHit(hit);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.ThreadId.Should().Be(testThreadId,
            "condition evaluator must receive the correct thread ID");
    }
}
