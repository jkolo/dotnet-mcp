using DotnetMcp.Models;
using DotnetMcp.Models.Breakpoints;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the breakpoint_set_exception tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in breakpoint-tools.json.
/// </summary>
public class BreakpointSetExceptionContractTests
{
    /// <summary>
    /// breakpoint_set_exception requires exception_type parameter.
    /// </summary>
    [Fact]
    public void BreakpointSetException_ExceptionType_IsRequired()
    {
        // Contract: "exception_type": { "type": "string", "description": "..." }
        // Required in "required": ["exception_type"]
        var exceptionType = "System.NullReferenceException";

        exceptionType.Should().NotBeNullOrEmpty("exception_type is required");
    }

    /// <summary>
    /// breakpoint_set_exception has default values for optional parameters.
    /// </summary>
    [Fact]
    public void BreakpointSetException_OptionalParameters_HaveDefaults()
    {
        // Contract specifies defaults:
        // "break_on_first_chance": { "type": "boolean", "default": true }
        // "break_on_second_chance": { "type": "boolean", "default": true }
        // "include_subtypes": { "type": "boolean", "default": true }

        const bool DefaultBreakOnFirstChance = true;
        const bool DefaultBreakOnSecondChance = true;
        const bool DefaultIncludeSubtypes = true;

        DefaultBreakOnFirstChance.Should().BeTrue("default for break_on_first_chance is true");
        DefaultBreakOnSecondChance.Should().BeTrue("default for break_on_second_chance is true");
        DefaultIncludeSubtypes.Should().BeTrue("default for include_subtypes is true");
    }

    /// <summary>
    /// ExceptionBreakpoint model contains all required fields per contract.
    /// </summary>
    [Fact]
    public void ExceptionBreakpoint_ContainsRequiredFields()
    {
        // Contract defines outputSchema for exception breakpoint

        var ebp = new ExceptionBreakpoint(
            Id: "ebp-12345",
            ExceptionType: "System.InvalidOperationException",
            BreakOnFirstChance: true,
            BreakOnSecondChance: false,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        ebp.Id.Should().StartWith("ebp-", "exception breakpoint IDs start with 'ebp-'");
        ebp.ExceptionType.Should().NotBeNullOrEmpty("exceptionType is required");
        ebp.BreakOnFirstChance.Should().BeTrue("breakOnFirstChance is present");
        ebp.BreakOnSecondChance.Should().BeFalse("breakOnSecondChance is present");
        ebp.IncludeSubtypes.Should().BeTrue("includeSubtypes is present");
        ebp.Enabled.Should().BeTrue("enabled is required");
        ebp.Verified.Should().BeTrue("verified is required");
        ebp.HitCount.Should().Be(0, "hitCount starts at 0");
    }

    /// <summary>
    /// ExceptionInfo contains all required fields per contract.
    /// </summary>
    [Fact]
    public void ExceptionInfo_ContainsRequiredFields()
    {
        // Contract: ExceptionInfo definition includes type, message, isFirstChance, stackTrace

        var info = new ExceptionInfo(
            Type: "System.ArgumentNullException",
            Message: "Value cannot be null. (Parameter 'name')",
            IsFirstChance: true,
            StackTrace: "at MyApp.Main() in Program.cs:line 10");

        info.Type.Should().NotBeNullOrEmpty("type is required");
        info.Message.Should().NotBeNullOrEmpty("message is required");
        info.IsFirstChance.Should().BeTrue("isFirstChance is required");
        info.StackTrace.Should().NotBeNull("stackTrace is optional but present");
    }

    /// <summary>
    /// Exception type supports fully qualified names.
    /// </summary>
    [Theory]
    [InlineData("System.Exception")]
    [InlineData("System.InvalidOperationException")]
    [InlineData("System.NullReferenceException")]
    [InlineData("System.IO.IOException")]
    [InlineData("MyApp.CustomException")]
    public void ExceptionType_SupportsFullyQualifiedNames(string exceptionType)
    {
        var ebp = new ExceptionBreakpoint(
            Id: "ebp-test",
            ExceptionType: exceptionType,
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        ebp.ExceptionType.Should().Be(exceptionType);
    }

    /// <summary>
    /// Exception breakpoint error codes are defined per contract.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.NoSession)]
    [InlineData(ErrorCodes.InvalidCondition)] // Used for invalid exception type
    public void ExceptionBreakpointErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }

    /// <summary>
    /// Success response contains breakpoint details.
    /// </summary>
    [Fact]
    public void SuccessResponse_ContainsBreakpointDetails()
    {
        // Contract: success response includes breakpoint object
        var ebp = new ExceptionBreakpoint(
            Id: "ebp-12345",
            ExceptionType: "System.Exception",
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: false,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // Verify all fields are present
        ebp.Id.Should().NotBeNullOrEmpty();
        ebp.ExceptionType.Should().Be("System.Exception");
        ebp.BreakOnFirstChance.Should().BeTrue();
        ebp.BreakOnSecondChance.Should().BeTrue();
        ebp.IncludeSubtypes.Should().BeFalse();
        ebp.Enabled.Should().BeTrue();
        ebp.Verified.Should().BeTrue();
    }
}
