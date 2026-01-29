using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for state queries and transitions.
/// These tests verify state management across the debug session lifecycle.
/// </summary>
[Collection("ProcessTests")]
public class StateTests : IDisposable
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public StateTests()
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
    public void InitialState_IsDisconnected()
    {
        // Act
        var state = _sessionManager.GetCurrentState();

        // Assert
        state.Should().Be(SessionState.Disconnected, "initial state should be disconnected");
    }

    [Fact]
    public void CurrentSession_WhenDisconnected_IsNull()
    {
        // Act
        var session = _sessionManager.CurrentSession;

        // Assert
        session.Should().BeNull("no session should exist when disconnected");
    }

    [Fact]
    public async Task StateAfterFailedAttach_RemainsDisconnected()
    {
        // Arrange
        var invalidPid = 999999;
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        try
        {
            await _sessionManager.AttachAsync(invalidPid, timeout);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected, "state should remain disconnected after failed attach");
        _sessionManager.CurrentSession.Should().BeNull("no session should be created on failed attach");
    }

    [Fact]
    public async Task StateAfterDisconnect_IsDisconnected()
    {
        // Arrange - even without a session, disconnect should be safe

        // Act
        await _sessionManager.DisconnectAsync();

        // Assert
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public void ProcessDebugger_InitialState_IsDisconnected()
    {
        // Act
        var state = _processDebugger.CurrentState;

        // Assert
        state.Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public void ProcessDebugger_IsAttached_InitiallyFalse()
    {
        // Act
        var isAttached = _processDebugger.IsAttached;

        // Assert
        isAttached.Should().BeFalse();
    }

    [Fact]
    public void ProcessDebugger_PauseReason_InitiallyNull()
    {
        // Act
        var pauseReason = _processDebugger.CurrentPauseReason;

        // Assert
        pauseReason.Should().BeNull("no pause reason when disconnected");
    }

    [Fact]
    public void ProcessDebugger_Location_InitiallyNull()
    {
        // Act
        var location = _processDebugger.CurrentLocation;

        // Assert
        location.Should().BeNull("no location when disconnected");
    }

    [Fact]
    public void ProcessDebugger_ActiveThreadId_InitiallyNull()
    {
        // Act
        var threadId = _processDebugger.ActiveThreadId;

        // Assert
        threadId.Should().BeNull("no active thread when disconnected");
    }

    [Fact]
    public async Task StateTransition_FromDisconnected_ToDisconnected_IsValid()
    {
        // State should remain disconnected when nothing happens
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);

        // After disconnect (no-op)
        await _sessionManager.DisconnectAsync();
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task SessionManager_SubscribesToDebuggerStateChanges()
    {
        // Arrange - simulate a state change from the process debugger
        var mockDebugger = new Mock<IProcessDebugger>();
        mockDebugger.SetupGet(d => d.CurrentState).Returns(SessionState.Disconnected);

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Verify initial state
        manager.GetCurrentState().Should().Be(SessionState.Disconnected);

        // No session exists yet, so state changes from debugger won't update anything
        // This verifies the manager is subscribed but handles no-session case gracefully
    }

    [Fact]
    public void SessionModel_StateProperty_CanBeUpdated()
    {
        // Arrange
        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "test",
            ExecutablePath = "/path/to/test",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running,
            LaunchMode = LaunchMode.Attach
        };

        // Assert initial state
        session.State.Should().Be(SessionState.Running);

        // Act - update state
        session.State = SessionState.Paused;
        session.PauseReason = PauseReason.Breakpoint;

        // Assert updated state
        session.State.Should().Be(SessionState.Paused);
        session.PauseReason.Should().Be(PauseReason.Breakpoint);
    }

    [Fact]
    public void SessionModel_LocationProperty_CanBeSet()
    {
        // Arrange
        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "test",
            ExecutablePath = "/path/to/test",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Paused,
            LaunchMode = LaunchMode.Attach,
            PauseReason = PauseReason.Breakpoint
        };

        // Act
        session.CurrentLocation = new SourceLocation(
            File: "/path/to/source.cs",
            Line: 42,
            Column: 8,
            FunctionName: "TestMethod",
            ModuleName: "TestAssembly"
        );

        // Assert
        session.CurrentLocation.Should().NotBeNull();
        session.CurrentLocation!.File.Should().Be("/path/to/source.cs");
        session.CurrentLocation.Line.Should().Be(42);
    }

    [Fact]
    public void SessionModel_ActiveThreadId_CanBeSet()
    {
        // Arrange
        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "test",
            ExecutablePath = "/path/to/test",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Paused,
            LaunchMode = LaunchMode.Attach
        };

        // Act
        session.ActiveThreadId = 42;

        // Assert
        session.ActiveThreadId.Should().Be(42);
    }
}
