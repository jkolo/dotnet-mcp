using DebugMcp.Models.Breakpoints;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.Tests.Unit;

/// <summary>
/// Unit tests for exception breakpoint registration and management.
/// </summary>
public class ExceptionBreakpointTests
{
    private readonly BreakpointRegistry _registry;

    public ExceptionBreakpointTests()
    {
        var logger = new Mock<ILogger<BreakpointRegistry>>();
        _registry = new BreakpointRegistry(logger.Object);
    }

    /// <summary>
    /// ExceptionBreakpoint model stores all properties correctly.
    /// </summary>
    [Fact]
    public void ExceptionBreakpoint_StoresAllProperties()
    {
        // Arrange & Act
        var ebp = new ExceptionBreakpoint(
            Id: "ebp-12345",
            ExceptionType: "System.NullReferenceException",
            BreakOnFirstChance: true,
            BreakOnSecondChance: false,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 5);

        // Assert
        ebp.Id.Should().Be("ebp-12345");
        ebp.ExceptionType.Should().Be("System.NullReferenceException");
        ebp.BreakOnFirstChance.Should().BeTrue();
        ebp.BreakOnSecondChance.Should().BeFalse();
        ebp.IncludeSubtypes.Should().BeTrue();
        ebp.Enabled.Should().BeTrue();
        ebp.Verified.Should().BeTrue();
        ebp.HitCount.Should().Be(5);
    }

    /// <summary>
    /// Registry can add and retrieve exception breakpoints.
    /// </summary>
    [Fact]
    public void Registry_AddException_CanRetrieve()
    {
        // Arrange
        var ebp = new ExceptionBreakpoint(
            Id: "ebp-test",
            ExceptionType: "System.Exception",
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // Act
        _registry.AddException(ebp);

        // Assert
        _registry.ExceptionCount.Should().Be(1);
        var retrieved = _registry.GetException("ebp-test");
        retrieved.Should().NotBeNull();
        retrieved!.ExceptionType.Should().Be("System.Exception");
    }

    /// <summary>
    /// Registry can remove exception breakpoints.
    /// </summary>
    [Fact]
    public void Registry_RemoveException_RemovesBreakpoint()
    {
        // Arrange
        var ebp = new ExceptionBreakpoint(
            Id: "ebp-test",
            ExceptionType: "System.Exception",
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        _registry.AddException(ebp);

        // Act
        var removed = _registry.RemoveException("ebp-test");

        // Assert
        removed.Should().NotBeNull();
        _registry.ExceptionCount.Should().Be(0);
        _registry.GetException("ebp-test").Should().BeNull();
    }

    /// <summary>
    /// Registry can get all exception breakpoints.
    /// </summary>
    [Fact]
    public void Registry_GetAllExceptions_ReturnsAll()
    {
        // Arrange
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-1", "System.Exception", true, true, true, true, true, 0));
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-2", "System.NullReferenceException", true, false, true, true, true, 0));
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-3", "System.ArgumentException", false, true, false, true, true, 0));

        // Act
        var all = _registry.GetAllExceptions();

        // Assert
        all.Should().HaveCount(3);
        all.Should().Contain(e => e.ExceptionType == "System.Exception");
        all.Should().Contain(e => e.ExceptionType == "System.NullReferenceException");
        all.Should().Contain(e => e.ExceptionType == "System.ArgumentException");
    }

    /// <summary>
    /// Registry can update exception breakpoint.
    /// </summary>
    [Fact]
    public void Registry_UpdateException_UpdatesBreakpoint()
    {
        // Arrange
        var ebp = new ExceptionBreakpoint(
            Id: "ebp-test",
            ExceptionType: "System.Exception",
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        _registry.AddException(ebp);

        // Act
        var updated = ebp with { HitCount = 5, Enabled = false };
        _registry.UpdateException(updated);

        // Assert
        var retrieved = _registry.GetException("ebp-test");
        retrieved.Should().NotBeNull();
        retrieved!.HitCount.Should().Be(5);
        retrieved.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Registry FindExceptionByType finds matching exception breakpoints.
    /// </summary>
    [Fact]
    public void Registry_FindExceptionByType_FindsMatching()
    {
        // Arrange
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-1", "System.Exception", true, true, true, true, true, 0));
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-2", "System.NullReferenceException", true, false, true, true, true, 0));

        // Act
        var found = _registry.FindExceptionByType("System.NullReferenceException");

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be("ebp-2");
    }

    /// <summary>
    /// Registry FindExceptionByType returns null for non-matching type.
    /// </summary>
    [Fact]
    public void Registry_FindExceptionByType_ReturnsNullForNonMatching()
    {
        // Arrange
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-1", "System.Exception", true, true, true, true, true, 0));

        // Act
        var found = _registry.FindExceptionByType("System.IO.IOException");

        // Assert
        found.Should().BeNull();
    }

    /// <summary>
    /// ExceptionBreakpoint can be updated via 'with' expression.
    /// </summary>
    [Fact]
    public void ExceptionBreakpoint_CanBeUpdatedViaWith()
    {
        // Arrange
        var original = new ExceptionBreakpoint(
            Id: "ebp-test",
            ExceptionType: "System.Exception",
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0);

        // Act
        var updated = original with { HitCount = 10, Enabled = false };

        // Assert
        original.HitCount.Should().Be(0, "original should be unchanged");
        original.Enabled.Should().BeTrue("original should be unchanged");
        updated.HitCount.Should().Be(10);
        updated.Enabled.Should().BeFalse();
        updated.Id.Should().Be(original.Id, "ID should be preserved");
        updated.ExceptionType.Should().Be(original.ExceptionType, "type should be preserved");
    }

    /// <summary>
    /// Clear removes all exception breakpoints.
    /// </summary>
    [Fact]
    public void Registry_Clear_RemovesAllExceptionBreakpoints()
    {
        // Arrange
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-1", "System.Exception", true, true, true, true, true, 0));
        _registry.AddException(new ExceptionBreakpoint(
            "ebp-2", "System.NullReferenceException", true, false, true, true, true, 0));

        // Act
        _registry.Clear();

        // Assert
        _registry.ExceptionCount.Should().Be(0);
    }
}
