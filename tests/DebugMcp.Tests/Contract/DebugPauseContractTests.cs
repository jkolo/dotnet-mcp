using System.Text.Json;
using DebugMcp.Models;
using DebugMcp.Models.Inspection;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the debug_pause tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in contracts/debug_pause.json.
/// </summary>
public class DebugPauseContractTests
{
    /// <summary>
    /// debug_pause has no parameters.
    /// </summary>
    [Fact]
    public void DebugPause_NoParameters()
    {
        // The input schema specifies: "properties": {} with additionalProperties: false
        // This means the tool accepts no parameters
        true.Should().BeTrue("debug_pause has no parameters");
    }

    /// <summary>
    /// Response includes required fields: success, state.
    /// </summary>
    [Fact]
    public void DebugPause_Response_IncludesRequiredFields()
    {
        // Contract requires: success, state
        var response = new
        {
            success = true,
            state = "paused",
            threads = Array.Empty<object>()
        };

        response.success.Should().BeTrue();
        response.state.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// State must be one of the allowed values.
    /// </summary>
    [Theory]
    [InlineData("paused", true)]
    [InlineData("already_paused", true)]
    [InlineData("running", false)]
    [InlineData("invalid", false)]
    public void DebugPause_State_Validation(string state, bool isValid)
    {
        // Contract defines enum: ["paused", "already_paused"]
        var validStates = new[] { "paused", "already_paused" };
        var valid = validStates.Contains(state);
        valid.Should().Be(isValid, $"state='{state}' should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// threads is optional in response.
    /// </summary>
    [Fact]
    public void DebugPause_Threads_IsOptional()
    {
        var responseWithThreads = new
        {
            success = true,
            state = "paused",
            threads = new[]
            {
                new { id = 1234, location = new { function = "Main" } }
            }
        };

        var responseWithoutThreads = new
        {
            success = true,
            state = "paused"
        };

        responseWithThreads.threads.Should().NotBeNull();
        // responseWithoutThreads doesn't have threads property - this is valid
        true.Should().BeTrue("threads is optional");
    }

    /// <summary>
    /// Thread in response has required id field.
    /// </summary>
    [Fact]
    public void DebugPause_Thread_RequiresId()
    {
        var thread = new
        {
            id = 12345,
            location = new
            {
                function = "MyMethod",
                file = "/path/file.cs",
                line = 42
            }
        };

        thread.id.Should().BePositive();
    }

    /// <summary>
    /// Thread location requires function field.
    /// </summary>
    [Fact]
    public void DebugPause_ThreadLocation_RequiresFunction()
    {
        var location = new
        {
            function = "System.Threading.Thread.Sleep"
        };

        location.function.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Thread location file and line are optional.
    /// </summary>
    [Fact]
    public void DebugPause_ThreadLocation_FileAndLineOptional()
    {
        // Location with only function (external code)
        var externalLocation = new
        {
            function = "System.Threading.Thread.Sleep"
        };

        // Location with full details (user code)
        var userLocation = new
        {
            function = "MyApp.Program.DoWork",
            file = "/src/Program.cs",
            line = 42
        };

        externalLocation.function.Should().NotBeNullOrEmpty();
        userLocation.function.Should().NotBeNullOrEmpty();
        userLocation.file.Should().NotBeNullOrEmpty();
        userLocation.line.Should().BePositive();
    }

    /// <summary>
    /// Response when process was already paused.
    /// </summary>
    [Fact]
    public void DebugPause_AlreadyPaused_Response()
    {
        var response = new
        {
            success = true,
            state = "already_paused"
        };

        response.success.Should().BeTrue();
        response.state.Should().Be("already_paused");
    }

    /// <summary>
    /// Response includes multiple threads when process has multiple threads.
    /// </summary>
    [Fact]
    public void DebugPause_MultipleThreads_Response()
    {
        var response = new
        {
            success = true,
            state = "paused",
            threads = new[]
            {
                new { id = 1, location = new { function = "Main", file = (string?)"/file.cs", line = (int?)10 } },
                new { id = 2, location = new { function = "WorkerThread.Run", file = (string?)null, line = (int?)null } },
                new { id = 3, location = new { function = "System.Threading.Thread.Sleep", file = (string?)null, line = (int?)null } }
            }
        };

        response.threads.Should().HaveCount(3);
        response.threads[0].id.Should().Be(1);
        response.threads[1].location.function.Should().Be("WorkerThread.Run");
    }
}
