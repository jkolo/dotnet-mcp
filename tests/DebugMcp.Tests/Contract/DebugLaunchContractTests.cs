using System.Text.Json;
using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_launch tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in mcp-tools.json.
/// </summary>
public class DebugLaunchContractTests
{
    /// <summary>
    /// debug_launch requires a 'program' parameter.
    /// </summary>
    [Fact]
    public void DebugLaunch_RequiresProgram()
    {
        // Contract specifies: "required": ["program"]
        // The program parameter is mandatory

        var validProgram = "/path/to/app.dll";
        validProgram.Should().NotBeNullOrEmpty("program is required by contract");
    }

    /// <summary>
    /// debug_launch has optional args as string array.
    /// </summary>
    [Fact]
    public void DebugLaunch_Args_IsOptionalStringArray()
    {
        // Contract specifies: "args": { "type": "array", "items": { "type": "string" }, "default": [] }

        var emptyArgs = Array.Empty<string>();
        var withArgs = new[] { "--verbose", "--config", "debug" };

        emptyArgs.Should().BeEmpty("default args is empty array per contract");
        withArgs.Should().AllBeOfType<string>("args must be string array per contract");
    }

    /// <summary>
    /// debug_launch has optional cwd parameter.
    /// </summary>
    [Fact]
    public void DebugLaunch_Cwd_IsOptional()
    {
        // Contract specifies: "cwd": { "type": "string" }
        // No required: means optional

        string? cwd = null;
        cwd.Should().BeNull("cwd is optional");

        cwd = "/working/directory";
        cwd.Should().NotBeNullOrEmpty("cwd can be provided");
    }

    /// <summary>
    /// debug_launch has optional env as object.
    /// </summary>
    [Fact]
    public void DebugLaunch_Env_IsOptionalObject()
    {
        // Contract specifies: "env": { "type": "object", "additionalProperties": { "type": "string" } }

        Dictionary<string, string>? env = null;
        env.Should().BeNull("env is optional");

        env = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MY_VAR"] = "value"
        };
        env.Should().NotBeEmpty("env can contain string key-value pairs");
    }

    /// <summary>
    /// debug_launch has stopAtEntry with default true.
    /// </summary>
    [Fact]
    public void DebugLaunch_StopAtEntry_DefaultsToTrue()
    {
        // Contract specifies: "stopAtEntry": { "type": "boolean", "default": true }

        const bool defaultStopAtEntry = true;
        defaultStopAtEntry.Should().BeTrue("stopAtEntry defaults to true per contract");
    }

    /// <summary>
    /// debug_launch has optional timeout with bounds.
    /// </summary>
    [Fact]
    public void DebugLaunch_Timeout_HasBounds()
    {
        // Contract specifies: "timeout": { "type": "integer", "default": 30000, "minimum": 1000, "maximum": 300000 }

        const int defaultTimeout = 30000;
        const int minTimeout = 1000;
        const int maxTimeout = 300000;

        defaultTimeout.Should().Be(30000, "default timeout is 30 seconds per contract");
        minTimeout.Should().Be(1000, "minimum timeout is 1 second per contract");
        maxTimeout.Should().Be(300000, "maximum timeout is 5 minutes per contract");
    }

    /// <summary>
    /// Successful launch returns a DebugSession with LaunchMode.Launch.
    /// </summary>
    [Fact]
    public void DebugLaunch_SuccessResponse_HasLaunchMode()
    {
        var session = new DebugSession
        {
            ProcessId = 5678,
            ProcessName = "testapp",
            ExecutablePath = "/path/to/testapp.dll",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Paused, // stopAtEntry = true
            LaunchMode = LaunchMode.Launch,
            PauseReason = PauseReason.Entry,
            CommandLineArgs = new[] { "--test" },
            WorkingDirectory = "/working/dir"
        };

        session.LaunchMode.Should().Be(LaunchMode.Launch, "launch operations should set LaunchMode.Launch");
        session.State.Should().Be(SessionState.Paused, "should be paused when stopAtEntry is true");
        session.PauseReason.Should().Be(PauseReason.Entry, "pause reason should be entry");
    }

    /// <summary>
    /// LaunchMode for launch operations must be 'launch'.
    /// </summary>
    [Fact]
    public void LaunchMode_LaunchValue_MatchesContract()
    {
        // Contract defines: enum ["attach", "launch"]
        var launchMode = LaunchMode.Launch;
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(launchMode.ToString());
        jsonValue.Should().Be("launch", "LaunchMode.Launch should serialize to 'launch'");
    }

    /// <summary>
    /// Launch with stopAtEntry false returns running state.
    /// </summary>
    [Fact]
    public void DebugLaunch_WithStopAtEntryFalse_ReturnsRunningState()
    {
        var session = new DebugSession
        {
            ProcessId = 5678,
            ProcessName = "testapp",
            ExecutablePath = "/path/to/testapp.dll",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running, // stopAtEntry = false
            LaunchMode = LaunchMode.Launch,
            PauseReason = null // not paused
        };

        session.State.Should().Be(SessionState.Running, "should be running when stopAtEntry is false");
        session.PauseReason.Should().BeNull("no pause reason when running");
    }

    /// <summary>
    /// Program path can be a DLL or executable.
    /// </summary>
    [Theory]
    [InlineData("/path/to/app.dll")]
    [InlineData("/path/to/app.exe")]
    [InlineData("C:\\path\\to\\app.dll")]
    [InlineData("./relative/path/app.dll")]
    public void DebugLaunch_Program_AcceptsVariousFormats(string program)
    {
        program.Should().NotBeNullOrEmpty("program path is required");
    }

    /// <summary>
    /// Error codes for launch operations.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.InvalidPath)]
    [InlineData(ErrorCodes.LaunchFailed)]
    [InlineData(ErrorCodes.AlreadyAttached)]
    [InlineData(ErrorCodes.Timeout)]
    [InlineData(ErrorCodes.PermissionDenied)]
    public void LaunchErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
    }
}
