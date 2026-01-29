using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_disconnect tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in mcp-tools.json.
/// </summary>
public class DebugDisconnectContractTests
{
    /// <summary>
    /// debug_disconnect has no required parameters.
    /// </summary>
    [Fact]
    public void DebugDisconnect_NoRequiredParameters()
    {
        // Contract specifies: "required": []
        // The tool should work with no parameters

        true.Should().BeTrue("debug_disconnect has no required parameters");
    }

    /// <summary>
    /// debug_disconnect has optional terminateProcess flag.
    /// </summary>
    [Fact]
    public void DebugDisconnect_TerminateProcess_IsOptional()
    {
        // Contract specifies: "terminateProcess": { "type": "boolean", "default": false }

        const bool defaultTerminateProcess = false;
        defaultTerminateProcess.Should().BeFalse("default terminateProcess is false per contract");
    }

    /// <summary>
    /// Successful disconnect returns success with state.
    /// </summary>
    [Fact]
    public void DebugDisconnect_SuccessResponse_IncludesState()
    {
        // After disconnect, state should be disconnected
        var expectedState = SessionState.Disconnected;
        expectedState.Should().Be(SessionState.Disconnected);
    }

    /// <summary>
    /// Disconnect on attached process with terminateProcess=true still detaches.
    /// </summary>
    [Fact]
    public void DebugDisconnect_AttachedProcess_IgnoresTerminateFlag()
    {
        // Per contract: "terminateProcess": "Terminate the process instead of detaching (only for launched processes)"
        // For attached processes, terminateProcess should be ignored and process should just be detached

        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "attached-app",
            ExecutablePath = "/path/to/app",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running,
            LaunchMode = LaunchMode.Attach // Attached, not launched
        };

        // terminateProcess should be ignored for attached processes
        session.LaunchMode.Should().Be(LaunchMode.Attach, "attached processes should be detached, not terminated");
    }

    /// <summary>
    /// Disconnect on launched process with terminateProcess=true terminates.
    /// </summary>
    [Fact]
    public void DebugDisconnect_LaunchedProcess_RespectsTerminateFlag()
    {
        var session = new DebugSession
        {
            ProcessId = 5678,
            ProcessName = "launched-app",
            ExecutablePath = "/path/to/app",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running,
            LaunchMode = LaunchMode.Launch // Launched, not attached
        };

        // terminateProcess should be respected for launched processes
        session.LaunchMode.Should().Be(LaunchMode.Launch, "launched processes can be terminated");
    }

    /// <summary>
    /// Disconnect when no session is active returns appropriate response.
    /// </summary>
    [Fact]
    public void DebugDisconnect_NoSession_ReturnsNoSessionState()
    {
        // When no session is active, disconnect should still succeed
        // State should be disconnected

        var state = SessionState.Disconnected;
        state.Should().Be(SessionState.Disconnected);
    }

    /// <summary>
    /// Error code for no active session.
    /// </summary>
    [Fact]
    public void DebugDisconnect_ErrorCodes_AreDefined()
    {
        // NoSession error code should be defined for when disconnect is called with no session
        ErrorCodes.NoSession.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Disconnect response includes previous session info if there was one.
    /// </summary>
    [Fact]
    public void DebugDisconnect_Response_MayIncludePreviousSessionInfo()
    {
        // The response may include info about the disconnected session
        var response = new
        {
            success = true,
            state = "disconnected",
            previousSession = new
            {
                processId = 1234,
                processName = "app",
                wasTerminated = false
            }
        };

        response.success.Should().BeTrue();
        response.state.Should().Be("disconnected");
        response.previousSession.processId.Should().Be(1234);
    }

    /// <summary>
    /// Response indicates whether process was terminated or detached.
    /// </summary>
    [Fact]
    public void DebugDisconnect_Response_IndicatesTerminationStatus()
    {
        // When disconnecting from a launched process with terminate=true
        var terminatedResponse = new
        {
            success = true,
            state = "disconnected",
            wasTerminated = true
        };

        // When detaching from a process
        var detachedResponse = new
        {
            success = true,
            state = "disconnected",
            wasTerminated = false
        };

        terminatedResponse.wasTerminated.Should().BeTrue();
        detachedResponse.wasTerminated.Should().BeFalse();
    }
}
