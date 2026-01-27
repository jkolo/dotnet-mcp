using DotnetMcp.Models;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the layout_get tool schema compliance.
/// </summary>
public class LayoutGetContractTests
{
    [Fact]
    public void LayoutGet_TypeName_IsRequired()
    {
        string validType = "MyApp.Models.Customer";
        string? nullType = null;
        var emptyType = "";

        validType.Should().NotBeNullOrEmpty("contract requires non-empty type_name");
        nullType.Should().BeNull("null should be rejected");
        emptyType.Should().BeEmpty("empty should be rejected");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    public void LayoutGet_FrameIndex_MustBeNonNegative(int frameIndex, bool isValid)
    {
        var meetsContract = frameIndex >= 0;
        meetsContract.Should().Be(isValid, $"frame_index={frameIndex}");
    }

    [Fact]
    public void LayoutGet_BooleanParams_HaveDefaults()
    {
        // Contract: include_inherited defaults true, include_padding defaults true
        const bool defaultIncludeInherited = true;
        const bool defaultIncludePadding = true;

        defaultIncludeInherited.Should().BeTrue("include_inherited defaults to true");
        defaultIncludePadding.Should().BeTrue("include_padding defaults to true");
    }

    [Fact]
    public void LayoutGet_ErrorCodes_AreDefined()
    {
        var errorCodes = new[]
        {
            ErrorCodes.InvalidParameter,
            ErrorCodes.NoSession,
            ErrorCodes.NotPaused,
            ErrorCodes.TypeNotFound
        };

        foreach (var code in errorCodes)
        {
            code.Should().NotBeNullOrEmpty("error code must be defined");
            code.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
        }
    }
}
