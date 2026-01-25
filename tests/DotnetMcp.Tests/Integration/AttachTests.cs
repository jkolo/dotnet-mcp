using DotnetMcp.Models;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for the attach workflow.
/// These tests verify end-to-end attachment to real .NET processes.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class AttachTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private TestTargetProcess? _targetProcess;

    public AttachTests()
    {
        _debuggerLoggerMock = new Mock<ILogger<ProcessDebugger>>();
        _managerLoggerMock = new Mock<ILogger<DebugSessionManager>>();
        _pdbSymbolReaderMock = new Mock<IPdbSymbolReader>();
        _processDebugger = new ProcessDebugger(_debuggerLoggerMock.Object, _pdbSymbolReaderMock.Object);
        _sessionManager = new DebugSessionManager(_processDebugger, _managerLoggerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _sessionManager.DisconnectAsync();
        _processDebugger.Dispose();
        _targetProcess?.Dispose();
    }

    [Fact]
    public void IsNetProcess_WithCurrentProcess_ReturnsTrue()
    {
        // Arrange - the test process itself is .NET
        var currentPid = Environment.ProcessId;

        // Act
        var result = _processDebugger.IsNetProcess(currentPid);

        // Assert
        result.Should().BeTrue("the test process is a .NET process");
    }

    [Fact]
    public void GetProcessInfo_WithCurrentProcess_ReturnsValidInfo()
    {
        // Arrange - the test process itself is .NET
        var currentPid = Environment.ProcessId;

        // Act
        var info = _processDebugger.GetProcessInfo(currentPid);

        // Assert
        info.Should().NotBeNull();
        info!.Pid.Should().Be(currentPid);
        info.Name.Should().NotBeNullOrEmpty();
        info.ExecutablePath.Should().NotBeNullOrEmpty();
        info.IsManaged.Should().BeTrue();
    }

    [Fact]
    public void IsNetProcess_WithPid1_ReturnsFalse()
    {
        // Arrange - PID 1 is init/systemd on Linux (not .NET)
        const int systemPid = 1;

        // Act
        var result = _processDebugger.IsNetProcess(systemPid);

        // Assert
        result.Should().BeFalse("system process is not a .NET process");
    }

    [Fact]
    public async Task AttachAsync_WithInvalidPid_ThrowsException()
    {
        // Arrange
        const int invalidPid = 999999;
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var act = async () => await _sessionManager.AttachAsync(invalidPid, timeout);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AttachAsync_WithNonDotNetProcess_ThrowsException()
    {
        // Arrange - PID 1 is init/systemd
        const int systemPid = 1;
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var act = async () => await _sessionManager.AttachAsync(systemPid, timeout);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a .NET*");
    }

    [Fact]
    public void CurrentSessionWorkflow_AttachThenDisconnect()
    {
        // This is a workflow test that verifies the session manager state transitions
        // Without actual process attachment (which requires elevated permissions)

        // Initial state
        _sessionManager.CurrentSession.Should().BeNull();
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);

        // After disconnect (no-op when nothing attached)
        var disconnectTask = _sessionManager.DisconnectAsync();
        disconnectTask.IsCompleted.Should().BeTrue();
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task AttachAsync_ThenDisconnect_ClearsSession()
    {
        // This test requires mocking or a real .NET process
        // For now, verify the error path

        var timeout = TimeSpan.FromSeconds(5);

        // Try to attach to a non-existent process
        try
        {
            await _sessionManager.AttachAsync(999999, timeout);
        }
        catch (InvalidOperationException)
        {
            // Expected - process not found
        }

        // Session should not be created on failed attach
        _sessionManager.CurrentSession.Should().BeNull();
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);

        // Disconnect should be no-op
        await _sessionManager.DisconnectAsync();
        _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AttachAsync_WithRealDotNetProcess_AttachesSuccessfully()
    {
        // This test verifies attachment to a real .NET process using dbgshim.
        // NOTE: This test must be run in isolation due to native debugging library constraints.
        // Run with: dotnet test --filter "FullyQualifiedName~AttachAsync_WithRealDotNetProcess"

        // Arrange - start the test target process
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        _targetProcess.IsRunning.Should().BeTrue("test target should be running");

        var targetPid = _targetProcess.ProcessId;
        var timeout = TimeSpan.FromSeconds(30);

        // Verify it's detected as a .NET process
        _processDebugger.IsNetProcess(targetPid).Should().BeTrue("test target is a .NET process");

        // Act - attach to the test target process
        try
        {
            var session = await _sessionManager.AttachAsync(targetPid, timeout);

            // Assert
            session.Should().NotBeNull();
            session.ProcessId.Should().Be(targetPid);
            session.State.Should().Be(SessionState.Running);
            session.LaunchMode.Should().Be(LaunchMode.Attach);

            // Cleanup
            await _sessionManager.DisconnectAsync();
            _sessionManager.GetCurrentState().Should().Be(SessionState.Disconnected);
        }
        catch (InvalidOperationException ex)
        {
            // If attach fails, it should NOT be because dbgshim is missing
            ex.Message.Should().NotContain("dbgshim", "dbgshim should be found");
            // Re-throw to fail the test with the actual error
            throw;
        }
    }
}
