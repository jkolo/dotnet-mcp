using DebugMcp.Models;
using DebugMcp.Models.Inspection;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for debug pause functionality (T073).
/// These tests verify the pause workflow with a real target process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class PauseTests : IAsyncLifetime
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

    public PauseTests()
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
    /// Verify PauseAsync throws when no session is active.
    /// </summary>
    [Fact]
    public async Task PauseAsync_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _processDebugger.PauseAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    /// <summary>
    /// Verify PauseAsync stops a running process.
    /// </summary>
    [Fact]
    public async Task PauseAsync_RunningProcess_StopsExecution()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Verify process is running
        _processDebugger.CurrentState.Should().Be(SessionState.Running);

        // Act
        var threads = await _processDebugger.PauseAsync();

        // Assert
        _processDebugger.CurrentState.Should().Be(SessionState.Paused);
        threads.Should().NotBeEmpty("should return thread information");
    }

    /// <summary>
    /// Verify PauseAsync returns thread information.
    /// </summary>
    [Fact]
    public async Task PauseAsync_ReturnsThreadInfo()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Act
        var threads = await _processDebugger.PauseAsync();

        // Assert
        threads.Should().NotBeEmpty();
        threads.Should().OnlyContain(t => t.Id > 0, "all threads should have valid IDs");
    }

    /// <summary>
    /// Verify PauseAsync is idempotent (calling on already paused is no-op).
    /// </summary>
    [Fact]
    public async Task PauseAsync_AlreadyPaused_IsIdempotent()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // First pause
        var threads1 = await _processDebugger.PauseAsync();

        // Act - second pause
        var threads2 = await _processDebugger.PauseAsync();

        // Assert
        threads2.Should().NotBeEmpty("should return thread info even when already paused");
        _processDebugger.CurrentState.Should().Be(SessionState.Paused);
    }

    /// <summary>
    /// Verify PauseAsync sets pause reason.
    /// </summary>
    [Fact]
    public async Task PauseAsync_SetsPauseReason()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Act
        await _processDebugger.PauseAsync();

        // Assert
        _processDebugger.CurrentPauseReason.Should().Be(PauseReason.Pause);
    }

    /// <summary>
    /// Verify paused state allows inspection.
    /// </summary>
    [Fact]
    public async Task PauseAsync_AllowsInspection()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Trigger managed code execution before pausing to ensure we have managed frames
        // Without this, the process might be in native code (Console.ReadLine)
        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32); // Level3
        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act - try to get threads and stack
        var threads = _processDebugger.GetThreads();
        var currentThread = threads.First(t => t.IsCurrent);
        var (frames, _) = _processDebugger.GetStackFrames(currentThread.Id);

        // Assert
        threads.Should().NotBeEmpty();
        frames.Should().NotBeEmpty("should be able to get stack frames when paused at breakpoint");
    }

    /// <summary>
    /// Verify pause then continue workflow.
    /// </summary>
    [Fact]
    public async Task PauseAsync_ThenContinue_ResumesExecution()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));
        await _processDebugger.PauseAsync();

        _processDebugger.CurrentState.Should().Be(SessionState.Paused);

        // Act
        await _processDebugger.ContinueAsync();

        // Assert
        _processDebugger.CurrentState.Should().Be(SessionState.Running);
    }

    /// <summary>
    /// Verify pause during loop execution.
    /// </summary>
    [Fact]
    public async Task PauseAsync_DuringLoop_StopsExecution()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Start a loop in the target
        await _targetProcess.SendCommandAsync("loop");

        // Give the loop time to start running
        await Task.Delay(100);

        // Act
        var threads = await _processDebugger.PauseAsync();

        // Assert
        _processDebugger.CurrentState.Should().Be(SessionState.Paused);
        threads.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verify pause selects an active thread.
    /// </summary>
    [Fact]
    public async Task PauseAsync_SelectsActiveThread()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Act
        var threads = await _processDebugger.PauseAsync();

        // Assert
        _processDebugger.ActiveThreadId.Should().NotBeNull("should select an active thread");
        var currentThread = threads.FirstOrDefault(t => t.IsCurrent);
        currentThread.Should().NotBeNull("one thread should be marked as current");
        currentThread!.Id.Should().Be(_processDebugger.ActiveThreadId!.Value);
    }
}
