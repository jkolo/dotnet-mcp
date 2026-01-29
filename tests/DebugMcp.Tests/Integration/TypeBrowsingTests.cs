using DebugMcp.Models;
using DebugMcp.Models.Modules;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for the types_get workflow.
/// These tests verify end-to-end type browsing with real .NET processes.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class TypeBrowsingTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private TestTargetProcess? _targetProcess;

    public TypeBrowsingTests()
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
    public async Task GetTypesAsync_WhenAttached_ReturnsTypes()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Get a module to query
        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");
        coreLib.Should().NotBeNull("CoreLib should be loaded");

        // Act
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name);

        // Assert
        result.Types.Should().NotBeEmpty("CoreLib should have many types");
        result.ModuleName.Should().Be(coreLib.Name);
    }

    [Fact]
    public async Task GetTypesAsync_WithNamespaceFilter_ReturnsFilteredTypes()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, namespaceFilter: "System.Collections*");

        // Assert
        result.Types.Should().OnlyContain(t =>
            t.Namespace != null && t.Namespace.StartsWith("System.Collections", StringComparison.OrdinalIgnoreCase),
            "should only return types in System.Collections namespace");
    }

    [Fact]
    public async Task GetTypesAsync_WithKindFilter_ReturnsOnlyMatchingKinds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act - filter for interfaces only
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, kind: TypeKind.Interface);

        // Assert
        result.Types.Should().OnlyContain(t => t.Kind == TypeKind.Interface,
            "should only return interfaces when kind filter is Interface");
    }

    [Fact]
    public async Task GetTypesAsync_WithVisibilityFilter_ReturnsOnlyMatchingVisibility()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act - filter for public types only
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, visibility: Visibility.Public);

        // Assert
        result.Types.Should().OnlyContain(t => t.Visibility == Visibility.Public,
            "should only return public types when visibility filter is Public");
    }

    [Fact]
    public async Task GetTypesAsync_WithMaxResults_LimitsReturnedTypes()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act - limit to 10 results
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, maxResults: 10);

        // Assert
        result.Types.Should().HaveCountLessThanOrEqualTo(10, "should respect max_results limit");
        result.ReturnedCount.Should().BeLessThanOrEqualTo(10);
        if (result.TotalCount > 10)
        {
            result.Truncated.Should().BeTrue("should indicate truncation when more results available");
        }
    }

    [Fact]
    public async Task GetTypesAsync_TypeInfo_HasRequiredFields()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, maxResults: 5);

        // Assert
        foreach (var type in result.Types)
        {
            type.FullName.Should().NotBeNullOrEmpty("fullName is required");
            type.Name.Should().NotBeNullOrEmpty("name is required");
            type.Namespace.Should().NotBeNull("namespace is required (can be empty string)");
            type.Kind.Should().BeDefined("kind must be valid");
            type.Visibility.Should().BeDefined("visibility must be valid");
            type.ModuleName.Should().NotBeNullOrEmpty("moduleName is required");
        }
    }

    [Fact]
    public async Task GetTypesAsync_FindsGenericTypes()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act - look for List<T>
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name, namespaceFilter: "System.Collections.Generic*");

        // Assert
        var genericTypes = result.Types.Where(t => t.IsGeneric).ToList();
        genericTypes.Should().NotBeEmpty("System.Collections.Generic has generic types");

        var listType = genericTypes.FirstOrDefault(t => t.Name.StartsWith("List"));
        if (listType != null)
        {
            listType.GenericParameters.Should().NotBeEmpty("List<T> has type parameter T");
        }
    }

    [Fact]
    public async Task GetTypesAsync_IncludesNamespaceHierarchy()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name);

        // Assert
        result.Namespaces.Should().NotBeEmpty("should include namespace hierarchy");
        result.Namespaces.Should().Contain(n => n.Name == "System" || n.FullName == "System",
            "should include System namespace");
    }

    [Fact]
    public async Task GetTypesAsync_WithNonExistentModule_ThrowsException()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var act = async () => await _processDebugger.GetTypesAsync("NonExistent.Module");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetTypesAsync_WorksWhileRunning()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Ensure process is running (not paused)
        _sessionManager.CurrentSession!.State.Should().Be(SessionState.Running);

        var modules = await _processDebugger.GetModulesAsync();
        var coreLib = modules.FirstOrDefault(m =>
            m.Name == "System.Private.CoreLib" ||
            m.Name == "mscorlib");

        // Act - type browsing should work while running (metadata only)
        var result = await _processDebugger.GetTypesAsync(coreLib!.Name);

        // Assert
        result.Types.Should().NotBeEmpty("type browsing works with running process");
    }
}
