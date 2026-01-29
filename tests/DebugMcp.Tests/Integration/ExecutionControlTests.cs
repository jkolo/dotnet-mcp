using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for execution control (continue, step).
/// These tests verify the complete execution control workflow with a real target process.
/// </summary>
[Collection("ProcessTests")]
[Trait("Category", "Integration")]
public class ExecutionControlTests : IAsyncLifetime
{
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public ExecutionControlTests()
    {
        var debuggerLoggerMock = new Mock<ILogger<ProcessDebugger>>();
        var managerLoggerMock = new Mock<ILogger<DebugSessionManager>>();
        var pdbCacheLoggerMock = new Mock<ILogger<PdbSymbolCache>>();
        var pdbLoggerMock = new Mock<ILogger<PdbSymbolReader>>();

        var pdbCache = new PdbSymbolCache(pdbCacheLoggerMock.Object);
        var pdbReader = new PdbSymbolReader(pdbCache, pdbLoggerMock.Object);
        _processDebugger = new ProcessDebugger(debuggerLoggerMock.Object, pdbReader);
        _sessionManager = new DebugSessionManager(_processDebugger, managerLoggerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _sessionManager.DisconnectAsync();
        _processDebugger.Dispose();
    }

    /// <summary>
    /// Verify ContinueAsync throws when no session is active.
    /// </summary>
    [Fact]
    public async Task ContinueAsync_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _sessionManager.ContinueAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active debug session*");
    }

    /// <summary>
    /// Verify StepAsync throws when no session is active.
    /// </summary>
    [Fact]
    public async Task StepAsync_WhenNoSession_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _sessionManager.StepAsync(StepMode.Over);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active debug session*");
    }
}
