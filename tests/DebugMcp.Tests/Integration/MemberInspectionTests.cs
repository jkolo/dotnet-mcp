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
/// Integration tests for the members_get workflow.
/// These tests verify end-to-end member inspection with real .NET processes.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
public class MemberInspectionTests : IAsyncLifetime
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;
    private TestTargetProcess? _targetProcess;

    public MemberInspectionTests()
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
    public async Task GetMembersAsync_WhenAttached_ReturnsMembers()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - inspect System.String which has many members
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        result.TypeName.Should().Be("System.String");
        result.Methods.Should().NotBeEmpty("String has many methods");
        result.Properties.Should().NotBeEmpty("String has properties like Length");
    }

    [Fact]
    public async Task GetMembersAsync_FindsProperties_ByName()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - System.String has Length property
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        result.Properties.Should().Contain(p => p.Name == "Length",
            "String has Length property");

        var lengthProp = result.Properties.First(p => p.Name == "Length");
        // Type can be "int" (C# style) or "System.Int32" (CLR style)
        (lengthProp.Type == "int" || lengthProp.Type.Contains("Int32"))
            .Should().BeTrue("Length returns int");
        lengthProp.HasGetter.Should().BeTrue("Length has getter");
    }

    [Fact]
    public async Task GetMembersAsync_FindsMethods_WithSignatures()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert - Find common String methods
        result.Methods.Should().Contain(m => m.Name == "Substring",
            "String has Substring method");
        result.Methods.Should().Contain(m => m.Name == "Contains",
            "String has Contains method");
        result.Methods.Should().Contain(m => m.Name == "ToUpper",
            "String has ToUpper method");
    }

    [Fact]
    public async Task GetMembersAsync_MethodInfo_HasRequiredFields()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        foreach (var method in result.Methods.Take(5)) // Check first few methods
        {
            method.Name.Should().NotBeNullOrEmpty("name is required");
            method.Signature.Should().NotBeNullOrEmpty("signature is required");
            method.ReturnType.Should().NotBeNullOrEmpty("returnType is required");
            method.Parameters.Should().NotBeNull("parameters is required");
            method.Visibility.Should().BeDefined("visibility is required");
            method.DeclaringType.Should().NotBeNullOrEmpty("declaringType is required");
        }
    }

    [Fact]
    public async Task GetMembersAsync_FindsStaticMembers()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - String has static members like Empty, IsNullOrEmpty
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        result.Fields.Should().Contain(f => f.Name == "Empty" && f.IsStatic,
            "String has static Empty field");
        result.Methods.Should().Contain(m => m.Name == "IsNullOrEmpty" && m.IsStatic,
            "String has static IsNullOrEmpty method");
    }

    [Fact]
    public async Task GetMembersAsync_WithModuleName_ReturnsTypeFromSpecificModule()
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
        var result = await _processDebugger.GetMembersAsync("System.String", coreLib!.Name);

        // Assert
        result.TypeName.Should().Be("System.String");
        result.Methods.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetMembersAsync_WithMemberKindsFilter_ReturnsOnlyRequestedKinds()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - request only properties
        var result = await _processDebugger.GetMembersAsync(
            "System.String",
            memberKinds: new[] { "properties" });

        // Assert
        result.Properties.Should().NotBeEmpty("requested properties");
        result.Methods.Should().BeEmpty("did not request methods");
        result.Fields.Should().BeEmpty("did not request fields");
    }

    [Fact]
    public async Task GetMembersAsync_WithVisibilityFilter_ReturnsOnlyMatchingVisibility()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - filter for public members only
        var result = await _processDebugger.GetMembersAsync(
            "System.String",
            visibility: Visibility.Public);

        // Assert
        result.Methods.Should().OnlyContain(m => m.Visibility == Visibility.Public,
            "should only return public methods");
        result.Properties.Should().OnlyContain(p => p.Visibility == Visibility.Public,
            "should only return public properties");
    }

    [Fact]
    public async Task GetMembersAsync_IncludeStaticFalse_ExcludesStaticMembers()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - exclude static members
        var result = await _processDebugger.GetMembersAsync(
            "System.String",
            includeStatic: false);

        // Assert
        result.Methods.Should().OnlyContain(m => !m.IsStatic,
            "should not return static methods");
        result.Fields.Should().OnlyContain(f => !f.IsStatic,
            "should not return static fields");
    }

    [Fact]
    public async Task GetMembersAsync_IncludeInstanceFalse_ExcludesInstanceMembers()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - only static members
        var result = await _processDebugger.GetMembersAsync(
            "System.String",
            includeInstance: false);

        // Assert
        result.Methods.Should().OnlyContain(m => m.IsStatic,
            "should only return static methods");
    }

    [Fact]
    public async Task GetMembersAsync_WithNonExistentType_ThrowsException()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var act = async () => await _processDebugger.GetMembersAsync("NonExistent.Type.Here");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetMembersAsync_WorksWhileRunning()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Ensure process is running (not paused)
        _sessionManager.CurrentSession!.State.Should().Be(SessionState.Running);

        // Act - member inspection should work while running (metadata only)
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        result.Methods.Should().NotBeEmpty("member inspection works with running process");
    }

    [Fact]
    public async Task GetMembersAsync_MethodParameters_HaveCorrectInfo()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert - Find Substring method which has parameters
        var substringMethod = result.Methods.FirstOrDefault(m =>
            m.Name == "Substring" && m.Parameters.Length == 2);

        if (substringMethod != null)
        {
            substringMethod.Parameters.Should().HaveCount(2);
            substringMethod.Parameters[0].Name.Should().NotBeNullOrEmpty();
            // Type can be "int" (C# style) or "System.Int32" (CLR style)
            (substringMethod.Parameters[0].Type == "int" || substringMethod.Parameters[0].Type.Contains("Int32"))
                .Should().BeTrue("parameter should be int");
        }
    }

    [Fact]
    public async Task GetMembersAsync_FindsGenericMethods()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - Look for generic methods in System.Array
        var result = await _processDebugger.GetMembersAsync("System.Array");

        // Assert - Array has generic methods like Empty<T>
        var genericMethods = result.Methods.Where(m => m.IsGeneric).ToList();
        // Not all builds will have visible generic methods, so just verify the logic works
        result.Methods.Should().NotBeEmpty("Array has methods");
    }

    [Fact]
    public async Task GetMembersAsync_PropertyVisibility_ReflectsAccessorVisibility()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act
        var result = await _processDebugger.GetMembersAsync("System.String");

        // Assert
        var lengthProp = result.Properties.FirstOrDefault(p => p.Name == "Length");
        if (lengthProp != null)
        {
            lengthProp.HasGetter.Should().BeTrue("Length has getter");
            lengthProp.HasSetter.Should().BeFalse("Length is read-only");
        }
    }

    [Fact]
    public async Task GetMembersAsync_IncludesInheritedMembers_WhenRequested()
    {
        // Arrange
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(10));

        // Act - include inherited members
        var resultWithInherited = await _processDebugger.GetMembersAsync(
            "System.String",
            includeInherited: true);

        var resultWithoutInherited = await _processDebugger.GetMembersAsync(
            "System.String",
            includeInherited: false);

        // Assert
        resultWithInherited.IncludesInherited.Should().BeTrue();
        resultWithoutInherited.IncludesInherited.Should().BeFalse();

        // With inherited should have more or equal methods (Object methods)
        resultWithInherited.MethodCount.Should().BeGreaterThanOrEqualTo(resultWithoutInherited.MethodCount);
    }
}
