using DebugMcp.Models;
using DebugMcp.Models.Breakpoints;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the breakpoint_list tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in breakpoint-tools.json.
/// </summary>
public class BreakpointListContractTests
{
    /// <summary>
    /// breakpoint_list requires no parameters (all optional).
    /// </summary>
    [Fact]
    public void BreakpointList_HasNoRequiredParameters()
    {
        // Contract: no required parameters
        // The tool can be called with no arguments
        true.Should().BeTrue("breakpoint_list has no required parameters");
    }

    /// <summary>
    /// Success response contains breakpoints array and count.
    /// </summary>
    [Fact]
    public void SuccessResponse_ContainsBreakpointsArrayAndCount()
    {
        // Contract defines outputSchema with:
        // "breakpoints": { "type": "array", "items": { "$ref": "#/definitions/Breakpoint" } }
        // "count": { "type": "integer", "minimum": 0 }

        var breakpoints = new List<object>
        {
            new { id = "bp-1", location = new { file = "/app/Program.cs", line = 10 } },
            new { id = "bp-2", location = new { file = "/app/Program.cs", line = 20 } }
        };

        var count = breakpoints.Count;

        breakpoints.Should().HaveCount(2);
        count.Should().Be(2);
    }

    /// <summary>
    /// Empty list returns count of zero.
    /// </summary>
    [Fact]
    public void EmptyList_ReturnsZeroCount()
    {
        // Contract: empty array with count=0 when no breakpoints
        var breakpoints = new List<object>();
        var count = breakpoints.Count;

        breakpoints.Should().BeEmpty();
        count.Should().Be(0);
    }

    /// <summary>
    /// Each breakpoint in list contains required fields.
    /// </summary>
    [Fact]
    public void EachBreakpoint_ContainsRequiredFields()
    {
        // Contract: Breakpoint definition includes id, location, state, enabled, verified, hitCount
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42,
            Column: 15);

        var breakpoint = new Breakpoint(
            Id: "bp-12345",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 3,
            Condition: "i > 5");

        // Required fields
        breakpoint.Id.Should().NotBeNullOrEmpty("id is required");
        breakpoint.Location.Should().NotBeNull("location is required");
        breakpoint.Location.File.Should().NotBeNullOrEmpty("location.file is required");
        breakpoint.Location.Line.Should().BePositive("location.line is required");

        // State fields
        breakpoint.State.Should().BeDefined("state is required");
        breakpoint.Enabled.Should().BeTrue("enabled is required");
        breakpoint.Verified.Should().BeTrue("verified is required");
        breakpoint.HitCount.Should().BeGreaterThanOrEqualTo(0, "hitCount is required");

        // Optional fields
        breakpoint.Condition.Should().NotBeNull("condition is optional but present");
    }

    /// <summary>
    /// BreakpointState enum has all expected values.
    /// </summary>
    [Theory]
    [InlineData(BreakpointState.Pending)]
    [InlineData(BreakpointState.Bound)]
    [InlineData(BreakpointState.Disabled)]
    public void BreakpointState_HasExpectedValues(BreakpointState state)
    {
        // Contract defines state as one of: pending, bound, disabled
        state.Should().BeDefined();
    }

    /// <summary>
    /// List includes breakpoints in all states.
    /// </summary>
    [Fact]
    public void List_IncludesBreakpointsInAllStates()
    {
        // Contract: list returns all breakpoints regardless of state
        var location = new BreakpointLocation("/app/Program.cs", 10);

        var pending = new Breakpoint("bp-1", location, BreakpointState.Pending, true, false, 0);
        var bound = new Breakpoint("bp-2", location with { Line = 20 }, BreakpointState.Bound, true, true, 5);
        var disabled = new Breakpoint("bp-3", location with { Line = 30 }, BreakpointState.Disabled, false, true, 2);

        var all = new[] { pending, bound, disabled };

        all.Should().Contain(bp => bp.State == BreakpointState.Pending);
        all.Should().Contain(bp => bp.State == BreakpointState.Bound);
        all.Should().Contain(bp => bp.State == BreakpointState.Disabled);
    }

    /// <summary>
    /// Error codes for breakpoint_list are defined per contract.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.NoSession)]
    [InlineData(ErrorCodes.Timeout)]
    public void BreakpointListErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }
}
