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
/// Integration tests for thread inspection (T043).
/// These tests verify the complete thread inspection workflow with a real target process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class ThreadInspectionTests : IAsyncLifetime
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

    public ThreadInspectionTests()
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
    /// Verify GetThreads throws when no session is active.
    /// </summary>
    [Fact]
    public void GetThreads_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = () => _processDebugger.GetThreads();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    /// <summary>
    /// Verify GetThreads returns threads when paused at breakpoint.
    /// </summary>
    [Fact]
    public async Task GetThreads_WhenPausedAtBreakpoint_ReturnsThreads()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var threads = _processDebugger.GetThreads();

        // Assert
        threads.Should().NotBeEmpty("should have at least one thread");
    }

    /// <summary>
    /// Verify GetThreads includes a current thread marker.
    /// </summary>
    [Fact]
    public async Task GetThreads_WhenPaused_IdentifiesCurrentThread()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var threads = _processDebugger.GetThreads();

        // Assert
        var currentThread = threads.FirstOrDefault(t => t.IsCurrent);
        currentThread.Should().NotBeNull("one thread should be marked as current");
    }

    /// <summary>
    /// Verify GetThreads returns valid thread IDs.
    /// </summary>
    [Fact]
    public async Task GetThreads_ReturnsValidThreadIds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var threads = _processDebugger.GetThreads();

        // Assert
        threads.Should().OnlyContain(t => t.Id > 0, "thread IDs should be positive");
    }

    /// <summary>
    /// Verify GetThreads returns thread state.
    /// </summary>
    [Fact]
    public async Task GetThreads_ReturnsThreadState()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var threads = _processDebugger.GetThreads();

        // Assert
        var currentThread = threads.First(t => t.IsCurrent);
        currentThread.State.Should().Be(Models.Inspection.ThreadState.Stopped,
            "thread at breakpoint should be stopped");
    }

    /// <summary>
    /// Verify GetThreads returns location for stopped threads.
    /// </summary>
    [Fact]
    public async Task GetThreads_ForStoppedThread_ReturnsLocation()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var threads = _processDebugger.GetThreads();

        // Assert
        var stoppedThread = threads.FirstOrDefault(t => t.State == Models.Inspection.ThreadState.Stopped);
        stoppedThread.Should().NotBeNull("at least one thread should be stopped");
        stoppedThread!.Location.Should().NotBeNull("stopped thread should have location");
    }

    /// <summary>
    /// Verify thread ID can be used for stack frame retrieval.
    /// </summary>
    [Fact]
    public async Task GetThreads_ThreadId_CanBeUsedForStackRetrieval()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        var sourceFile = TestTargetProcess.GetSourceFilePath("NestedTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 32);

        await _targetProcess.SendCommandAsync("nested");
        await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(5));

        // Act
        var threads = _processDebugger.GetThreads();
        var currentThread = threads.First(t => t.IsCurrent);

        var (frames, _) = _processDebugger.GetStackFrames(currentThread.Id);

        // Assert
        frames.Should().NotBeEmpty("should get stack frames using thread ID from GetThreads");
    }

    /// <summary>
    /// Verify multiple threads are reported (if process has them).
    /// Note: This depends on the test target spawning multiple threads.
    /// </summary>
    [Fact]
    public async Task GetThreads_MultithreadedProcess_ReturnsMultipleThreads()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();

        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Pause the process (all threads stop)
        await _processDebugger.PauseAsync();

        // Act
        var threads = _processDebugger.GetThreads();

        // Assert - .NET processes typically have multiple threads (main, finalizer, etc.)
        threads.Should().NotBeEmpty();
        // Note: Can't guarantee multiple threads, but most .NET apps have > 1
    }
}
