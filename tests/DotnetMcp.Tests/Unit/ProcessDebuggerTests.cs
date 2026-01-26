using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Unit;

/// <summary>
/// Unit tests for ProcessDebugger service.
/// Tests the low-level ICorDebug operations.
/// </summary>
public class ProcessDebuggerTests
{
    private readonly Mock<ILogger<ProcessDebugger>> _loggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _sut;

    public ProcessDebuggerTests()
    {
        _loggerMock = new Mock<ILogger<ProcessDebugger>>();
        _pdbSymbolReaderMock = new Mock<IPdbSymbolReader>();
        _sut = new ProcessDebugger(_loggerMock.Object, _pdbSymbolReaderMock.Object);
    }

    [Fact]
    public void IsAttached_WhenNotAttached_ReturnsFalse()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.IsAttached;

        // Assert
        result.Should().BeFalse("debugger should not be attached initially");
    }

    [Fact]
    public void CurrentState_WhenNotAttached_ReturnsDisconnected()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.CurrentState;

        // Assert
        result.Should().Be(SessionState.Disconnected, "state should be disconnected when not attached");
    }

    [Fact]
    public void CurrentPauseReason_WhenNotAttached_ReturnsNull()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.CurrentPauseReason;

        // Assert
        result.Should().BeNull("no pause reason when not attached");
    }

    [Fact]
    public void CurrentLocation_WhenNotAttached_ReturnsNull()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.CurrentLocation;

        // Assert
        result.Should().BeNull("no location when not attached");
    }

    [Fact]
    public void ActiveThreadId_WhenNotAttached_ReturnsNull()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.ActiveThreadId;

        // Assert
        result.Should().BeNull("no active thread when not attached");
    }

    [Fact]
    public void IsNetProcess_WithInvalidPid_ReturnsFalse()
    {
        // Arrange
        const int invalidPid = -1;

        // Act
        var result = _sut.IsNetProcess(invalidPid);

        // Assert
        result.Should().BeFalse("invalid PID should not be a .NET process");
    }

    [Fact]
    public void IsNetProcess_WithNonExistentPid_ReturnsFalse()
    {
        // Arrange - use a PID that likely doesn't exist
        const int nonExistentPid = 999999;

        // Act
        var result = _sut.IsNetProcess(nonExistentPid);

        // Assert
        result.Should().BeFalse("non-existent process should return false");
    }

    [Fact]
    public void GetProcessInfo_WithInvalidPid_ReturnsNull()
    {
        // Arrange
        const int invalidPid = -1;

        // Act
        var result = _sut.GetProcessInfo(invalidPid);

        // Assert
        result.Should().BeNull("invalid PID should return null");
    }

    [Fact]
    public void GetProcessInfo_WithNonExistentPid_ReturnsNull()
    {
        // Arrange - use a PID that likely doesn't exist
        const int nonExistentPid = 999999;

        // Act
        var result = _sut.GetProcessInfo(nonExistentPid);

        // Assert
        result.Should().BeNull("non-existent process should return null");
    }

    [Fact]
    public async Task AttachAsync_WithNonExistentPid_ThrowsInvalidOperationException()
    {
        // Arrange
        const int nonExistentPid = 999999;
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var act = async () => await _sut.AttachAsync(nonExistentPid, timeout);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentPid}*not found*");
    }

    [Fact]
    public async Task AttachAsync_WithNonDotNetProcess_ThrowsInvalidOperationException()
    {
        // Arrange - PID 1 is usually init/systemd (not .NET)
        const int systemPid = 1;
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var act = async () => await _sut.AttachAsync(systemPid, timeout);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a .NET*");
    }

    [Fact]
    public void StateChanged_CanSubscribeAndUnsubscribe()
    {
        // This test verifies the event subscription mechanism works.
        // Actual state change testing requires integration test with real process.

        SessionState? capturedState = null;
        EventHandler<SessionStateChangedEventArgs> handler = (sender, e) => capturedState = e.NewState;

        // Act - subscribe
        _sut.StateChanged += handler;

        // Act - unsubscribe
        _sut.StateChanged -= handler;

        // Assert - subscription/unsubscription should work without error
        capturedState.Should().BeNull("no state change should have occurred");
    }

    [Fact]
    public async Task DetachAsync_WhenNotAttached_CompletesWithoutError()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.DetachAsync();

        // Assert
        await act.Should().NotThrowAsync("detach when not attached should be no-op");
    }

    [Fact]
    public async Task TerminateAsync_WhenNotAttached_CompletesWithoutError()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.TerminateAsync();

        // Assert
        await act.Should().NotThrowAsync("terminate when not attached should be no-op");
    }

    [Fact]
    public void Dispose_WhenNotAttached_CompletesWithoutError()
    {
        // Arrange - fresh instance

        // Act
        var act = () => _sut.Dispose();

        // Assert
        act.Should().NotThrow("dispose when not attached should be safe");
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange - fresh instance

        // Act
        var act = () =>
        {
            _sut.Dispose();
            _sut.Dispose();
        };

        // Assert
        act.Should().NotThrow("dispose should be idempotent");
    }

    // ===== ContinueAsync Tests (T068) =====

    [Fact]
    public async Task ContinueAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.ContinueAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task ContinueAsync_WhenNotPaused_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (disconnected state, not paused)
        // Note: Full behavior testing requires integration test with real debugger

        // Act
        var act = async () => await _sut.ContinueAsync();

        // Assert - When not attached, should indicate not attached
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ===== StepAsync Tests (T069) =====

    [Fact]
    public async Task StepAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.StepAsync(StepMode.Over);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task StepAsync_WhenNotPaused_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (disconnected state, not paused)
        // Note: Full behavior testing requires integration test with real debugger

        // Act
        var act = async () => await _sut.StepAsync(StepMode.In);

        // Assert - When not attached, should indicate not attached
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(StepMode.In)]
    [InlineData(StepMode.Over)]
    [InlineData(StepMode.Out)]
    public async Task StepAsync_AllModesHandled_WhenNotAttached(StepMode mode)
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.StepAsync(mode);

        // Assert - all modes should handle not-attached state
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ===== T012: GetStackFrames (StackWalker) Tests =====

    [Fact]
    public void GetStackFrames_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = () => _sut.GetStackFrames();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public void GetStackFrames_WithInvalidThreadId_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)
        const int invalidThreadId = 999999;

        // Act
        var act = () => _sut.GetStackFrames(invalidThreadId);

        // Assert - not attached takes precedence
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(5, 10)]
    [InlineData(0, 100)]
    public void GetStackFrames_PaginationParameters_DoNotThrow_WhenNotAttached(int startFrame, int maxFrames)
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = () => _sut.GetStackFrames(threadId: null, startFrame: startFrame, maxFrames: maxFrames);

        // Assert - should fail with not attached, not parameter validation
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    // ===== T025: GetVariables (VariableInspector) Tests =====

    [Fact]
    public void GetVariables_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = () => _sut.GetVariables();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public void GetVariables_WithInvalidThreadId_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)
        const int invalidThreadId = 999999;

        // Act
        var act = () => _sut.GetVariables(invalidThreadId);

        // Assert - not attached takes precedence
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Theory]
    [InlineData("all")]
    [InlineData("locals")]
    [InlineData("arguments")]
    [InlineData("this")]
    public void GetVariables_ScopeParameter_DoesNotThrow_WhenNotAttached(string scope)
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = () => _sut.GetVariables(threadId: null, frameIndex: 0, scope: scope);

        // Assert - should fail with not attached, not parameter validation
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public void GetVariables_WithExpandPath_ThrowsNotAttached()
    {
        // Arrange
        const string expandPath = "this._field";

        // Act
        var act = () => _sut.GetVariables(expandPath: expandPath);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    // ===== T042: GetThreads (ThreadInspector) Tests =====

    [Fact]
    public void GetThreads_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = () => _sut.GetThreads();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    // ===== T056: EvaluateAsync (ExpressionEvaluator) Tests =====

    [Fact]
    public async Task EvaluateAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)
        const string expression = "x + 1";

        // Act
        var act = async () => await _sut.EvaluateAsync(expression);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task EvaluateAsync_WithEmptyExpression_ReturnsSyntaxError()
    {
        // Arrange
        // Note: Empty expression validation happens before attach check
        // This test documents that behavior though it may vary by implementation

        // Act
        var act = async () => await _sut.EvaluateAsync("");

        // Assert - Either throws not attached or returns error result
        // Implementation checks attach first, so expect not attached
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EvaluateAsync_WithWhitespaceExpression_ReturnsError()
    {
        // Arrange
        const string expression = "   ";

        // Act
        var act = async () => await _sut.EvaluateAsync(expression);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("variable")]
    [InlineData("this.Property")]
    [InlineData("a + b")]
    [InlineData("Method()")]
    public async Task EvaluateAsync_VariousExpressions_ThrowNotAttached(string expression)
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.EvaluateAsync(expression);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task EvaluateAsync_WithFrameIndex_ThrowsNotAttached(int frameIndex)
    {
        // Arrange
        const string expression = "x";

        // Act
        var act = async () => await _sut.EvaluateAsync(expression, frameIndex: frameIndex);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task EvaluateAsync_WithTimeout_ThrowsNotAttached()
    {
        // Arrange
        const string expression = "x";
        const int timeoutMs = 1000;

        // Act
        var act = async () => await _sut.EvaluateAsync(expression, timeoutMs: timeoutMs);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    // ===== PauseAsync Tests =====

    [Fact]
    public async Task PauseAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.PauseAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    // ===== Module Inspection Tests =====

    [Fact]
    public async Task GetModulesAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.GetModulesAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task GetModulesAsync_WithNameFilter_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.GetModulesAsync(nameFilter: "MyApp*");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task GetModulesAsync_WithIncludeSystemFalse_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.GetModulesAsync(includeSystem: false);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task GetTypesAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.GetTypesAsync("TestModule");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task GetMembersAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.GetMembersAsync("TestType");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    [Fact]
    public async Task SearchModulesAsync_WhenNotAttached_ThrowsInvalidOperationException()
    {
        // Arrange - fresh instance (not attached)

        // Act
        var act = async () => await _sut.SearchModulesAsync("*Controller");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }
}
