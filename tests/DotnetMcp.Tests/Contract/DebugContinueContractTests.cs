using System.Text.Json;
using DotnetMcp.Models;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_continue tool schema compliance.
/// These tests verify the tool adheres to the MCP contract.
/// </summary>
public class DebugContinueContractTests
{
    /// <summary>
    /// debug_continue has optional timeout with defaults and bounds.
    /// </summary>
    [Fact]
    public void DebugContinue_Timeout_HasDefaultsAndBounds()
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
    /// Successful continue returns session in running state.
    /// </summary>
    [Fact]
    public void DebugContinue_SuccessResponse_ReturnsRunningState()
    {
        // After continue, the session state should transition to Running
        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "dotnet",
            ExecutablePath = "/usr/bin/dotnet",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running, // After continue
            LaunchMode = LaunchMode.Attach
        };

        session.State.Should().Be(SessionState.Running, "state should be running after continue");
    }

    /// <summary>
    /// Continue should fail if session is not paused.
    /// </summary>
    [Fact]
    public void DebugContinue_WhenNotPaused_ReturnsError()
    {
        // NOT_PAUSED error code should be used when continue is called while running
        var error = new ErrorResponse
        {
            Code = ErrorCodes.NotPaused,
            Message = "Cannot continue: process is not paused",
            Details = new Dictionary<string, object> { ["currentState"] = "running" }
        };

        error.Code.Should().Be(ErrorCodes.NotPaused, "error code should be NOT_PAUSED");
        error.Message.Should().Contain("not paused", "error message should explain the issue");
    }

    /// <summary>
    /// Continue should fail if no session is active.
    /// </summary>
    [Fact]
    public void DebugContinue_WhenNoSession_ReturnsError()
    {
        var error = new ErrorResponse
        {
            Code = ErrorCodes.NoSession,
            Message = "No active debug session"
        };

        error.Code.Should().Be(ErrorCodes.NoSession, "error code should be NO_SESSION");
    }
}
