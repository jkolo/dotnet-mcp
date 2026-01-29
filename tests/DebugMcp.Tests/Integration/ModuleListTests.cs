using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for the modules_list workflow.
/// These tests verify end-to-end module listing with real .NET processes.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class ModuleListTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private TestTargetProcess? _targetProcess;

    public ModuleListTests()
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
    public async Task GetModulesAsync_WhenAttached_ReturnsLoadedModules()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var modules = await _processDebugger.GetModulesAsync();

        // Assert
        modules.Should().NotBeEmpty("attached process should have loaded modules");
        modules.Should().Contain(m => m.Name.Contains("System"), "should include System assemblies");
    }

    [Fact]
    public async Task GetModulesAsync_WithIncludeSystemFalse_ExcludesSystemModules()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var modules = await _processDebugger.GetModulesAsync(includeSystem: false);

        // Assert
        modules.Should().NotContain(m =>
            m.Name.StartsWith("System.") ||
            m.Name.StartsWith("Microsoft.") ||
            m.Name == "mscorlib" ||
            m.Name == "System",
            "should exclude system assemblies when includeSystem is false");
    }

    [Fact]
    public async Task GetModulesAsync_WithNameFilter_ReturnsFilteredModules()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var modules = await _processDebugger.GetModulesAsync(nameFilter: "System*");

        // Assert
        modules.Should().OnlyContain(m =>
            m.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase),
            "should only return modules matching the filter pattern");
    }

    [Fact]
    public async Task GetModulesAsync_ModuleInfo_HasRequiredFields()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var modules = await _processDebugger.GetModulesAsync();

        // Assert
        foreach (var module in modules.Take(5)) // Check first 5 modules
        {
            module.Name.Should().NotBeNullOrEmpty("module name is required");
            module.FullName.Should().NotBeNullOrEmpty("full name is required");
            module.ModuleId.Should().NotBeNullOrEmpty("module ID is required");
            module.Version.Should().NotBeNullOrEmpty("version is required");
            module.IsManaged.Should().BeTrue("all modules from ICorDebug are managed");
        }
    }

    [Fact]
    public async Task GetModulesAsync_WorksWhileRunning()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Ensure process is running (not paused)
        _sessionManager.CurrentSession!.State.Should().Be(SessionState.Running);

        // Act - module listing should work while running (metadata only)
        var modules = await _processDebugger.GetModulesAsync();

        // Assert
        modules.Should().NotBeEmpty("module listing works with running process");
    }

    [Fact]
    public async Task GetModulesAsync_WorksWhilePaused()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Pause the process
        await _processDebugger.PauseAsync();

        // Act
        var modules = await _processDebugger.GetModulesAsync();

        // Assert
        modules.Should().NotBeEmpty("module listing works with paused process");
    }

    [Fact]
    public async Task GetModulesAsync_SystemPrivateCoreLib_HasCorrectProperties()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var modules = await _processDebugger.GetModulesAsync();

        // Assert
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        coreLib.Should().NotBeNull("CoreLib should always be loaded");
        coreLib!.IsManaged.Should().BeTrue();
        coreLib.IsDynamic.Should().BeFalse("CoreLib is not dynamic");
        coreLib.Path.Should().NotBeNullOrEmpty("CoreLib has a file path");
    }
}
