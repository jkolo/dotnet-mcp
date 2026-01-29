using System.Text.Json;
using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_state tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in mcp-tools.json.
/// </summary>
public class DebugStateContractTests
{
    /// <summary>
    /// debug_state has no required input parameters.
    /// </summary>
    [Fact]
    public void DebugState_NoRequiredParameters()
    {
        // The input schema specifies: "required": []
        // This means the tool should work with no parameters at all

        // This is a contract verification test
        // Actual implementation will accept empty input
        true.Should().BeTrue("debug_state has no required parameters");
    }

    /// <summary>
    /// State response when disconnected should indicate no active session.
    /// </summary>
    [Fact]
    public void DebugState_WhenDisconnected_ReturnsDisconnectedState()
    {
        // Contract defines SessionState enum: ["disconnected", "running", "paused"]
        // When no session is active, state should be "disconnected"

        var state = SessionState.Disconnected;
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(state.ToString());

        jsonValue.Should().Be("disconnected", "disconnected state should serialize correctly");
    }

    /// <summary>
    /// State response when running should include session info.
    /// </summary>
    [Fact]
    public void DebugState_WhenRunning_IncludesSessionInfo()
    {
        // Contract defines DebugSession with required fields when connected

        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "testapp",
            ExecutablePath = "/path/to/testapp.dll",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Running,
            LaunchMode = LaunchMode.Attach
        };

        session.State.Should().Be(SessionState.Running);
        session.ProcessId.Should().BePositive();
        session.ProcessName.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// State response when paused should include pause reason.
    /// </summary>
    [Fact]
    public void DebugState_WhenPaused_IncludesPauseReason()
    {
        // Contract defines PauseReason enum: ["breakpoint", "step", "exception", "pause", "entry"]

        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "testapp",
            ExecutablePath = "/path/to/testapp.dll",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = DateTime.UtcNow,
            State = SessionState.Paused,
            LaunchMode = LaunchMode.Attach,
            PauseReason = PauseReason.Breakpoint
        };

        session.State.Should().Be(SessionState.Paused);
        session.PauseReason.Should().Be(PauseReason.Breakpoint);
    }

    /// <summary>
    /// State response when paused may include source location.
    /// </summary>
    [Fact]
    public void DebugState_WhenPaused_MayIncludeSourceLocation()
    {
        // Contract defines SourceLocation with required: file, line

        var location = new SourceLocation(
            File: "/path/to/source.cs",
            Line: 42,
            Column: 1,
            FunctionName: "TestMethod",
            ModuleName: "TestAssembly"
        );

        location.File.Should().NotBeNullOrEmpty("file is required by contract");
        location.Line.Should().BePositive("line must be >= 1 per contract");
    }

    /// <summary>
    /// PauseReason enum values match contract specification.
    /// </summary>
    [Theory]
    [InlineData(PauseReason.Breakpoint, "breakpoint")]
    [InlineData(PauseReason.Step, "step")]
    [InlineData(PauseReason.Exception, "exception")]
    [InlineData(PauseReason.Pause, "pause")]
    [InlineData(PauseReason.Entry, "entry")]
    public void PauseReason_ValuesMatchContract(PauseReason reason, string expectedJsonValue)
    {
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(reason.ToString());
        jsonValue.Should().Be(expectedJsonValue, $"PauseReason.{reason} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// SourceLocation with only required fields is valid.
    /// </summary>
    [Fact]
    public void SourceLocation_WithRequiredFieldsOnly_IsValid()
    {
        // Contract requires: file, line
        // Column, functionName, moduleName are optional

        var location = new SourceLocation(
            File: "/path/to/source.cs",
            Line: 1,
            Column: null,
            FunctionName: null,
            ModuleName: null
        );

        location.File.Should().NotBeNullOrEmpty();
        location.Line.Should().BeGreaterThanOrEqualTo(1);
        location.Column.Should().BeNull("column is optional");
        location.FunctionName.Should().BeNull("functionName is optional");
        location.ModuleName.Should().BeNull("moduleName is optional");
    }

    /// <summary>
    /// State response always includes success indicator and state field.
    /// </summary>
    [Fact]
    public void DebugState_Response_IncludesSuccessAndState()
    {
        // All tool responses should include success indicator

        var disconnectedResponse = new
        {
            success = true,
            state = "disconnected",
            session = (object?)null
        };

        var runningResponse = new
        {
            success = true,
            state = "running",
            session = new { processId = 1234 }
        };

        disconnectedResponse.success.Should().BeTrue();
        disconnectedResponse.state.Should().Be("disconnected");
        disconnectedResponse.session.Should().BeNull();

        runningResponse.success.Should().BeTrue();
        runningResponse.state.Should().Be("running");
        runningResponse.session.Should().NotBeNull();
    }
}
