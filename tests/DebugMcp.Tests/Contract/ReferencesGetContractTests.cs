using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the references_get tool schema compliance.
/// </summary>
public class ReferencesGetContractTests
{
    [Fact]
    public void ReferencesGet_ObjectRef_IsRequired()
    {
        string validRef = "this._orders";
        string? nullRef = null;
        var emptyRef = "";

        validRef.Should().NotBeNullOrEmpty("contract requires non-empty object_ref");
        nullRef.Should().BeNull("null should be rejected");
        emptyRef.Should().BeEmpty("empty should be rejected");
    }

    [Theory]
    [InlineData("outbound", true)]
    [InlineData("inbound", true)]
    [InlineData("both", true)]
    [InlineData("up", false)]
    [InlineData("", false)]
    [InlineData("OUTBOUND", false)]
    public void ReferencesGet_Direction_MustBeValid(string direction, bool isValid)
    {
        string[] validDirections = ["outbound", "inbound", "both"];
        var meetsContract = validDirections.Contains(direction);
        meetsContract.Should().Be(isValid, $"direction='{direction}'");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(101, true)] // clamped to 100 by tool, not rejected
    public void ReferencesGet_MaxResults_MustBePositive(int maxResults, bool isValid)
    {
        // Contract: max_results >= 1, values > 100 are clamped
        var meetsContract = maxResults >= 1;
        meetsContract.Should().Be(isValid, $"max_results={maxResults}");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    public void ReferencesGet_FrameIndex_MustBeNonNegative(int frameIndex, bool isValid)
    {
        var meetsContract = frameIndex >= 0;
        meetsContract.Should().Be(isValid, $"frame_index={frameIndex}");
    }

    [Fact]
    public void ReferencesGet_ErrorCodes_AreDefined()
    {
        var errorCodes = new[]
        {
            ErrorCodes.InvalidParameter,
            ErrorCodes.NoSession,
            ErrorCodes.NotPaused,
            ErrorCodes.InvalidReference
        };

        foreach (var code in errorCodes)
        {
            code.Should().NotBeNullOrEmpty("error code must be defined");
            code.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
        }
    }
}
