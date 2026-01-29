using DebugMcp.Models;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Integration;

/// <summary>
/// Integration tests for nested object property inspection (Bug #1 fix).
/// Tests verify that object_inspect can resolve nested property paths like
/// 'this._currentUser.HomeAddress' using dot notation.
/// </summary>
[Collection("ProcessTests")] // Prevent parallel execution with other process tests
[Trait("Category", "Integration")]
public class NestedInspectionTests : IAsyncLifetime
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

    public NestedInspectionTests()
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

        try
        {
            await _sessionManager.DisconnectAsync();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _processDebugger.Dispose();
        _targetProcess?.Dispose();
    }

    /// <summary>
    /// Helper to set a breakpoint in ObjectTarget.ProcessUser and wait for it to hit.
    /// This ensures we're paused at a location where 'this' is accessible.
    /// </summary>
    private async Task<int> SetBreakpointAndWaitAsync()
    {
        // Set breakpoint at ObjectTarget.ProcessUser method where 'this' is ObjectTarget instance
        // and _currentUser field is accessible (line 25 in ObjectTarget.cs - first statement)
        var sourceFile = TestTargetProcess.GetSourceFilePath("ObjectTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 25);

        // Trigger the object command to execute ProcessUser
        await _targetProcess!.SendCommandAsync("object");

        // Wait for breakpoint to be hit
        var hit = await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(10));
        hit.Should().NotBeNull("Breakpoint should be hit within timeout");

        return hit!.ThreadId;
    }

    /// <summary>
    /// T019: Single Level Field Access
    /// Given debugger is paused with 'this' being an ObjectTarget instance
    /// When I inspect 'this._currentUser'
    /// Then I get the Person object details
    /// </summary>
    [Fact]
    public async Task SingleLevelFieldAccess_ShouldSucceed()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint and wait for it
        var threadId = await SetBreakpointAndWaitAsync();

        // Act - Inspect this._currentUser (single level field access)
        var result = await _processDebugger.InspectObjectAsync(
            objectRef: "this._currentUser",
            depth: 1,
            threadId: threadId,
            frameIndex: 0);

        // Assert
        result.Should().NotBeNull("single level field access should succeed");
        result.TypeName.Should().Contain("Person", "should return Person type");
        result.Fields.Should().NotBeEmpty("Person should have fields");
    }

    /// <summary>
    /// T020: Two Level Property Access
    /// Given debugger is paused with 'this' being an ObjectTarget instance
    /// When I inspect 'this._currentUser.HomeAddress'
    /// Then I get the Address object details
    /// </summary>
    [Fact]
    public async Task TwoLevelPropertyAccess_ShouldReturnAddressObject()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint and wait for it
        var threadId = await SetBreakpointAndWaitAsync();

        // Act - Inspect this._currentUser.HomeAddress (field -> property access)
        var result = await _processDebugger.InspectObjectAsync(
            objectRef: "this._currentUser.HomeAddress",
            depth: 1,
            threadId: threadId,
            frameIndex: 0);

        // Assert
        result.Should().NotBeNull("two level property access should succeed");
        result.TypeName.Should().Contain("Address", "should return Address type");
        result.Fields.Should().NotBeEmpty("Address should have fields like City, Street");
    }

    /// <summary>
    /// T021: Three Level Access to String Value
    /// Given debugger is paused with 'this' being an ObjectTarget instance
    /// When I inspect 'this._currentUser.HomeAddress.City'
    /// Then I get the string value
    /// </summary>
    [Fact]
    public async Task ThreeLevelAccess_ShouldReturnStringValue()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint and wait for it
        var threadId = await SetBreakpointAndWaitAsync();

        // Act - Inspect this._currentUser.HomeAddress.City (field -> property -> property)
        var result = await _processDebugger.InspectObjectAsync(
            objectRef: "this._currentUser.HomeAddress.City",
            depth: 1,
            threadId: threadId,
            frameIndex: 0);

        // Assert
        result.Should().NotBeNull("three level access should succeed");
        result.TypeName.Should().Be("System.String", "City is a string property");
        result.IsNull.Should().BeFalse("City should have a value");
    }

    /// <summary>
    /// T022: Null Intermediate Value
    /// Given a path with null intermediate value
    /// When I inspect 'this._currentUser.WorkAddress.City' (WorkAddress is null)
    /// Then I get an exception indicating the null reference
    /// </summary>
    [Fact]
    public async Task NullIntermediate_ShouldReturnClearError()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint and wait for it
        var threadId = await SetBreakpointAndWaitAsync();

        // Act & Assert - Try to inspect through a null intermediate (WorkAddress is null)
        var act = async () => await _processDebugger.InspectObjectAsync(
            objectRef: "this._currentUser.WorkAddress.City",
            depth: 1,
            threadId: threadId,
            frameIndex: 0);

        // Should throw with clear error message about the null intermediate
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*null*", "should indicate the path contains null");
    }

    /// <summary>
    /// T023: Invalid Member Access
    /// Given valid object
    /// When I inspect 'this._currentUser.InvalidProperty'
    /// Then I get member not found error
    /// </summary>
    [Fact]
    public async Task InvalidMember_ShouldReturnMemberNotFound()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint and wait for it
        var threadId = await SetBreakpointAndWaitAsync();

        // Act & Assert - Try to inspect a non-existent property
        var act = async () => await _processDebugger.InspectObjectAsync(
            objectRef: "this._currentUser.InvalidProperty",
            depth: 1,
            threadId: threadId,
            frameIndex: 0);

        // Should throw with error mentioning the invalid member
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*", "should indicate the member was not found");
    }

    /// <summary>
    /// T044: Five Level Deep Nesting Access
    /// Given debugger is paused with deep object hierarchy
    /// When I inspect 'this._company.Department.Team.Manager.Contact.Email'
    /// Then I get the string email value through 5 levels of nesting
    /// </summary>
    [Fact]
    public async Task FiveLevelDeepNesting_ShouldResolveAllLevels()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint at DeepNestingTarget.ProcessCompany method (line 130)
        var sourceFile = TestTargetProcess.GetSourceFilePath("ObjectTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 130);

        // Trigger the deep command
        await _targetProcess.SendCommandAsync("deep");

        // Wait for breakpoint to be hit
        var hit = await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(10));
        hit.Should().NotBeNull("Breakpoint should be hit within timeout");
        var threadId = hit!.ThreadId;

        // Act - Inspect 5-level deep path: this._company.Department.Team.Manager.Contact.Email
        var result = await _processDebugger.InspectObjectAsync(
            objectRef: "this._company.Department.Team.Manager.Contact.Email",
            depth: 1,
            threadId: threadId,
            frameIndex: 0);

        // Assert
        result.Should().NotBeNull("5-level deep nesting should succeed");
        result.TypeName.Should().Be("System.String", "Email is a string property");
        result.IsNull.Should().BeFalse("Email should have a value");
    }

    /// <summary>
    /// T044b: Five Level With Inherited Property
    /// Given debugger is paused with deep object hierarchy where Manager inherits from BaseEntity
    /// When I inspect 'this._company.Department.Team.Manager.Id'
    /// Then I get the inherited Id value through multiple levels + base type traversal
    /// </summary>
    [Fact]
    public async Task FiveLevelWithInheritedProperty_ShouldResolveBaseTypeProperty()
    {
        // Arrange - Start target and attach
        _targetProcess = new TestTargetProcess();
        await _targetProcess.StartAsync();
        await _sessionManager.AttachAsync(_targetProcess.ProcessId, TimeSpan.FromSeconds(5));

        // Set breakpoint at DeepNestingTarget.ProcessCompany method (line 130)
        var sourceFile = TestTargetProcess.GetSourceFilePath("ObjectTarget.cs");
        await _breakpointManager.SetBreakpointAsync(sourceFile, 130);

        // Trigger the deep command
        await _targetProcess.SendCommandAsync("deep");

        // Wait for breakpoint to be hit
        var hit = await _breakpointManager.WaitForBreakpointAsync(TimeSpan.FromSeconds(10));
        hit.Should().NotBeNull("Breakpoint should be hit within timeout");
        var threadId = hit!.ThreadId;

        // Act - Inspect path with inherited property: Manager.Id (inherited from BaseEntity)
        var result = await _processDebugger.EvaluateAsync(
            expression: "this._company.Department.Team.Manager.Id",
            threadId: threadId,
            frameIndex: 0);

        // Assert
        result.Success.Should().BeTrue("deep path with inherited property should succeed");
        result.Value.Should().NotBeNullOrEmpty("inherited Id should have a value");
        result.Value.Should().MatchRegex(@"\d+", "Id should be a numeric value");
    }
}
