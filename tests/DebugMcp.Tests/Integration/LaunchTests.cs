using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for the launch workflow.
/// These tests verify launching processes under debugger control.
/// </summary>
[Collection("ProcessTests")]
public class LaunchTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public LaunchTests()
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
        try { await _sessionManager.DisconnectAsync(terminateProcess: true); }
        catch { /* ignore cleanup errors */ }
        _processDebugger.Dispose();
    }

    [Fact]
    public async Task LaunchAsync_WithNonExistentProgram_ThrowsFileNotFoundException()
    {
        // Arrange
        const string nonExistentProgram = "/path/to/nonexistent/app.dll";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var act = async () => await _sessionManager.LaunchAsync(
            nonExistentProgram,
            timeout: timeout);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LaunchAsync_WithEmptyProgram_ThrowsArgumentException()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(5);

        // Act - empty string
        var actEmpty = async () => await _sessionManager.LaunchAsync(
            "",
            timeout: timeout);

        // Assert
        await actEmpty.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task LaunchAsync_WhenAlreadyAttached_ThrowsInvalidOperationException()
    {
        // Arrange - first we need to attach (which will fail, but simulate the state)
        // We use a mock to set up an existing session

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
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // First launch
        await manager.LaunchAsync("/path/to/test.dll");

        // Act - try to launch again
        var act = async () => await manager.LaunchAsync("/path/to/another.dll");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already active*");
    }

    [Fact]
    public async Task LaunchAsync_PassesArgsCorrectly()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        var capturedArgs = Array.Empty<string>();

        mockDebugger
            .Setup(d => d.LaunchAsync(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string[]?, string?, Dictionary<string, string>?, bool, TimeSpan?, CancellationToken>(
                (_, args, _, _, _, _, _) => capturedArgs = args ?? Array.Empty<string>())
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Act
        await manager.LaunchAsync(
            "/path/to/test.dll",
            args: new[] { "--verbose", "--config", "debug" });

        // Assert
        capturedArgs.Should().BeEquivalentTo(new[] { "--verbose", "--config", "debug" });
    }

    [Fact]
    public async Task LaunchAsync_PassesCwdCorrectly()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        string? capturedCwd = null;

        mockDebugger
            .Setup(d => d.LaunchAsync(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string[]?, string?, Dictionary<string, string>?, bool, TimeSpan?, CancellationToken>(
                (_, _, cwd, _, _, _, _) => capturedCwd = cwd)
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Act
        await manager.LaunchAsync(
            "/path/to/test.dll",
            cwd: "/working/directory");

        // Assert
        capturedCwd.Should().Be("/working/directory");
    }

    [Fact]
    public async Task LaunchAsync_PassesEnvCorrectly()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        Dictionary<string, string>? capturedEnv = null;

        mockDebugger
            .Setup(d => d.LaunchAsync(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string[]?, string?, Dictionary<string, string>?, bool, TimeSpan?, CancellationToken>(
                (_, _, _, env, _, _, _) => capturedEnv = env)
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        var env = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        };

        // Act
        await manager.LaunchAsync(
            "/path/to/test.dll",
            env: env);

        // Assert
        capturedEnv.Should().ContainKey("ASPNETCORE_ENVIRONMENT");
        capturedEnv!["ASPNETCORE_ENVIRONMENT"].Should().Be("Development");
    }

    [Fact]
    public async Task LaunchAsync_PassesStopAtEntryCorrectly()
    {
        // Arrange
        var mockDebugger = new Mock<IProcessDebugger>();
        bool capturedStopAtEntry = false;

        mockDebugger
            .Setup(d => d.LaunchAsync(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string[]?, string?, Dictionary<string, string>?, bool, TimeSpan?, CancellationToken>(
                (_, _, _, _, stopAtEntry, _, _) => capturedStopAtEntry = stopAtEntry)
            .ReturnsAsync(new ProcessInfo(
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Act - default (true)
        await manager.LaunchAsync("/path/to/test.dll");

        // Assert
        capturedStopAtEntry.Should().BeTrue("default stopAtEntry should be true");

        // Act - explicit false
        await manager.DisconnectAsync();
        await manager.LaunchAsync("/path/to/test.dll", stopAtEntry: false);

        // Assert
        capturedStopAtEntry.Should().BeFalse("explicit stopAtEntry=false should be passed");
    }

    [Fact]
    public async Task LaunchAsync_CreatesSessionWithLaunchMode()
    {
        // Arrange
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
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Act
        var session = await manager.LaunchAsync(
            "/path/to/test.dll",
            args: new[] { "--test" },
            cwd: "/working");

        // Assert
        session.Should().NotBeNull();
        session.LaunchMode.Should().Be(LaunchMode.Launch);
        session.CommandLineArgs.Should().BeEquivalentTo(new[] { "--test" });
        session.WorkingDirectory.Should().Be("/working");
    }

    [Fact]
    public async Task LaunchAsync_WithStopAtEntryTrue_SessionIsPaused()
    {
        // Arrange
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
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Act
        var session = await manager.LaunchAsync("/path/to/test.dll", stopAtEntry: true);

        // Assert
        session.State.Should().Be(SessionState.Paused, "should be paused when stopAtEntry is true");
        session.PauseReason.Should().Be(PauseReason.Entry, "pause reason should be entry");
    }

    [Fact]
    public async Task LaunchAsync_WithStopAtEntryFalse_SessionIsRunning()
    {
        // Arrange
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
                Pid: 1234,
                Name: "test",
                ExecutablePath: "/path/to/test.dll",
                IsManaged: true,
                CommandLine: null,
                RuntimeVersion: ".NET 8.0"));

        var manager = new DebugSessionManager(mockDebugger.Object, _managerLoggerMock.Object);

        // Act
        var session = await manager.LaunchAsync("/path/to/test.dll", stopAtEntry: false);

        // Assert
        session.State.Should().Be(SessionState.Running, "should be running when stopAtEntry is false");
        session.PauseReason.Should().BeNull("no pause reason when running");
    }

    [Fact]
    public async Task LaunchAsync_WithInvalidCwd_ThrowsDirectoryNotFoundException()
    {
        var testDll = TestTargetProcess.TestTargetDllPath;

        // Act
        var act = async () => await _sessionManager.LaunchAsync(
            testDll,
            cwd: "/nonexistent/directory/path");

        // Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }
}
