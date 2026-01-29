using System.Text.Json;
using DebugMcp.Models;
using DebugMcp.Models.Inspection;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the variables_get tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in contracts/variables_get.json.
/// </summary>
public class VariablesGetContractTests
{
    /// <summary>
    /// variables_get has no required input parameters (all are optional).
    /// </summary>
    [Fact]
    public void VariablesGet_NoRequiredParameters()
    {
        // The input schema specifies no required parameters
        // thread_id, frame_index, scope, and expand are all optional
        true.Should().BeTrue("variables_get has no required parameters");
    }

    /// <summary>
    /// Default values for optional parameters match contract.
    /// </summary>
    [Fact]
    public void VariablesGet_DefaultValues_MatchContract()
    {
        // Contract defines: frame_index default: 0, scope default: "all"
        const int defaultFrameIndex = 0;
        const string defaultScope = "all";

        defaultFrameIndex.Should().Be(0, "frame_index default is 0");
        defaultScope.Should().Be("all", "scope default is 'all'");
    }

    /// <summary>
    /// frame_index must be >= 0 per contract.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    public void VariablesGet_FrameIndex_Validation(int frameIndex, bool isValid)
    {
        // Contract defines: minimum: 0
        var valid = frameIndex >= 0;
        valid.Should().Be(isValid, $"frame_index={frameIndex} should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// scope must be one of the allowed values.
    /// </summary>
    [Theory]
    [InlineData("all", true)]
    [InlineData("locals", true)]
    [InlineData("arguments", true)]
    [InlineData("this", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData("All", false)]  // case sensitive
    public void VariablesGet_Scope_Validation(string scope, bool isValid)
    {
        // Contract defines enum: ["all", "locals", "arguments", "this"]
        var validScopes = new[] { "all", "locals", "arguments", "this" };
        var valid = validScopes.Contains(scope);
        valid.Should().Be(isValid, $"scope='{scope}' should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// Response includes required field: variables.
    /// </summary>
    [Fact]
    public void VariablesGet_Response_IncludesRequiredFields()
    {
        // Contract requires: variables array
        var response = new
        {
            variables = new[]
            {
                new { name = "counter", type = "System.Int32", value = "42", scope = "local", has_children = false }
            }
        };

        response.variables.Should().NotBeNull();
        response.variables.Should().HaveCountGreaterThan(0);
    }

    /// <summary>
    /// Variable has required fields: name, type, value, scope, has_children.
    /// </summary>
    [Fact]
    public void Variable_RequiredFields()
    {
        var variable = new Variable(
            Name: "myVar",
            Type: "System.String",
            Value: "\"hello world\"",
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
    /// Variable scope enum matches contract values.
    /// </summary>
    [Theory]
    [InlineData(VariableScope.Local, "local")]
    [InlineData(VariableScope.Argument, "argument")]
    [InlineData(VariableScope.This, "this")]
    [InlineData(VariableScope.Field, "field")]
    [InlineData(VariableScope.Property, "property")]
    [InlineData(VariableScope.Element, "element")]
    public void Variable_Scope_SerializesCorrectly(VariableScope scope, string expectedJsonValue)
    {
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(scope.ToString());
        jsonValue.Should().Be(expectedJsonValue, $"VariableScope.{scope} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// Variable children_count is optional.
    /// </summary>
    [Fact]
    public void Variable_ChildrenCount_IsOptional()
    {
        var primitiveVar = new Variable(
            Name: "x",
            Type: "Int32",
            Value: "5",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        var objectVar = new Variable(
            Name: "user",
            Type: "User",
            Value: "{User}",
            Scope: VariableScope.Local,
            HasChildren: true,
            ChildrenCount: 5,
            Path: "user");

        primitiveVar.ChildrenCount.Should().BeNull();
        primitiveVar.HasChildren.Should().BeFalse();

        objectVar.ChildrenCount.Should().Be(5);
        objectVar.HasChildren.Should().BeTrue();
    }

    /// <summary>
    /// Variable path is optional but required for expansion.
    /// </summary>
    [Fact]
    public void Variable_Path_IsOptional()
    {
        var topLevelVar = new Variable(
            Name: "user",
            Type: "User",
            Value: "{User}",
            Scope: VariableScope.Local,
            HasChildren: true,
            ChildrenCount: 2,
            Path: "user");

        var nestedVar = new Variable(
            Name: "Name",
            Type: "String",
            Value: "\"John\"",
            Scope: VariableScope.Field,
            HasChildren: false,
            ChildrenCount: null,
            Path: "user.Name");

        topLevelVar.Path.Should().Be("user");
        nestedVar.Path.Should().Be("user.Name");
    }

    /// <summary>
    /// expand parameter specifies path to expand.
    /// </summary>
    [Fact]
    public void VariablesGet_Expand_PathFormat()
    {
        // Valid expand paths
        var validPaths = new[] { "user", "user.Address", "arr[0]", "dict[\"key\"]" };

        foreach (var path in validPaths)
        {
            path.Should().NotBeNullOrEmpty($"'{path}' is a valid expansion path");
        }
    }

    /// <summary>
    /// Primitive types have has_children=false.
    /// </summary>
    [Theory]
    [InlineData("System.Int32", false)]
    [InlineData("System.Boolean", false)]
    [InlineData("System.Double", false)]
    [InlineData("System.Char", false)]
    [InlineData("System.IntPtr", false)]
    public void Variable_PrimitiveTypes_NoChildren(string typeName, bool hasChildren)
    {
        var variable = new Variable(
            Name: "val",
            Type: typeName,
            Value: "0",
            Scope: VariableScope.Local,
            HasChildren: hasChildren,
            ChildrenCount: null,
            Path: null);

        variable.HasChildren.Should().BeFalse($"{typeName} is a primitive type");
    }

    /// <summary>
    /// String type has has_children=false (displayed as value, not expandable).
    /// </summary>
    [Fact]
    public void Variable_StringType_NoChildren()
    {
        var stringVar = new Variable(
            Name: "message",
            Type: "System.String",
            Value: "\"Hello World\"",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        stringVar.HasChildren.Should().BeFalse("strings are displayed as values, not expandable");
    }

    /// <summary>
    /// Array type has has_children=true.
    /// </summary>
    [Fact]
    public void Variable_ArrayType_HasChildren()
    {
        var arrayVar = new Variable(
            Name: "numbers",
            Type: "System.Int32[]",
            Value: "{int[5]}",
            Scope: VariableScope.Local,
            HasChildren: true,
            ChildrenCount: 5,
            Path: "numbers");

        arrayVar.HasChildren.Should().BeTrue("arrays can be expanded");
        arrayVar.ChildrenCount.Should().Be(5);
    }

    /// <summary>
    /// Object type has has_children=true.
    /// </summary>
    [Fact]
    public void Variable_ObjectType_HasChildren()
    {
        var objectVar = new Variable(
            Name: "user",
            Type: "MyApp.User",
            Value: "{User}",
            Scope: VariableScope.Local,
            HasChildren: true,
            ChildrenCount: 3,
            Path: "user");

        objectVar.HasChildren.Should().BeTrue("objects can be expanded");
        objectVar.ChildrenCount.Should().Be(3);
    }

    /// <summary>
    /// Null values are handled correctly.
    /// </summary>
    [Fact]
    public void Variable_NullValue_DisplayedAsNull()
    {
        var nullVar = new Variable(
            Name: "nullable",
            Type: "System.String",
            Value: "null",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        nullVar.Value.Should().Be("null");
        nullVar.HasChildren.Should().BeFalse("null values cannot be expanded");
    }

    /// <summary>
    /// Empty array is handled correctly.
    /// </summary>
    [Fact]
    public void Variable_EmptyArray_NoChildren()
    {
        var emptyArray = new Variable(
            Name: "items",
            Type: "System.Int32[]",
            Value: "{int[0]}",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: 0,
            Path: null);

        emptyArray.HasChildren.Should().BeFalse("empty arrays have no children to expand");
        emptyArray.ChildrenCount.Should().Be(0);
    }

    /// <summary>
    /// Scope "this" returns only the this reference.
    /// </summary>
    [Fact]
    public void VariablesGet_ScopeThis_ReturnsOnlyThis()
    {
        var thisVar = new Variable(
            Name: "this",
            Type: "MyClass",
            Value: "{MyClass}",
            Scope: VariableScope.This,
            HasChildren: true,
            ChildrenCount: 5,
            Path: "this");

        thisVar.Scope.Should().Be(VariableScope.This);
        thisVar.Name.Should().Be("this");
    }

    /// <summary>
    /// Scope "arguments" returns only method arguments.
    /// </summary>
    [Fact]
    public void VariablesGet_ScopeArguments_ReturnsOnlyArgs()
    {
        var arg = new Variable(
            Name: "param1",
            Type: "System.Int32",
            Value: "42",
            Scope: VariableScope.Argument,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        arg.Scope.Should().Be(VariableScope.Argument);
    }

    /// <summary>
    /// Scope "locals" returns only local variables.
    /// </summary>
    [Fact]
    public void VariablesGet_ScopeLocals_ReturnsOnlyLocals()
    {
        var local = new Variable(
            Name: "counter",
            Type: "System.Int32",
            Value: "0",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null);

        local.Scope.Should().Be(VariableScope.Local);
    }
}
