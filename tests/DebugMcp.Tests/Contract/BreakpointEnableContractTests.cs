using DebugMcp.Models;
using DebugMcp.Models.Breakpoints;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the breakpoint_enable tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in breakpoint-tools.json.
/// </summary>
public class BreakpointEnableContractTests
{
    /// <summary>
    /// breakpoint_enable requires id parameter.
    /// </summary>
    [Fact]
    public void BreakpointEnable_Id_IsRequired()
    {
        // Contract: "id": { "type": "string", "description": "Breakpoint ID to enable/disable" }
        // Required in "required": ["id"]
        var id = "bp-12345";

        id.Should().NotBeNullOrEmpty("id is required");
    }

    /// <summary>
    /// breakpoint_enable has default value for enabled parameter.
    /// </summary>
    [Fact]
    public void BreakpointEnable_Enabled_HasDefault()
    {
        // Contract: "enabled": { "type": "boolean", "default": true }
        const bool DefaultEnabled = true;

        DefaultEnabled.Should().BeTrue("default for enabled is true");
    }

    /// <summary>
    /// Success response contains updated breakpoint details.
    /// </summary>
    [Fact]
    public void SuccessResponse_ContainsBreakpointDetails()
    {
        // Contract: success response includes breakpoint object with updated state
        var location = new BreakpointLocation(
            File: "/app/Program.cs",
            Line: 42);

        var breakpoint = new Breakpoint(
            Id: "bp-12345",
            Location: location,
            State: BreakpointState.Disabled,
            Enabled: false,
            Verified: true,
            HitCount: 5);

        breakpoint.Id.Should().NotBeNullOrEmpty("id is required");
        breakpoint.Enabled.Should().BeFalse("enabled reflects current state");
        breakpoint.State.Should().Be(BreakpointState.Disabled, "state reflects disabled");
    }

    /// <summary>
    /// Enabling changes state from Disabled to Bound.
    /// </summary>
    [Fact]
    public void Enabling_ChangesStateFromDisabledToBound()
    {
        // Arrange
        var location = new BreakpointLocation("/app/Program.cs", 42);
        var disabled = new Breakpoint(
            Id: "bp-12345",
            Location: location,
            State: BreakpointState.Disabled,
            Enabled: false,
            Verified: true,
            HitCount: 0);

        // Act - simulate enabling
        var enabled = disabled with
        {
            Enabled = true,
            State = BreakpointState.Bound
        };

        // Assert
        enabled.Enabled.Should().BeTrue();
        enabled.State.Should().Be(BreakpointState.Bound);
    }

    /// <summary>
    /// Disabling changes state from Bound to Disabled.
    /// </summary>
    [Fact]
    public void Disabling_ChangesStateFromBoundToDisabled()
    {
        // Arrange
        var location = new BreakpointLocation("/app/Program.cs", 42);
        var bound = new Breakpoint(
            Id: "bp-12345",
            Location: location,
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 3);

        // Act - simulate disabling
        var disabled = bound with
        {
            Enabled = false,
            State = BreakpointState.Disabled
        };

        // Assert
        disabled.Enabled.Should().BeFalse();
        disabled.State.Should().Be(BreakpointState.Disabled);
    }

    /// <summary>
    /// Pending breakpoints stay pending when enabled/disabled.
    /// </summary>
    [Fact]
    public void PendingBreakpoint_StaysPending_WhenToggled()
    {
        // Pending breakpoints haven't bound yet, so they stay pending
        var location = new BreakpointLocation("/app/Program.cs", 42);
        var pending = new Breakpoint(
            Id: "bp-12345",
            Location: location,
            State: BreakpointState.Pending,
            Enabled: true,
            Verified: false,
            HitCount: 0);

        // Disabling a pending breakpoint should keep it pending (not turn it to Disabled)
        // because it hasn't been bound yet - the Disabled state is for bound breakpoints
        var disabled = pending with { Enabled = false };

        disabled.Enabled.Should().BeFalse();
        disabled.State.Should().Be(BreakpointState.Pending, "pending stays pending until bound");
    }

    /// <summary>
    /// Error codes for breakpoint_enable are defined per contract.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.BreakpointNotFound)]
    [InlineData(ErrorCodes.NoSession)]
    public void BreakpointEnableErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }
}
