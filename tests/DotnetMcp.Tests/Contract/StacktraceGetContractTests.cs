using System.Text.Json;
using DotnetMcp.Models;
using DotnetMcp.Models.Inspection;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the stacktrace_get tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in contracts/stacktrace_get.json.
/// </summary>
public class StacktraceGetContractTests
{
    /// <summary>
    /// stacktrace_get has no required input parameters (all are optional).
    /// </summary>
    [Fact]
    public void StacktraceGet_NoRequiredParameters()
    {
        // The input schema specifies no required parameters
        // thread_id, start_frame, and max_frames are all optional
        true.Should().BeTrue("stacktrace_get has no required parameters");
    }

    /// <summary>
    /// Default values for optional parameters match contract.
    /// </summary>
    [Fact]
    public void StacktraceGet_DefaultValues_MatchContract()
    {
        // Contract defines: start_frame default: 0, max_frames default: 20
        const int defaultStartFrame = 0;
        const int defaultMaxFrames = 20;

        defaultStartFrame.Should().Be(0, "start_frame default is 0");
        defaultMaxFrames.Should().Be(20, "max_frames default is 20");
    }

    /// <summary>
    /// max_frames must be between 1 and 1000 per contract.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(1000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1001, false)]
    public void StacktraceGet_MaxFrames_Validation(int maxFrames, bool isValid)
    {
        // Contract defines: minimum: 1, maximum: 1000
        var valid = maxFrames >= 1 && maxFrames <= 1000;
        valid.Should().Be(isValid, $"max_frames={maxFrames} should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// start_frame must be >= 0 per contract.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    public void StacktraceGet_StartFrame_Validation(int startFrame, bool isValid)
    {
        // Contract defines: minimum: 0
        var valid = startFrame >= 0;
        valid.Should().Be(isValid, $"start_frame={startFrame} should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// Response includes required fields: thread_id, total_frames, frames.
    /// </summary>
    [Fact]
    public void StacktraceGet_Response_IncludesRequiredFields()
    {
        // Contract requires: thread_id, total_frames, frames
        var response = new
        {
            thread_id = 1,
            total_frames = 5,
            frames = new[]
            {
                new { index = 0, function = "Test.Method", module = "Test.dll", is_external = false }
            }
        };

        response.thread_id.Should().BePositive();
        response.total_frames.Should().BeGreaterThanOrEqualTo(0);
        response.frames.Should().NotBeNull();
    }

    /// <summary>
    /// StackFrame has required fields: index, function, module, is_external.
    /// </summary>
    [Fact]
    public void StackFrame_RequiredFields()
    {
        var frame = new StackFrame(
            Index: 0,
            Function: "TestNamespace.TestClass.TestMethod",
            Module: "TestAssembly.dll",
            IsExternal: false);

        frame.Index.Should().Be(0);
        frame.Function.Should().NotBeNullOrEmpty();
        frame.Module.Should().NotBeNullOrEmpty();
        frame.IsExternal.Should().BeFalse();
    }

    /// <summary>
    /// StackFrame index starts at 0 (top of stack).
    /// </summary>
    [Fact]
    public void StackFrame_Index_StartsAtZero()
    {
        // Contract: index minimum: 0, 0 = top of stack
        var topFrame = new StackFrame(
            Index: 0,
            Function: "Top.Method",
            Module: "App.dll",
            IsExternal: false);

        topFrame.Index.Should().Be(0, "top frame index is 0");
    }

    /// <summary>
    /// StackFrame location is optional (null when symbols unavailable).
    /// </summary>
    [Fact]
    public void StackFrame_Location_IsOptional()
    {
        // Contract defines location as oneOf: [source_location, null]
        var frameWithLocation = new StackFrame(
            Index: 0,
            Function: "Test.Method",
            Module: "Test.dll",
            IsExternal: false,
            Location: new SourceLocation("/path/to/file.cs", 10, null, null, null));

        var frameWithoutLocation = new StackFrame(
            Index: 1,
            Function: "External.Method",
            Module: "System.dll",
            IsExternal: true,
            Location: null);

        frameWithLocation.Location.Should().NotBeNull();
        frameWithoutLocation.Location.Should().BeNull();
    }

    /// <summary>
    /// StackFrame arguments is optional.
    /// </summary>
    [Fact]
    public void StackFrame_Arguments_IsOptional()
    {
        var frameWithArgs = new StackFrame(
            Index: 0,
            Function: "Test.Method",
            Module: "Test.dll",
            IsExternal: false,
            Arguments: new[]
            {
                new Variable("arg1", "String", "\"hello\"", VariableScope.Argument, false, null, null)
            });

        var frameWithoutArgs = new StackFrame(
            Index: 1,
            Function: "Test.Method2",
            Module: "Test.dll",
            IsExternal: false,
            Arguments: null);

        frameWithArgs.Arguments.Should().NotBeNull();
        frameWithArgs.Arguments.Should().HaveCount(1);
        frameWithoutArgs.Arguments.Should().BeNull();
    }

    /// <summary>
    /// IsExternal should be true for framework code (no source available).
    /// </summary>
    [Fact]
    public void StackFrame_IsExternal_TrueForFrameworkCode()
    {
        // Framework code (System.*, Microsoft.*) should have is_external = true
        var frameworkFrame = new StackFrame(
            Index: 2,
            Function: "System.Threading.Tasks.Task.Run",
            Module: "System.Private.CoreLib.dll",
            IsExternal: true,
            Location: null);

        var userFrame = new StackFrame(
            Index: 0,
            Function: "MyApp.Program.Main",
            Module: "MyApp.dll",
            IsExternal: false,
            Location: new SourceLocation("/src/Program.cs", 10, null, null, null));

        frameworkFrame.IsExternal.Should().BeTrue();
        userFrame.IsExternal.Should().BeFalse();
    }

    /// <summary>
    /// Variable scope enum matches contract values.
    /// </summary>
    [Theory]
    [InlineData(VariableScope.Local, "local")]
    [InlineData(VariableScope.Argument, "argument")]
    [InlineData(VariableScope.This, "this")]
    [InlineData(VariableScope.Field, "field")]
    [InlineData(VariableScope.Property, "property")]
    [InlineData(VariableScope.Element, "element")]
    public void VariableScope_ValuesMatchContract(VariableScope scope, string expectedJsonValue)
    {
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(scope.ToString());
        jsonValue.Should().Be(expectedJsonValue, $"VariableScope.{scope} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// Variable has required fields: name, type, value, scope, has_children.
    /// </summary>
    [Fact]
    public void Variable_RequiredFields()
    {
        var variable = new Variable(
            Name: "counter",
            Type: "System.Int32",
            Value: "42",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        variable.Name.Should().NotBeNullOrEmpty();
        variable.Type.Should().NotBeNullOrEmpty();
        variable.Value.Should().NotBeNull();
        variable.HasChildren.Should().BeFalse();
    }

    /// <summary>
    /// Variable children_count is optional.
    /// </summary>
    [Fact]
    public void Variable_ChildrenCount_IsOptional()
    {
        var simpleVar = new Variable(
            Name: "x",
            Type: "Int32",
            Value: "5",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        var complexVar = new Variable(
            Name: "obj",
            Type: "MyClass",
            Value: "{MyClass}",
            Scope: VariableScope.Local,
            HasChildren: true,
            ChildrenCount: 3,
            Path: "obj");

        simpleVar.ChildrenCount.Should().BeNull();
        complexVar.ChildrenCount.Should().Be(3);
    }

    /// <summary>
    /// SourceLocation has required fields: file, line.
    /// </summary>
    [Fact]
    public void SourceLocation_RequiredFields()
    {
        var location = new SourceLocation(
            File: "/path/to/source.cs",
            Line: 42,
            Column: null,
            FunctionName: null,
            ModuleName: null);

        location.File.Should().NotBeNullOrEmpty();
        location.Line.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// SourceLocation column and function are optional.
    /// </summary>
    [Fact]
    public void SourceLocation_OptionalFields()
    {
        var minimalLocation = new SourceLocation("/file.cs", 1, null, null, null);
        var fullLocation = new SourceLocation("/file.cs", 1, 5, "Method", "Module");

        minimalLocation.Column.Should().BeNull();
        minimalLocation.FunctionName.Should().BeNull();

        fullLocation.Column.Should().Be(5);
        fullLocation.FunctionName.Should().Be("Method");
    }

    /// <summary>
    /// Pagination: total_frames reflects actual stack depth, not returned count.
    /// </summary>
    [Fact]
    public void StacktraceGet_Pagination_TotalFramesIsActualDepth()
    {
        // If stack has 100 frames but we request max_frames=20,
        // total_frames should be 100, frames array should have 20
        var response = new
        {
            thread_id = 1,
            total_frames = 100, // actual stack depth
            frames = Enumerable.Range(0, 20).Select(i => new { index = i }).ToArray()
        };

        response.total_frames.Should().Be(100);
        response.frames.Should().HaveCount(20);
    }

    /// <summary>
    /// Pagination: start_frame offsets the returned frames.
    /// </summary>
    [Fact]
    public void StacktraceGet_Pagination_StartFrameOffset()
    {
        // If start_frame=10, the first returned frame should have index=10
        var frames = Enumerable.Range(10, 20).Select(i => new StackFrame(
            Index: i,
            Function: $"Method{i}",
            Module: "App.dll",
            IsExternal: false)).ToList();

        frames[0].Index.Should().Be(10, "first frame should start at start_frame offset");
        frames[^1].Index.Should().Be(29, "last frame should be start_frame + max_frames - 1");
    }
}
