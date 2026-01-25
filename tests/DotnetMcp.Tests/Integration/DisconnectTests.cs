using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for the disconnect workflow.
/// These tests verify disconnection from debug sessions.
/// </summary>
[Collection("ProcessTests")]
public class DisconnectTests : IDisposable
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public DisconnectTests()
    {
        _debuggerLoggerMock = new Mock<ILogger<ProcessDebugger>>();
        _managerLoggerMock = new Mock<ILogger<DebugSessionManager>>();
        _pdbSymbolReaderMock = new Mock<IPdbSymbolReader>();
        _processDebugger = new ProcessDebugger(_debuggerLoggerMock.Object, _pdbSymbolReaderMock.Object);
        _sessionManager = new DebugSessionManager(_processDebugger, _managerLoggerMock.Object);
    }

    public void Dispose()
    {
        _processDebugger.Dispose();
    }

    [Fact]
    public async Task DisconnectAsync_WhenNoSession_CompletesSuccessfully()
    {
        // Arrange - no session exists

        // Act
        var act = async () => await _sessionManager.DisconnectAsync();

        // Assert - should not throw
        await act.Should().NotThrowAsync("disconnect when no session should be no-op");
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhenNoSession_StateRemainsDisconnected()
    {
        // Arrange
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);

        // Act
        await _sessionManager.DisconnectAsync();

        // Assert
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
        _sessionManager.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task DisconnectAsync_ClearsCurrentSession()
    {
        // Arrange - set up a mock session
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.AttachAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Create a session
        await manager.AttachAsync(1234, TimeSpan.FromSeconds(30));
        manager.CurrentSession.Should().NotBeNull();

        // Act
        await manager.DisconnectAsync();

        // Assert
        manager.CurrentSession.Should().BeNull("session should be cleared after disconnect");
    }

    [Fact]
    public async Task DisconnectAsync_CallsDetachForAttachedProcess()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.AttachAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Create an attached session
        await manager.AttachAsync(1234, TimeSpan.FromSeconds(30));

        // Act
        await manager.DisconnectAsync(terminateProcess: false);

        // Assert - DetachAsync should be called
        mockDebugger.Verify(d => d.DetachAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockDebugger.Verify(d => d.TerminateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_AttachedProcessWithTerminateTrue_StillDetaches()
    {
        // Arrange - attached processes should be detached, not terminated
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.AttachAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Create an attached session
        await manager.AttachAsync(1234, TimeSpan.FromSeconds(30));

        // Act - even with terminateProcess=true, attached processes should be detached
        await manager.DisconnectAsync(terminateProcess: true);

        // Assert - Detach should be called, NOT Terminate (because it's attached, not launched)
        mockDebugger.Verify(d => d.DetachAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockDebugger.Verify(d => d.TerminateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_LaunchedProcessWithTerminateTrue_CallsTerminate()
    {
        // Arrange - launched processes should be terminated when terminateProcess=true
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.LaunchAsync(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 5678,
                Name: "launched-app",
                ExecutablePath: "/path/to/app.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Create a launched session
        await manager.LaunchAsync("/path/to/app.dll");

        // Act
        await manager.DisconnectAsync(terminateProcess: true);

        // Assert - TerminateAsync should be called for launched processes
        mockDebugger.Verify(d => d.TerminateAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockDebugger.Verify(d => d.DetachAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_LaunchedProcessWithTerminateFalse_CallsDetach()
    {
        // Arrange - launched processes with terminateProcess=false should be detached
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.LaunchAsync(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 5678,
                Name: "launched-app",
                ExecutablePath: "/path/to/app.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Create a launched session
        await manager.LaunchAsync("/path/to/app.dll");

        // Act
        await manager.DisconnectAsync(terminateProcess: false);

        // Assert - DetachAsync should be called
        mockDebugger.Verify(d => d.DetachAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockDebugger.Verify(d => d.TerminateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_SetsStateToDisconnected()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.AttachAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Create a session
        await manager.AttachAsync(1234, TimeSpan.FromSeconds(30));
        manager.GetCurrentState().Should().Be(SessionState.Running);

        // Act
        await manager.DisconnectAsync();

        // Assert
        manager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_CanReconnectAfterDisconnect()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.AttachAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // First connection
        await manager.AttachAsync(1234, TimeSpan.FromSeconds(30));
        await manager.DisconnectAsync();

        // Act - reconnect
        var session = await manager.AttachAsync(5678, TimeSpan.FromSeconds(30));

        // Assert
        session.Should().NotBeNull();
        manager.GetCurrentState().Should().Be(SessionState.Running);
    }

    [Fact]
    public async Task DisconnectAsync_MultipleCallsAreSafe()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger
            .Setup(d => d.AttachAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        await manager.AttachAsync(1234, TimeSpan.FromSeconds(30));

        // Act - disconnect multiple times
        await manager.DisconnectAsync();
        await manager.DisconnectAsync();
        await manager.DisconnectAsync();

        // Assert - should not throw, state should remain disconnected
        manager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }
}
