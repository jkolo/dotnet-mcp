using System.Diagnostics;
using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Performance;

/// <summary>
/// Performance tests validating success criteria SC-001 and SC-002.
/// SC-001: attach_process completes within 5 seconds for typical .NET process
/// SC-002: debug_state queries return within 100ms
/// </summary>
[Collection("ProcessTests")]
public class PerformanceTests : IDisposable
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public PerformanceTests()
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

    /// <summary>
    /// SC-002: debug_state queries return within 100ms.
    /// This tests the synchronous state query path.
    /// </summary>
    [Fact]
    public void GetCurrentState_Disconnected_ReturnsWithin100ms()
    {
        // Arrange
        const int maxMs = 100;
        var sw = Stopwatch.StartNew();

        // Act
        var state = _sessionManager.GetCurrentState();

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, $"SC-002: state query should complete within {maxMs}ms");
        state.Should().Be(SessionState.Disconnected);
    }

    /// <summary>
    /// SC-002: CurrentSession property access returns within 100ms.
    /// </summary>
    [Fact]
    public void CurrentSession_Access_ReturnsWithin100ms()
    {
        // Arrange
        const int maxMs = 100;
        var sw = Stopwatch.StartNew();

        // Act
        var session = _sessionManager.CurrentSession;

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, $"SC-002: session access should complete within {maxMs}ms");
        session.Should().BeNull();
    }

    /// <summary>
    /// SC-002: ProcessDebugger property access is fast.
    /// </summary>
    [Fact]
    public void ProcessDebugger_PropertyAccess_ReturnsWithin100ms()
    {
        // Arrange
        const int maxMs = 100;
        var sw = Stopwatch.StartNew();

        // Act - access all properties
        var isAttached = _processDebugger.IsAttached;
        var state = _processDebugger.CurrentState;
        var pauseReason = _processDebugger.CurrentPauseReason;
        var location = _processDebugger.CurrentLocation;
        var threadId = _processDebugger.ActiveThreadId;

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, $"SC-002: property access should complete within {maxMs}ms");
    }

    /// <summary>
    /// SC-002: State query with active session returns within 100ms.
    /// </summary>
    [Fact]
    public async Task GetCurrentState_WithSession_ReturnsWithin100ms()
    {
        // Arrange - set up mock session
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

        const int maxMs = 100;
        var sw = Stopwatch.StartNew();

        // Act
        var state = manager.GetCurrentState();
        var session = manager.CurrentSession;

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, $"SC-002: state query should complete within {maxMs}ms");
        state.Should().Be(SessionState.Running);
        session.Should().NotBeNull();
    }

    /// <summary>
    /// SC-002: Multiple state queries in sequence stay within bounds.
    /// </summary>
    [Fact]
    public void GetCurrentState_MultipleQueries_AllReturnWithin100ms()
    {
        // Arrange
        const int queryCount = 100;
        const int maxTotalMs = 100; // All 100 queries should complete within 100ms total
        var sw = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < queryCount; i++)
        {
            _ = _sessionManager.GetCurrentState();
            _ = _sessionManager.CurrentSession;
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxTotalMs, $"SC-002: {queryCount} queries should complete within {maxTotalMs}ms total");
    }

    /// <summary>
    /// SC-001: Attach validation fails fast for invalid PID.
    /// </summary>
    [Fact]
    public async Task AttachAsync_InvalidPid_FailsFast()
    {
        // Arrange
        const int invalidPid = 999999;
        const int maxMs = 1000; // Should fail within 1 second (well under 5s)
        var sw = Stopwatch.StartNew();

        // Act
        try
        {
            await _sessionManager.AttachAsync(invalidPid, TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, "Invalid PID should fail fast");
    }

    /// <summary>
    /// SC-001: IsNetProcess check is fast.
    /// </summary>
    [Fact]
    public void IsNetProcess_CurrentProcess_CompletesQuickly()
    {
        // Arrange
        var currentPid = Environment.ProcessId;
        const int maxMs = 500; // Should complete within 500ms
        var sw = Stopwatch.StartNew();

        // Act
        var isNet = _processDebugger.IsNetProcess(currentPid);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, "IsNetProcess should complete quickly");
        isNet.Should().BeTrue();
    }

    /// <summary>
    /// SC-001: GetProcessInfo is fast.
    /// </summary>
    [Fact]
    public void GetProcessInfo_CurrentProcess_CompletesQuickly()
    {
        // Arrange
        var currentPid = Environment.ProcessId;
        const int maxMs = 500; // Should complete within 500ms
        var sw = Stopwatch.StartNew();

        // Act
        var info = _processDebugger.GetProcessInfo(currentPid);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, "GetProcessInfo should complete quickly");
        info.Should().NotBeNull();
    }

    /// <summary>
    /// Disconnect completes quickly.
    /// </summary>
    [Fact]
    public async Task DisconnectAsync_WhenNotAttached_CompletesQuickly()
    {
        // Arrange
        const int maxMs = 100;
        var sw = Stopwatch.StartNew();

        // Act
        await _sessionManager.DisconnectAsync();

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs, "Disconnect when not attached should complete quickly");
    }

    /// <summary>
    /// Memory usage: DebugSession model is lightweight.
    /// </summary>
    [Fact]
    public void DebugSession_MemoryFootprint_IsReasonable()
    {
        // Arrange - create many sessions
        const int sessionCount = 10000;
        var sessions = new List<DebugSession>(sessionCount);

        var beforeMem = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < sessionCount; i++)
        {
            sessions.Add(new DebugSession
            {
                ProcessId = i,
                ProcessName = $"process_{i}",
                ExecutablePath = $"/path/to/process_{i}.dll",
                RuntimeVersion = ".NET 8.0",
                AttachedAt = DateTime.UtcNow,
                State = SessionState.Running,
                LaunchMode = LaunchMode.Attach
            });
        }

        var afterMem = GC.GetTotalMemory(true);
        var memPerSession = (afterMem - beforeMem) / (double)sessionCount;

        // Assert - each session should use less than 1KB on average
        memPerSession.Should().BeLessThan(1024, "Each DebugSession should use less than 1KB memory");
    }
}
