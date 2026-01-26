using DotnetMcp.Models;
using DotnetMcp.Models.Modules;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Integration;

/// <summary>
/// Integration tests for the modules_search workflow.
/// These tests verify end-to-end module search with real .NET processes.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class ModuleSearchTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private TestTargetProcess? _targetProcess;

    public ModuleSearchTests()
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
    public async Task SearchModulesAsync_TypeSearch_FindsTypes()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for String type
        var result = await _processDebugger.SearchModulesAsync("*String*", SearchType.Types);

        // Assert
        result.Query.Should().Be("*String*");
        result.SearchType.Should().Be(SearchType.Types);
        result.Types.Should().NotBeEmpty("System.String should be found");
        result.Types.Should().Contain(t => t.Name == "String" || t.FullName.Contains("String"));
    }

    [Fact]
    public async Task SearchModulesAsync_MethodSearch_FindsMethods()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for ToString methods
        var result = await _processDebugger.SearchModulesAsync("ToString", SearchType.Methods);

        // Assert
        result.Query.Should().Be("ToString");
        result.SearchType.Should().Be(SearchType.Methods);
        result.Methods.Should().NotBeEmpty("many types have ToString");
    }

    [Fact]
    public async Task SearchModulesAsync_BothSearch_FindsTypesAndMethods()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for something likely to match both
        var result = await _processDebugger.SearchModulesAsync("*Object*", SearchType.Both);

        // Assert
        result.SearchType.Should().Be(SearchType.Both);
        // Should find System.Object type and methods containing "Object" in name or type
        (result.Types.Any() || result.Methods.Any()).Should().BeTrue("should find types or methods with Object");
    }

    [Fact]
    public async Task SearchModulesAsync_WildcardPrefix_MatchesSuffix()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for types ending with "Exception"
        var result = await _processDebugger.SearchModulesAsync("*Exception", SearchType.Types);

        // Assert
        result.Types.Should().NotBeEmpty("there are many exception types");
        result.Types.Should().OnlyContain(t =>
            t.Name.EndsWith("Exception", StringComparison.OrdinalIgnoreCase) ||
            t.FullName.EndsWith("Exception", StringComparison.OrdinalIgnoreCase),
            "should only match types ending with Exception");
    }

    [Fact]
    public async Task SearchModulesAsync_WildcardSuffix_MatchesPrefix()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for types starting with "System"
        var result = await _processDebugger.SearchModulesAsync("System*", SearchType.Types, maxResults: 10);

        // Assert
        result.Types.Should().NotBeEmpty("there are many System types");
        result.Types.Should().OnlyContain(t =>
            t.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
            t.Namespace.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
            t.FullName.StartsWith("System", StringComparison.OrdinalIgnoreCase),
            "should only match types starting with System");
    }

    [Fact]
    public async Task SearchModulesAsync_ExactMatch_FindsExactType()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for exact type name
        var result = await _processDebugger.SearchModulesAsync("System.String", SearchType.Types);

        // Assert
        result.Types.Should().Contain(t =>
            t.FullName == "System.String" || t.Name == "String",
            "should find System.String");
    }

    [Fact]
    public async Task SearchModulesAsync_CaseSensitive_MatchesCase()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - case-insensitive search (default)
        var resultInsensitive = await _processDebugger.SearchModulesAsync("string", SearchType.Types, caseSensitive: false);

        // Act - case-sensitive search
        var resultSensitive = await _processDebugger.SearchModulesAsync("string", SearchType.Types, caseSensitive: true);

        // Assert
        // Case-insensitive should find more or equal results
        resultInsensitive.TotalMatches.Should().BeGreaterThanOrEqualTo(resultSensitive.TotalMatches);
    }

    [Fact]
    public async Task SearchModulesAsync_MaxResults_LimitsOutput()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search with limit
        var result = await _processDebugger.SearchModulesAsync("*", SearchType.Types, maxResults: 5);

        // Assert
        result.ReturnedMatches.Should().BeLessThanOrEqualTo(5);
        if (result.TotalMatches > 5)
        {
            result.Truncated.Should().BeTrue("results should be truncated");
        }
    }

    [Fact]
    public async Task SearchModulesAsync_ModuleFilter_LimitsSearchScope()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search only in core library
        var result = await _processDebugger.SearchModulesAsync("*String*", SearchType.Types,
            moduleFilter: "System.Private.CoreLib");

        // Assert
        result.Types.Should().NotBeEmpty("CoreLib has String types");
        result.Types.Should().OnlyContain(t =>
            t.ModuleName == "System.Private.CoreLib" ||
            t.ModuleName == "mscorlib",
            "should only return types from specified module");
    }

    [Fact]
    public async Task SearchModulesAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - search for non-existent pattern
        var result = await _processDebugger.SearchModulesAsync("XyzNonExistentPattern123", SearchType.Types);

        // Assert
        result.Types.Should().BeEmpty("no types match this pattern");
        result.TotalMatches.Should().Be(0);
        result.Truncated.Should().BeFalse("no results to truncate");
    }

    [Fact]
    public async Task SearchModulesAsync_WorksWhileRunning()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Ensure process is running (not paused)
        _sessionManager.CurrentSession!.State.Should().Be(SessionState.Running);

        // Act - search should work while running (metadata only)
        var result = await _processDebugger.SearchModulesAsync("*String*", SearchType.Types);

        // Assert
        result.Types.Should().NotBeEmpty("search works with running process");
    }

    [Fact]
    public async Task SearchModulesAsync_MethodSearchMatch_IncludesDeclaringType()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var result = await _processDebugger.SearchModulesAsync("ToString", SearchType.Methods, maxResults: 5);

        // Assert
        result.Methods.Should().NotBeEmpty("many types have ToString");
        foreach (var match in result.Methods)
        {
            match.DeclaringType.Should().NotBeNullOrEmpty("declaringType is required");
            match.ModuleName.Should().NotBeNullOrEmpty("moduleName is required");
            match.Method.Should().NotBeNull("method info is required");
            match.MatchReason.Should().NotBeNullOrEmpty("matchReason is required");
        }
    }
}
