using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for debugger reattachment capability (Bug #3 fix).
/// Tests verify that the debugger can attach, disconnect, and reattach
/// to processes without requiring MCP server restart.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class ReattachmentTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private readonly List<TestTargetProcess> _targetProcesses = new();

    public ReattachmentTests()
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
        try
        {
            await _sessionManager.DisconnectAsync();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _processDebugger.Dispose();

        foreach (var process in _targetProcesses)
        {
            process.Dispose();
        }
        _targetProcesses.Clear();
    }

    private async Task<TestTargetProcess> StartTargetProcessAsync()
    {
        var process = new TestTargetProcess();
        await process.StartAsync();
        _targetProcesses.Add(process);
        return process;
    }

    /// <summary>
    /// T001: Basic Reattachment Cycle
    /// Given MCP server is running and TestTargetApp is started
    /// When I attach, disconnect, then attach again
    /// Then both attach operations succeed
    /// </summary>
    [Fact]
    public async Task BasicReattachmentCycle_ShouldSucceed()
    {
        // Arrange - Start first target process
        var processA = await StartTargetProcessAsync();
        var timeout = TimeSpan.FromSeconds(30);

        // Act & Assert - First attach
        await _sessionManager.AttachAsync(processA.ProcessId, timeout);
        var stateAfterFirstAttach = _sessionManager.GetCurrentState();
        stateAfterFirstAttach.Should().Be(SessionState.Running, "first attach should succeed");

        // Act - Disconnect
        await _sessionManager.DisconnectAsync();
        var stateAfterDisconnect = _sessionManager.GetCurrentState();
        stateAfterDisconnect.Should().Be(SessionState.Disconnected, "disconnect should succeed");

        // Arrange - Start second target process
        var processB = await StartTargetProcessAsync();

        // Act & Assert - Second attach (this was failing before the fix)
        await _sessionManager.AttachAsync(processB.ProcessId, timeout);
        var stateAfterSecondAttach = _sessionManager.GetCurrentState();
        stateAfterSecondAttach.Should().Be(SessionState.Running, "second attach should succeed without MCP restart");
    }

    /// <summary>
    /// T002: Multiple Cycle Stress Test
    /// Given MCP server is running
    /// When I perform 10 attach/disconnect cycles
    /// Then all cycles complete successfully
    /// </summary>
    [Fact]
    public async Task MultipleCycles_TenTimes_AllSucceed()
    {
        const int cycleCount = 10;
        var timeout = TimeSpan.FromSeconds(30);

        for (int i = 0; i < cycleCount; i++)
        {
            // Arrange - Start a new target process for this cycle
            var process = await StartTargetProcessAsync();

            // Act - Attach
            await _sessionManager.AttachAsync(process.ProcessId, timeout);

            var stateAfterAttach = _sessionManager.GetCurrentState();
            stateAfterAttach.Should().Be(SessionState.Running,
                $"attach cycle {i + 1} should succeed");

            // Act - Pause to verify debugger is functional
            await _sessionManager.PauseAsync();

            var stateAfterPause = _sessionManager.GetCurrentState();
            stateAfterPause.Should().Be(SessionState.Paused,
                $"pause in cycle {i + 1} should succeed");

            // Act - Disconnect
            await _sessionManager.DisconnectAsync();

            var stateAfterDisconnect = _sessionManager.GetCurrentState();
            stateAfterDisconnect.Should().Be(SessionState.Disconnected,
                $"disconnect cycle {i + 1} should succeed");

            // Cleanup - Kill the process for this cycle
            process.Kill();
        }
    }

    /// <summary>
    /// T003: Reattach After Process Termination
    /// Given debugger is attached to a process
    /// When the process terminates unexpectedly
    /// And I attempt to attach to a new process
    /// Then attachment succeeds
    /// </summary>
    [Fact]
    public async Task ReattachAfterProcessTermination_ShouldSucceed()
    {
        // Arrange - Start first target process
        var processA = await StartTargetProcessAsync();
        var timeout = TimeSpan.FromSeconds(30);

        // Act - Attach to first process
        await _sessionManager.AttachAsync(processA.ProcessId, timeout);
        var stateAfterAttach = _sessionManager.GetCurrentState();
        stateAfterAttach.Should().Be(SessionState.Running);

        // Act - Kill the process externally (simulating unexpected termination)
        processA.Kill();

        // Wait a bit for the debugger to notice process termination
        await Task.Delay(500);

        // The session may have transitioned to Disconnected or may still show Running
        // Either way, we should be able to disconnect cleanly
        try
        {
            await _sessionManager.DisconnectAsync();
        }
        catch
        {
            // Disconnect might fail if process already gone - that's ok
        }

        // Arrange - Start second target process
        var processB = await StartTargetProcessAsync();

        // Act & Assert - Attach to new process should succeed
        await _sessionManager.AttachAsync(processB.ProcessId, timeout);
        var stateAfterSecondAttach = _sessionManager.GetCurrentState();
        stateAfterSecondAttach.Should().Be(SessionState.Running,
            "attach after process termination should succeed");
    }

    /// <summary>
    /// E001: Attach While Already Attached
    /// Given debugger is attached to process A
    /// When I try to attach to process B without disconnecting
    /// Then clear error is returned
    /// </summary>
    [Fact]
    public async Task AttachWhileAlreadyAttached_ShouldReturnError()
    {
        // Arrange - Start two target processes
        var processA = await StartTargetProcessAsync();
        var processB = await StartTargetProcessAsync();
        var timeout = TimeSpan.FromSeconds(30);

        // Act - Attach to first process
        await _sessionManager.AttachAsync(processA.ProcessId, timeout);

        // Act & Assert - Try to attach to second without disconnecting
        var act = async () => await _sessionManager.AttachAsync(processB.ProcessId, timeout);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already*", "should indicate already attached");
    }
}
