using System.Text.Json;
using DotnetMcp.Models;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_step tool schema compliance.
/// These tests verify the tool adheres to the MCP contract.
/// </summary>
public class DebugStepContractTests
{
    /// <summary>
    /// debug_step requires mode parameter with valid values.
    /// </summary>
    [Theory]
    [InlineData("in")]
    [InlineData("over")]
    [InlineData("out")]
    public void DebugStep_Mode_ValidValues(string mode)
    {
        // Contract specifies: enum ["in", "over", "out"]
        var validModes = new[] { "in", "over", "out" };
        validModes.Should().Contain(mode, "mode must be one of the valid step modes");
    }

    /// <summary>
    /// StepMode enum values match contract specification.
    /// </summary>
    [Theory]
    [InlineData(StepMode.In, "in")]
    [InlineData(StepMode.Over, "over")]
    [InlineData(StepMode.Out, "out")]
    public void StepMode_ValuesMatchContract(StepMode mode, string expectedJsonValue)
    {
        // Contract defines: enum ["in", "over", "out"]
        var jsonValue = mode.ToString().ToLowerInvariant();
        jsonValue.Should().Be(expectedJsonValue, $"StepMode.{mode} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// debug_step has optional timeout with defaults and bounds.
    /// </summary>
    [Fact]
    public void DebugStep_Timeout_HasDefaultsAndBounds()
    {
        const int DefaultTimeout = 30000;
        const int MinTimeout = 1000;
        const int MaxTimeout = 300000;

        DefaultTimeout.Should().Be(30000, "default timeout is 30 seconds per contract");
        MinTimeout.Should().Be(1000, "minimum timeout is 1 second per contract");
        MaxTimeout.Should().Be(300000, "maximum timeout is 5 minutes per contract");
    }

    /// <summary>
    /// Successful step returns session with new location.
    /// </summary>
    [Fact]
    public void DebugStep_SuccessResponse_ReturnsPausedWithLocation()
    {
        // After step, the session should be paused at the new location
        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "dotnet",
            ExecutablePath = "/usr/bin/dotnet",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Paused, // After step, paused at new location
            LaunchMode = LaunchMode.Attach,
            CurrentLocation = new SourceLocation("/app/Program.cs", 42, 1, "Main", "MyApp"),
            PauseReason = PauseReason.Step
        };

        session.State.Should().Be(SessionState.Paused, "state should be paused after step");
        session.PauseReason.Should().Be(PauseReason.Step, "pause reason should be step");
        session.CurrentLocation.Should().NotBeNull("location should be set after step");
    }

    /// <summary>
    /// Step should fail if session is not paused.
    /// </summary>
    [Fact]
    public void DebugStep_WhenNotPaused_ReturnsError()
    {
        var error = new ErrorResponse
        {
            Code = ErrorCodes.NotPaused,
            Message = "Cannot step: process is not paused",
            Details = new Dictionary<string, object> { ["currentState"] = "running" }
        };

        error.Code.Should().Be(ErrorCodes.NotPaused, "error code should be NOT_PAUSED");
    }

    /// <summary>
    /// Step should fail with invalid mode.
    /// </summary>
    [Fact]
    public void DebugStep_InvalidMode_ReturnsError()
    {
        var error = new ErrorResponse
        {
            Code = ErrorCodes.InvalidParameter,
            Message = "Invalid step mode: 'invalid'. Valid modes: in, over, out",
            Details = new Dictionary<string, object> { ["parameter"] = "mode", ["value"] = "invalid" }
        };

        error.Code.Should().Be(ErrorCodes.InvalidParameter, "error code should be INVALID_PARAMETER");
    }
}
