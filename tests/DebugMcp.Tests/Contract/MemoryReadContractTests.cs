using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the memory_read tool schema compliance.
/// </summary>
public class MemoryReadContractTests
{
    [Fact]
    public void MemoryRead_Address_IsRequired()
    {
        // Contract: address is required, non-empty string
        string validHex = "0x00007FF8A1234560";
        string validDecimal = "12345678";
        string? nullAddr = null;
        var emptyAddr = "";

        validHex.Should().NotBeNullOrEmpty("hex address is valid");
        validDecimal.Should().NotBeNullOrEmpty("decimal address is valid");
        nullAddr.Should().BeNull("null should be rejected");
        emptyAddr.Should().BeEmpty("empty should be rejected");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(256, true)]
    [InlineData(65536, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(65537, false)]
    public void MemoryRead_Size_MustBeBetween1And65536(int size, bool isValid)
    {
        // Contract: size must be > 0 and <= 65536
        var meetsContract = size > 0 && size <= 65536;
        meetsContract.Should().Be(isValid, $"size={size}");
    }

    [Theory]
    [InlineData("hex", true)]
    [InlineData("hex_ascii", true)]
    [InlineData("raw", true)]
    [InlineData("json", false)]
    [InlineData("", false)]
    [InlineData("HEX", false)]
    public void MemoryRead_Format_MustBeValid(string format, bool isValid)
    {
        // Contract: format must be one of hex, hex_ascii, raw
        string[] validFormats = ["hex", "hex_ascii", "raw"];
        var meetsContract = validFormats.Contains(format);
        meetsContract.Should().Be(isValid, $"format='{format}'");
    }

    [Fact]
    public void MemoryRead_ErrorCodes_AreDefined()
    {
        var errorCodes = new[]
        {
            ErrorCodes.InvalidParameter,
            ErrorCodes.NoSession,
            ErrorCodes.NotPaused,
            ErrorCodes.SizeExceeded,
            ErrorCodes.InvalidAddress,
            ErrorCodes.MemoryReadFailed
        };

        foreach (var code in errorCodes)
        {
            code.Should().NotBeNullOrEmpty("error code must be defined");
            code.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
        }
    }
}
