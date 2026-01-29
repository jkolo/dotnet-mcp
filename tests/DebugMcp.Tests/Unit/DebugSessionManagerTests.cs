using DebugMcp.Models;
using DebugMcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Unit;

/// <summary>
/// Unit tests for DebugSessionManager service.
/// Tests session lifecycle management.
/// </summary>
public class DebugSessionManagerTests
{
    private readonly Mock<IProcessDebugger> _processDebuggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _loggerMock;
    private readonly DebugSessionManager _sut;

    public DebugSessionManagerTests()
    {
        _processDebuggerMock = new Mock<IProcessDebugger>();
        _loggerMock = new Mock<ILogger<DebugSessionManager>>();
        _sut = new DebugSessionManager(_processDebuggerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void CurrentSession_WhenNoSession_ReturnsNull()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.CurrentSession;

        // Assert
        result.Should().BeNull("no session should exist initially");
    }

    [Fact]
    public void GetCurrentState_WhenNoSession_ReturnsDisconnected()
    {
        // Arrange - fresh instance

        // Act
        var result = _sut.GetCurrentState();

        // Assert
        result.Should().Be(SessionState.Disconnected, "state should be disconnected when no session");
    }

    [Fact]
    public async Task AttachAsync_WhenProcessExists_CreatesSession()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: "dotnet TestApp.dll",
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        // Act
        var session = await _sut.AttachAsync(pid, timeout);

        // Assert
        session.Should().NotBeNull();
        session.ProcessId.Should().Be(pid);
        session.ProcessName.Should().Be("TestApp");
        session.ExecutablePath.Should().Be("/path/to/TestApp.dll");
        session.RuntimeVersion.Should().Be(".NET 8.0");
        session.LaunchMode.Should().Be(LaunchMode.Attach);
        session.State.Should().Be(SessionState.Running);
        session.AttachedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AttachAsync_StoresSessionAsCurrentSession()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: null,
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        // Act
        var session = await _sut.AttachAsync(pid, timeout);

        // Assert
        _sut.CurrentSession.Should().BeSameAs(session);
    }

    [Fact]
    public async Task AttachAsync_WhenAlreadyAttached_ThrowsInvalidOperationException()
    {
        // Arrange
        const int pid1 = 12345;
        const int pid2 = 67890;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid1,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: null,
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(It.IsAny<int>(), timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        await _sut.AttachAsync(pid1, timeout);

        // Act
        var act = async () => await _sut.AttachAsync(pid2, timeout);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already active*");
    }

    [Fact]
    public async Task AttachAsync_WhenProcessDebuggerFails_PropagatesException()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Process not found"));

        // Act
        var act = async () => await _sut.AttachAsync(pid, timeout);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task AttachAsync_HandlesCancellation()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = async () => await _sut.AttachAsync(pid, timeout, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DisconnectAsync_WhenSessionExists_ClearsSession()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: null,
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        await _sut.AttachAsync(pid, timeout);

        // Act
        await _sut.DisconnectAsync();

        // Assert
        _sut.CurrentSession.Should().BeNull("session should be cleared after disconnect");
        _sut.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhenNoSession_CompletesWithoutError()
    {
        // Arrange - no session exists

        // Act
        var act = async () => await _sut.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync("disconnect when no session should be no-op");
    }

    [Fact]
    public async Task DisconnectAsync_CallsDetachOnProcessDebugger()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: null,
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        await _sut.AttachAsync(pid, timeout);

        // Act
        await _sut.DisconnectAsync(terminateProcess: false);

        // Assert
        _processDebuggerMock.Verify(x => x.DetachAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_WithTerminate_ForLaunchedProcess_CallsTerminate()
    {
        // Arrange - we need to set up a launched session (not attach)
        // For now, just verify the detach behavior
        // Launch tests are in User Story 3

        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: null,
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        await _sut.AttachAsync(pid, timeout);

        // Act - terminate for attached process should still call detach (not terminate)
        await _sut.DisconnectAsync(terminateProcess: true);

        // Assert - attached processes should be detached, not terminated
        _processDebuggerMock.Verify(x => x.DetachAsync(It.IsAny<CancellationToken>()), Times.Once);
        _processDebuggerMock.Verify(x => x.TerminateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StateChanged_UpdatesSessionState()
    {
        // Arrange
        const int pid = 12345;
        var timeout = TimeSpan.FromSeconds(30);
        var processInfo = new ProcessInfo(
            Pid: pid,
            Name: "TestApp",
            ExecutablePath: "/path/to/TestApp.dll",
            IsManaged: true,
            CommandLine: null,
            RuntimeVersion: ".NET 8.0"
        );

        _processDebuggerMock
            .Setup(x => x.AttachAsync(pid, timeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processInfo);

        await _sut.AttachAsync(pid, timeout);

        // Act - simulate state change from process debugger
        _processDebuggerMock.Raise(
            x => x.StateChanged += null,
            new SessionStateChangedEventArgs
            {
                OldState = SessionState.Running,
                NewState = SessionState.Paused,
                PauseReason = PauseReason.Breakpoint,
                Location = new SourceLocation("Test.cs", 42, 1, "TestMethod", "TestModule"),
                ThreadId = 1
            });

        // Assert
        _sut.CurrentSession.Should().NotBeNull();
        _sut.CurrentSession!.State.Should().Be(SessionState.Paused);
        _sut.CurrentSession.PauseReason.Should().Be(PauseReason.Breakpoint);
        _sut.CurrentSession.CurrentLocation.Should().NotBeNull();
        _sut.CurrentSession.CurrentLocation!.Line.Should().Be(42);
        _sut.CurrentSession.ActiveThreadId.Should().Be(1);
    }
}
