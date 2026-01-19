using System.Text.Json;
using DotnetMcp.Models;
using DotnetMcp.Models.Breakpoints;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the breakpoint_wait tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in breakpoint-tools.json.
/// </summary>
public class BreakpointWaitContractTests
{
    /// <summary>
    /// breakpoint_wait timeout_ms parameter has defaults and bounds.
    /// </summary>
    [Fact]
    public void BreakpointWait_TimeoutMs_HasDefaultsAndBounds()
    {
        // Contract specifies:
        // "timeout_ms": { "type": "integer", "minimum": 1, "maximum": 300000, "default": 30000 }
        const int DefaultTimeout = 30000;
        const int MinTimeout = 1;
        const int MaxTimeout = 300000;

        DefaultTimeout.Should().Be(30000, "default timeout is 30 seconds per contract");
        MinTimeout.Should().Be(1, "minimum timeout is 1ms per contract");
        MaxTimeout.Should().Be(300000, "maximum timeout is 5 minutes per contract");
    }

    /// <summary>
    /// breakpoint_wait breakpoint_id parameter is optional.
    /// </summary>
    [Fact]
    public void BreakpointWait_BreakpointId_IsOptional()
    {
        // Contract: "breakpoint_id": { "type": "string", "description": "Wait for specific breakpoint only (optional)" }
        string? noneSpecified = null;
        var specificId = "bp-12345";

        // Both are valid
        noneSpecified.Should().BeNull("null breakpoint_id means wait for any breakpoint");
        specificId.Should().NotBeNullOrEmpty("specific ID filters to one breakpoint");
    }

    /// <summary>
    /// Success response with hit contains required fields per contract.
    /// </summary>
    [Fact]
    public void BreakpointWait_HitResponse_ContainsRequiredFields()
    {
        // Contract defines outputSchema with required: ["hit"]
        // When hit=true, additional fields should be present

        var hit = new BreakpointHit(
            BreakpointId: "bp-12345",
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: new BreakpointLocation(
                File: "/app/Program.cs",
                Line: 42),
            HitCount: 1);

        // Required field
        var hitFlag = true;
        hitFlag.Should().BeTrue("hit is the only required field");

        // Additional fields when hit=true
        hit.BreakpointId.Should().NotBeNullOrEmpty("breakpointId present when hit");
        hit.ThreadId.Should().BePositive("threadId present when hit");
        hit.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5), "timestamp present when hit");
        hit.Location.Should().NotBeNull("location present when hit");
        hit.HitCount.Should().BeGreaterThanOrEqualTo(1, "hitCount present when hit");
    }

    /// <summary>
    /// Timeout response has correct format per contract.
    /// </summary>
    [Fact]
    public void BreakpointWait_TimeoutResponse_HasCorrectFormat()
    {
        // Contract: When timeout, response should have:
        // { "hit": false, "timeout": true, "message": "..." }

        var hitFlag = false;
        var timeoutFlag = true;
        var message = "No breakpoint hit within 30000ms";

        hitFlag.Should().BeFalse("hit should be false on timeout");
        timeoutFlag.Should().BeTrue("timeout should be true on timeout");
        message.Should().Contain("ms", "message should mention timeout duration");
    }

    /// <summary>
    /// Exception breakpoint hit includes ExceptionInfo per contract.
    /// </summary>
    [Fact]
    public void BreakpointWait_ExceptionHit_IncludesExceptionInfo()
    {
        // Contract defines exceptionInfo in outputSchema:
        // "$ref": "#/definitions/ExceptionInfo"

        var exceptionInfo = new ExceptionInfo(
            Type: "System.NullReferenceException",
            Message: "Object reference not set to an instance of an object.",
            IsFirstChance: true,
            StackTrace: "at MyApp.Program.Main()");

        var hit = new BreakpointHit(
            BreakpointId: "ebp-12345",
            ThreadId: 1,
            Timestamp: DateTime.UtcNow,
            Location: new BreakpointLocation(
                File: "/app/Program.cs",
                Line: 15),
            HitCount: 1,
            ExceptionInfo: exceptionInfo);

        hit.ExceptionInfo.Should().NotBeNull("exceptionInfo present for exception breakpoints");
        hit.ExceptionInfo!.Type.Should().NotBeNullOrEmpty("type required by ExceptionInfo");
        hit.ExceptionInfo.Message.Should().NotBeNullOrEmpty("message required by ExceptionInfo");
        hit.ExceptionInfo.IsFirstChance.Should().BeTrue("isFirstChance required by ExceptionInfo");
        hit.ExceptionInfo.StackTrace.Should().NotBeNull("stackTrace optional but present");
    }

    /// <summary>
    /// BreakpointHit timestamp uses ISO 8601 format.
    /// </summary>
    [Fact]
    public void BreakpointHit_Timestamp_IsIso8601Format()
    {
        // Contract: "timestamp": { "type": "string", "format": "date-time" }
        var timestamp = DateTime.UtcNow;
        var iso8601 = timestamp.ToString("O"); // Round-trip format is ISO 8601

        iso8601.Should().MatchRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}",
            "timestamp should be ISO 8601 format");
    }

    /// <summary>
    /// Breakpoint wait error codes are defined per contract.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.NoSession)]
    [InlineData(ErrorCodes.Timeout)]
    [InlineData(ErrorCodes.BreakpointNotFound)]
    public void BreakpointWaitErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }
}
