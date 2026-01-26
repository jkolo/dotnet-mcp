using System.Diagnostics;
using DotnetMcp.Models.Modules;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Performance;

/// <summary>
/// Performance tests for module inspection operations.
/// Tests validate success criteria from specs/005-module-ops.
/// SC-001: Module list returns within 1 second
/// SC-002: Type browse returns within 2 seconds
/// SC-003: Member inspect returns within 500ms
/// SC-004: Search returns within 3 seconds
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class ModulePerformanceTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private TestTargetProcess? _targetProcess;

    public ModulePerformanceTests()
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

    /// <summary>
    /// SC-001: Module list returns within 1 second.
    /// </summary>
    [Fact]
    public async Task GetModulesAsync_ReturnsWithin1Second()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 1000; // SC-001: 1 second max
        var sw = Stopwatch.StartNew();

        // Act
        var modules = await _processDebugger.GetModulesAsync();

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Module list should complete within {maxMs}ms");
        modules.Should().NotBeEmpty("should find at least some modules");
    }

    /// <summary>
    /// SC-001: Module list with system filter returns within 1 second.
    /// </summary>
    [Fact]
    public async Task GetModulesAsync_WithFilter_ReturnsWithin1Second()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 1000; // SC-001: 1 second max
        var sw = Stopwatch.StartNew();

        // Act
        var modules = await _processDebugger.GetModulesAsync(includeSystem: true);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Module list with filter should complete within {maxMs}ms");
    }

    /// <summary>
    /// SC-002: Type browse returns within 2 seconds.
    /// </summary>
    [Fact]
    public async Task GetTypesAsync_ReturnsWithin2Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Get a module to browse
        var modules = await _processDebugger.GetModulesAsync(includeSystem: true);
        var coreLib = modules.FirstOrDefault(m =>
            m.Name.Contains("System.Private.CoreLib") || m.Name.Contains("mscorlib"));
        coreLib.Should().NotBeNull("CoreLib must be loaded");

        const int maxMs = 2000; // SC-002: 2 seconds max
        var sw = Stopwatch.StartNew();

        // Act
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Type browse should complete within {maxMs}ms");
        result.Types.Should().NotBeEmpty("CoreLib has many types");
    }

    /// <summary>
    /// SC-002: Type browse with pagination returns within 2 seconds.
    /// </summary>
    [Fact]
    public async Task GetTypesAsync_WithPagination_ReturnsWithin2Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync(includeSystem: true);
        var coreLib = modules.FirstOrDefault(m =>
            m.Name.Contains("System.Private.CoreLib") || m.Name.Contains("mscorlib"));
        coreLib.Should().NotBeNull();

        const int maxMs = 2000; // SC-002: 2 seconds max
        var sw = Stopwatch.StartNew();

        // Act - request paginated results
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, maxResults: 50);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Type browse with pagination should complete within {maxMs}ms");
        result.Types.Should().HaveCountLessThanOrEqualTo(50, "pagination should limit results");
    }

    /// <summary>
    /// SC-003: Member inspect returns within 500ms.
    /// </summary>
    [Fact]
    public async Task GetMembersAsync_ReturnsWithin500ms()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 500; // SC-003: 500ms max
        var sw = Stopwatch.StartNew();

        // Act - inspect System.String members
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-003: Member inspect should complete within {maxMs}ms");
        result.Methods.Should().NotBeEmpty("String has methods");
    }

    /// <summary>
    /// SC-003: Member inspect with inheritance returns within 500ms.
    /// </summary>
    [Fact]
    public async Task GetMembersAsync_WithInheritance_ReturnsWithin500ms()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 500; // SC-003: 500ms max
        var sw = Stopwatch.StartNew();

        // Act - inspect with inherited members
        var result = await _processDebugger.GetMembersAsync("System.Exception", includeInherited: true);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-003: Member inspect with inheritance should complete within {maxMs}ms");
        result.Methods.Should().NotBeEmpty("Exception has methods");
    }

    /// <summary>
    /// SC-004: Search returns within 3 seconds.
    /// </summary>
    [Fact]
    public async Task SearchModulesAsync_ReturnsWithin3Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 3000; // SC-004: 3 seconds max
        var sw = Stopwatch.StartNew();

        // Act - search for String types
        var result = await _processDebugger.SearchModulesAsync("*String*", SearchType.Types);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Search should complete within {maxMs}ms");
        result.Types.Should().NotBeEmpty("should find String types");
    }

    /// <summary>
    /// SC-004: Method search returns within 3 seconds.
    /// </summary>
    [Fact]
    public async Task SearchModulesAsync_MethodSearch_ReturnsWithin3Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 3000; // SC-004: 3 seconds max
        var sw = Stopwatch.StartNew();

        // Act - search for ToString methods
        var result = await _processDebugger.SearchModulesAsync("ToString", SearchType.Methods, maxResults: 50);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Method search should complete within {maxMs}ms");
        result.Methods.Should().NotBeEmpty("should find ToString methods");
    }

    /// <summary>
    /// SC-004: Both search (types + methods) returns within 3 seconds.
    /// </summary>
    [Fact]
    public async Task SearchModulesAsync_BothSearch_ReturnsWithin3Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 3000; // SC-004: 3 seconds max
        var sw = Stopwatch.StartNew();

        // Act - search for both types and methods
        var result = await _processDebugger.SearchModulesAsync("*Object*", SearchType.Both, maxResults: 50);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Both search should complete within {maxMs}ms");
        (result.Types.Any() || result.Methods.Any()).Should().BeTrue("should find matches");
    }

    /// <summary>
    /// SC-004: Search with module filter returns within 3 seconds.
    /// </summary>
    [Fact]
    public async Task SearchModulesAsync_WithModuleFilter_ReturnsWithin3Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 3000; // SC-004: 3 seconds max
        var sw = Stopwatch.StartNew();

        // Act - search limited to CoreLib
        var result = await _processDebugger.SearchModulesAsync(
            "*String*", SearchType.Types, moduleFilter: "System.Private.CoreLib");

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Search with module filter should complete within {maxMs}ms");
    }

    /// <summary>
    /// Performance: Multiple sequential operations stay within bounds.
    /// </summary>
    [Fact]
    public async Task ModuleOperations_Sequential_StayWithinBounds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Sum of all individual limits: 1s + 2s + 0.5s + 3s = 6.5s
        const int maxTotalMs = 7000; // Allow some overhead
        var sw = Stopwatch.StartNew();

        // Act - run all operations sequentially
        var modules = await _processDebugger.GetModulesAsync(includeSystem: true);
        var coreLib = modules.FirstOrDefault(m => m.Name.Contains("System.Private.CoreLib"));

        if (coreLib != null)
        {
            var types = await _processDebugger.GetTypesAsync(coreLib.Name, maxResults: 20);
            var members = await _processDebugger.GetMembersAsync("System.String");
            var search = await _processDebugger.SearchModulesAsync("*String*", SearchType.Both, maxResults: 20);
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxTotalMs,
            $"All module operations should complete within {maxTotalMs}ms total");
    }

    /// <summary>
    /// Performance: Repeated module list calls are consistent.
    /// </summary>
    [Fact]
    public async Task GetModulesAsync_Repeated_ConsistentPerformance()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int iterations = 5;
        const int maxMs = 1000; // Each call should stay within SC-001
        var times = new List<long>();

        // Act - run multiple times
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await _processDebugger.GetModulesAsync();
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }

        // Assert
        times.All(t => t < maxMs).Should().BeTrue(
            $"All iterations should complete within {maxMs}ms, but got: {string.Join(", ", times)}ms");
    }

    /// <summary>
    /// Performance: Search with wildcard at start is efficient.
    /// </summary>
    [Fact]
    public async Task SearchModulesAsync_WildcardPrefix_ReturnsWithin3Seconds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        const int maxMs = 3000; // SC-004: 3 seconds max
        var sw = Stopwatch.StartNew();

        // Act - wildcard at start is typically slower
        var result = await _processDebugger.SearchModulesAsync("*Exception", SearchType.Types, maxResults: 50);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Prefix wildcard search should complete within {maxMs}ms");
        result.Types.Should().NotBeEmpty("should find Exception types");
    }
}
