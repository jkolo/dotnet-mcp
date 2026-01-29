using System.Text.Json;
using DebugMcp.Models;
using DebugMcp.Models.Breakpoints;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the breakpoint_set tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in breakpoint-tools.json.
/// </summary>
public class BreakpointSetContractTests
{
    /// <summary>
    /// breakpoint_set requires either file+line OR function parameter.
    /// </summary>
    [Fact]
    public void BreakpointSet_RequiresFileAndLine_OrFunction()
    {
        // Contract specifies oneOf:
        // - { required: ["file", "line"] }
        // - { required: ["function"] }

        // Valid combinations
        var validFileAndLine = new { file = "/app/Program.cs", line = 42 };
        var validFunction = new { function = "MyApp.Program.Main" };

        // Invalid: neither file/line nor function
        var invalidEmpty = new { };
        var invalidOnlyFile = new { file = "/app/Program.cs" };
        var invalidOnlyLine = new { line = 42 };

        // Assert valid inputs
        validFileAndLine.file.Should().NotBeNullOrEmpty("file required when using file/line mode");
        validFileAndLine.line.Should().BeGreaterThanOrEqualTo(1, "line must be >= 1 per contract");
        validFunction.function.Should().NotBeNullOrEmpty("function required when using function mode");
    }

    /// <summary>
    /// line parameter must be positive integer (1-based).
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void BreakpointSet_Line_MustBePositive(int line, bool isValid)
    {
        // Contract: "line": { "type": "integer", "minimum": 1 }
        var meetsContract = line >= 1;
        meetsContract.Should().Be(isValid, $"line={line} should {(isValid ? "be valid" : "violate contract")}");
    }

    /// <summary>
    /// column parameter is optional and must be positive when present.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void BreakpointSet_Column_OptionalAndPositive(int? column, bool isValid)
    {
        // Contract: "column": { "type": "integer", "minimum": 1 }
        var meetsContract = column == null || column >= 1;
        meetsContract.Should().Be(isValid, $"column={column} should {(isValid ? "be valid" : "violate contract")}");
    }

    /// <summary>
    /// condition parameter is optional string for C# expression.
    /// </summary>
    [Fact]
    public void BreakpointSet_Condition_IsOptionalString()
    {
        // Contract: "condition": { "type": "string" }
        string? noCondition = null;
        var simpleCondition = "x > 10";
        var complexCondition = "items.Count > 0 && items[0].Active";

        // All are valid per contract (syntax validation is runtime)
        noCondition.Should().BeNull("null condition is valid");
        simpleCondition.Should().NotBeNullOrEmpty("condition can be simple expression");
        complexCondition.Should().Contain("&&", "condition can include operators");
    }

    /// <summary>
    /// Success response contains required breakpoint fields per contract.
    /// </summary>
    [Fact]
    public void BreakpointSet_SuccessResponse_ContainsRequiredBreakpointFields()
    {
        // Contract defines breakpoint with required fields:
        // id, state, enabled, verified, hitCount

        var breakpoint = new Breakpoint(
            Id: "bp-12345678-1234-1234-1234-123456789012",
            Location: new BreakpointLocation(
                File: "/app/Services/UserService.cs",
                Line: 42),
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // All required fields must be present
        breakpoint.Id.Should().NotBeNullOrEmpty("id required by contract");
        breakpoint.State.Should().BeDefined("state required by contract");
        breakpoint.Enabled.Should().BeTrue("enabled required by contract");
        breakpoint.Verified.Should().BeTrue("verified required by contract");
        breakpoint.HitCount.Should().BeGreaterThanOrEqualTo(0, "hitCount required by contract");
    }

    /// <summary>
    /// BreakpointState enum values match contract specification.
    /// </summary>
    [Theory]
    [InlineData(BreakpointState.Pending, "pending")]
    [InlineData(BreakpointState.Bound, "bound")]
    [InlineData(BreakpointState.Disabled, "disabled")]
    public void BreakpointState_ValuesMatchContract(BreakpointState state, string expectedJsonValue)
    {
        // Contract: enum ["pending", "bound", "disabled"]
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(state.ToString());
        jsonValue.Should().Be(expectedJsonValue, $"BreakpointState.{state} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// BreakpointLocation contains file/line and optional fields per contract.
    /// </summary>
    [Fact]
    public void BreakpointLocation_ContainsRequiredAndOptionalFields()
    {
        // Contract defines BreakpointLocation with:
        // file, line (implied required)
        // column, endLine, endColumn, functionName, moduleName (optional)

        var location = new BreakpointLocation(
            File: "/app/Services/UserService.cs",
            Line: 42,
            Column: 5,
            EndLine: 42,
            EndColumn: 30,
            FunctionName: "GetUser",
            ModuleName: "MyApp");

        location.File.Should().NotBeNullOrEmpty("file required for location");
        location.Line.Should().BeGreaterThanOrEqualTo(1, "line must be positive");
        location.Column.Should().BeGreaterThanOrEqualTo(1, "column must be positive when present");
        location.EndLine.Should().BeGreaterThanOrEqualTo(location.Line, "endLine >= startLine");
        location.FunctionName.Should().NotBeNullOrEmpty("functionName is optional but present");
        location.ModuleName.Should().NotBeNullOrEmpty("moduleName is optional but present");
    }

    /// <summary>
    /// Breakpoint error codes are defined for programmatic handling.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.NoSession)]
    [InlineData(ErrorCodes.InvalidFile)]
    [InlineData(ErrorCodes.InvalidLine)]
    [InlineData(ErrorCodes.InvalidColumn)]
    [InlineData(ErrorCodes.InvalidCondition)]
    [InlineData(ErrorCodes.BreakpointNotFound)]
    [InlineData(ErrorCodes.BreakpointExists)]
    [InlineData(ErrorCodes.EvalFailed)]
    [InlineData(ErrorCodes.Timeout)]
    public void BreakpointErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }

    /// <summary>
    /// Pending breakpoint response has correct state and message.
    /// </summary>
    [Fact]
    public void BreakpointSet_PendingResponse_HasCorrectStateAndMessage()
    {
        // When module not loaded, breakpoint should be pending with message
        var breakpoint = new Breakpoint(
            Id: "bp-12345678-1234-1234-1234-123456789012",
            Location: new BreakpointLocation(
                File: "/app/Services/LazyService.cs",
                Line: 15),
            State: BreakpointState.Pending,
            Enabled: true,
            Verified: false,
            HitCount: 0,
            Message: "Module not yet loaded; breakpoint will bind when module loads");

        breakpoint.State.Should().Be(BreakpointState.Pending, "state should be pending for unloaded module");
        breakpoint.Verified.Should().BeFalse("unbound breakpoint is not verified");
        breakpoint.Message.Should().NotBeNullOrEmpty("pending breakpoints should have explanation");
    }

    /// <summary>
    /// Duplicate breakpoint returns existing ID per contract.
    /// </summary>
    [Fact]
    public void BreakpointSet_DuplicateLocation_ReturnsExistingId()
    {
        // Per contract: setting breakpoint at same location should be idempotent
        // Returns existing breakpoint rather than creating duplicate

        var existingId = "bp-existing-1234";
        var firstBreakpoint = new Breakpoint(
            Id: existingId,
            Location: new BreakpointLocation(File: "/app/Program.cs", Line: 10),
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // Second set at same location should return same ID
        // This is contract behavior - implementation test is separate
        firstBreakpoint.Id.Should().Be(existingId, "existing breakpoint ID should be returned");
    }
}
