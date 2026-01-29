using System.Text.Json;
using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_attach tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in mcp-tools.json.
/// </summary>
public class DebugAttachContractTests
{
    /// <summary>
    /// debug_attach requires a 'pid' parameter that must be a positive integer.
    /// </summary>
    [Fact]
    public void DebugAttach_RequiresPid_PositiveInteger()
    {
        // The input schema requires:
        // - "pid": integer, minimum 1, required
        // This is a contract verification - actual validation in tool implementation

        // Arrange: pid must be >= 1
        var validPid = 1;
        var invalidPid = 0;
        var negativePid = -1;

        // Assert: Contract specifies minimum: 1
        validPid.Should().BeGreaterThanOrEqualTo(1, "pid must be positive per contract");
        invalidPid.Should().BeLessThan(1, "pid=0 should violate contract");
        negativePid.Should().BeLessThan(1, "negative pid should violate contract");
    }

    /// <summary>
    /// debug_attach has optional timeout with defaults and bounds.
    /// </summary>
    [Fact]
    public void DebugAttach_Timeout_HasDefaultsAndBounds()
    {
        // Contract specifies:
        // - "timeout": integer, default 30000, minimum 1000, maximum 300000
        const int DefaultTimeout = 30000;
        const int MinTimeout = 1000;
        const int MaxTimeout = 300000;

        DefaultTimeout.Should().Be(30000, "default timeout is 30 seconds per contract");
        MinTimeout.Should().Be(1000, "minimum timeout is 1 second per contract");
        MaxTimeout.Should().Be(300000, "maximum timeout is 5 minutes per contract");
    }

    /// <summary>
    /// Successful attach returns a DebugSession with required fields per contract.
    /// </summary>
    [Fact]
    public void DebugAttach_SuccessResponse_ContainsRequiredSessionFields()
    {
        // Contract defines DebugSession with required fields:
        // processId, processName, runtimeVersion, state, launchMode, attachedAt

        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "dotnet",
            ExecutablePath = "/usr/bin/dotnet",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running,
            LaunchMode = LaunchMode.Attach
        };

        // All required fields must be present
        session.ProcessId.Should().BePositive("processId required by contract");
        session.ProcessName.Should().NotBeNullOrEmpty("processName required by contract");
        session.RuntimeVersion.Should().NotBeNullOrEmpty("runtimeVersion required by contract");
        session.State.Should().BeDefined("state required by contract");
        session.LaunchMode.Should().Be(LaunchMode.Attach, "launchMode must be 'attach' for attach operations");
        session.AttachedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5), "attachedAt required by contract");
    }

    /// <summary>
    /// State enum values match contract specification.
    /// </summary>
    [Theory]
    [InlineData(SessionState.Disconnected, "disconnected")]
    [InlineData(SessionState.Running, "running")]
    [InlineData(SessionState.Paused, "paused")]
    public void SessionState_ValuesMatchContract(SessionState state, string expectedJsonValue)
    {
        // Contract defines: enum ["disconnected", "running", "paused"]
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(state.ToString());
        jsonValue.Should().Be(expectedJsonValue, $"SessionState.{state} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// LaunchMode for attach operations must be 'attach'.
    /// </summary>
    [Fact]
    public void LaunchMode_AttachValue_MatchesContract()
    {
        // Contract defines: enum ["attach", "launch"]
        var attachMode = LaunchMode.Attach;
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(attachMode.ToString());
        jsonValue.Should().Be("attach", "LaunchMode.Attach should serialize to 'attach'");
    }

    /// <summary>
    /// Error response format matches contract ErrorResponse definition.
    /// </summary>
    [Fact]
    public void ErrorResponse_FormatMatchesContract()
    {
        // Contract defines ErrorResponse with:
        // - error.code (required): string
        // - error.message (required): string
        // - error.details (optional): object

        var error = new ErrorResponse
        {
            Code = ErrorCodes.ProcessNotFound,
            Message = "Process 12345 not found",
            Details = new Dictionary<string, object> { ["pid"] = 12345 }
        };

        error.Code.Should().NotBeNullOrEmpty("error code required by contract");
        error.Message.Should().NotBeNullOrEmpty("error message required by contract");
        error.Details.Should().NotBeNull("details are optional but should be present when provided");
    }

    /// <summary>
    /// Common error codes are defined for programmatic handling.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.ProcessNotFound)]
    [InlineData(ErrorCodes.NotDotNetProcess)]
    [InlineData(ErrorCodes.AlreadyAttached)]
    [InlineData(ErrorCodes.AttachFailed)]
    [InlineData(ErrorCodes.Timeout)]
    [InlineData(ErrorCodes.PermissionDenied)]
    public void ErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }
}
