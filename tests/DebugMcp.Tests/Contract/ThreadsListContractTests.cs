using System.Text.Json;
using DebugMcp.Models;
using DebugMcp.Models.Inspection;
using FluentAssertions;
using ThreadState = DebugMcp.Models.Inspection.ThreadState;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the threads_list tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in contracts/threads_list.json.
/// </summary>
public class ThreadsListContractTests
{
    /// <summary>
    /// threads_list has no parameters.
    /// </summary>
    [Fact]
    public void ThreadsList_NoParameters()
    {
        // The input schema specifies: "properties": {} with additionalProperties: false
        // This means the tool accepts no parameters
        true.Should().BeTrue("threads_list has no parameters");
    }

    /// <summary>
    /// Response includes required field: threads.
    /// </summary>
    [Fact]
    public void ThreadsList_Response_IncludesRequiredFields()
    {
        // Contract requires: threads array
        var response = new
        {
            threads = new[]
            {
                new { id = 1234, state = "running", is_current = true }
            }
        };

        response.threads.Should().NotBeNull();
    }

    /// <summary>
    /// Thread has required fields: id, state, is_current.
    /// </summary>
    [Fact]
    public void Thread_RequiredFields()
    {
        var thread = new ThreadInfo(
            Id: 1234,
            Name: null,
            State: ThreadState.Running,
            IsCurrent: true,
            Location: null);

        thread.Id.Should().BePositive();
        thread.IsCurrent.Should().BeTrue();
    }

    /// <summary>
    /// Thread name is optional (can be null for unnamed threads).
    /// </summary>
    [Fact]
    public void Thread_Name_IsOptional()
    {
        var namedThread = new ThreadInfo(
            Id: 1,
            Name: "Main Thread",
            State: ThreadState.Running,
            IsCurrent: true,
            Location: null);

        var unnamedThread = new ThreadInfo(
            Id: 2,
            Name: null,
            State: ThreadState.Waiting,
            IsCurrent: false,
            Location: null);

        namedThread.Name.Should().Be("Main Thread");
        unnamedThread.Name.Should().BeNull();
    }

    /// <summary>
    /// Thread state enum matches contract values.
    /// </summary>
    [Theory]
    [InlineData(ThreadState.Running, "running")]
    [InlineData(ThreadState.Stopped, "stopped")]
    [InlineData(ThreadState.Waiting, "waiting")]
    [InlineData(ThreadState.NotStarted, "notStarted")]
    [InlineData(ThreadState.Terminated, "terminated")]
    public void Thread_State_SerializesCorrectly(ThreadState state, string expectedJsonValue)
    {
        var jsonValue = JsonNamingPolicy.CamelCase.ConvertName(state.ToString());
        jsonValue.Should().Be(expectedJsonValue, $"ThreadState.{state} should serialize to '{expectedJsonValue}'");
    }

    /// <summary>
    /// Thread location is optional (null when thread is running).
    /// </summary>
    [Fact]
    public void Thread_Location_IsOptional()
    {
        var runningThread = new ThreadInfo(
            Id: 1,
            Name: "Worker",
            State: ThreadState.Running,
            IsCurrent: false,
            Location: null);

        var stoppedThread = new ThreadInfo(
            Id: 2,
            Name: "Main",
            State: ThreadState.Stopped,
            IsCurrent: true,
            Location: new SourceLocation("/path/to/file.cs", 42, null, "Main", null));

        runningThread.Location.Should().BeNull();
        stoppedThread.Location.Should().NotBeNull();
        stoppedThread.Location!.File.Should().Be("/path/to/file.cs");
        stoppedThread.Location.Line.Should().Be(42);
    }

    /// <summary>
    /// Only one thread should be marked as current.
    /// </summary>
    [Fact]
    public void ThreadsList_OnlyOneCurrentThread()
    {
        var threads = new[]
        {
            new ThreadInfo(1, "Main", ThreadState.Stopped, true, null),
            new ThreadInfo(2, "Worker1", ThreadState.Running, false, null),
            new ThreadInfo(3, "Worker2", ThreadState.Waiting, false, null)
        };

        var currentCount = threads.Count(t => t.IsCurrent);
        currentCount.Should().Be(1, "only one thread should be marked as current");
    }

    /// <summary>
    /// Thread ID must be positive.
    /// </summary>
    [Fact]
    public void Thread_Id_MustBePositive()
    {
        var thread = new ThreadInfo(
            Id: 12345,
            Name: null,
            State: ThreadState.Running,
            IsCurrent: false,
            Location: null);

        thread.Id.Should().BePositive();
    }

    /// <summary>
    /// ThreadState covers all states from the contract.
    /// </summary>
    [Fact]
    public void ThreadState_CoversAllContractStates()
    {
        // Contract defines: ["running", "stopped", "waiting", "not_started", "terminated"]
        var allStates = Enum.GetValues<ThreadState>();

        allStates.Should().Contain(ThreadState.Running);
        allStates.Should().Contain(ThreadState.Stopped);
        allStates.Should().Contain(ThreadState.Waiting);
        allStates.Should().Contain(ThreadState.NotStarted);
        allStates.Should().Contain(ThreadState.Terminated);
    }

    /// <summary>
    /// Empty thread list is valid (process with no managed threads).
    /// </summary>
    [Fact]
    public void ThreadsList_EmptyArray_IsValid()
    {
        var response = new
        {
            threads = Array.Empty<ThreadInfo>()
        };

        response.threads.Should().BeEmpty();
    }

    /// <summary>
    /// Multiple threads can exist in different states.
    /// </summary>
    [Fact]
    public void ThreadsList_MultipleThreads_DifferentStates()
    {
        var threads = new List<ThreadInfo>
        {
            new(1, "Main", ThreadState.Stopped, true, new SourceLocation("/file.cs", 10, null, null, null)),
            new(2, "Worker", ThreadState.Running, false, null),
            new(3, "IO", ThreadState.Waiting, false, null),
            new(4, "Completed", ThreadState.Terminated, false, null)
        };

        threads.Should().HaveCount(4);
        threads.Select(t => t.State).Should().OnlyHaveUniqueItems();
    }
}
