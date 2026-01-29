using DebugMcp.Models.Breakpoints;
using FluentAssertions;

namespace DebugMcp.Tests.Unit;

/// <summary>
/// Unit tests for the Breakpoint model.
/// </summary>
public class BreakpointTests
{
    /// <summary>
    /// Breakpoint model stores condition expression.
    /// </summary>
    [Fact]
    public void Breakpoint_StoresCondition()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        // Act
        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 0,
            Condition: "i > 5",
            Message: null);

        // Assert
        breakpoint.Condition.Should().Be("i > 5");
    }

    /// <summary>
    /// Breakpoint condition can be null (unconditional breakpoint).
    /// </summary>
    [Fact]
    public void Breakpoint_ConditionCanBeNull()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        // Act
        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // Assert
        breakpoint.Condition.Should().BeNull();
    }

    /// <summary>
    /// Breakpoint condition can be updated via 'with' expression.
    /// </summary>
    [Fact]
    public void Breakpoint_ConditionCanBeUpdated()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 0,
            Condition: "i > 5");

        // Act
        var updated = breakpoint with { Condition = "i > 10" };

        // Assert
        breakpoint.Condition.Should().Be("i > 5", "original should be unchanged");
        updated.Condition.Should().Be("i > 10", "updated copy should have new condition");
    }

    /// <summary>
    /// Breakpoint HitCount starts at zero.
    /// </summary>
    [Fact]
    public void Breakpoint_HitCount_StartsAtZero()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        // Act
        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // Assert
        breakpoint.HitCount.Should().Be(0);
    }

    /// <summary>
    /// Breakpoint HitCount can be incremented via 'with' expression.
    /// </summary>
    [Fact]
    public void Breakpoint_HitCount_CanBeIncremented()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 5);

        // Act
        var updated = breakpoint with { HitCount = breakpoint.HitCount + 1 };

        // Assert
        updated.HitCount.Should().Be(6);
    }

    /// <summary>
    /// Breakpoint state can transition from Pending to Bound.
    /// </summary>
    [Fact]
    public void Breakpoint_State_CanTransitionPendingToBound()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Pending,
            Enabled: true,
            Verified: false,
            HitCount: 0);

        // Act
        var updated = breakpoint with
        {
            State = BreakpointState.Bound,
            Verified = true
        };

        // Assert
        updated.State.Should().Be(BreakpointState.Bound);
        updated.Verified.Should().BeTrue();
    }

    /// <summary>
    /// Breakpoint state can transition to Disabled.
    /// </summary>
    [Fact]
    public void Breakpoint_State_CanTransitionToDisabled()
    {
        // Arrange
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        var breakpoint = new Breakpoint(
            Id: "bp-123",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 3);

        // Act
        var updated = breakpoint with
        {
            Enabled = false,
            State = BreakpointState.Disabled
        };

        // Assert
        updated.State.Should().Be(BreakpointState.Disabled);
        updated.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Breakpoint location includes all properties.
    /// </summary>
    [Fact]
    public void BreakpointLocation_IncludesAllProperties()
    {
        // Arrange & Act
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42,
            Column: 15,
            EndLine: 42,
            EndColumn: 25,
            FunctionName: "Main",
            ModuleName: "MyApp.dll");

        // Assert
        location.File.Should().Be("/app/Program.cs");
        location.Line.Should().Be(42);
        location.Column.Should().Be(15);
        location.EndLine.Should().Be(42);
        location.EndColumn.Should().Be(25);
        location.FunctionName.Should().Be("Main");
        location.ModuleName.Should().Be("MyApp.dll");
    }
}
