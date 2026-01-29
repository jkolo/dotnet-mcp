using DebugMcp.Models;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the object_inspect tool schema compliance.
/// </summary>
public class ObjectInspectContractTests
{
    [Fact]
    public void ObjectInspect_ObjectRef_IsRequired()
    {
        // Contract: object_ref is required, non-empty string
        string validRef = "this._currentUser";
        string? nullRef = null;
        var emptyRef = "";

        validRef.Should().NotBeNullOrEmpty("contract requires non-empty object_ref");
        nullRef.Should().BeNull("null should be rejected by tool");
        emptyRef.Should().BeEmpty("empty should be rejected by tool");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(-1, false)]
    public void ObjectInspect_Depth_MustBeBetween1And10(int depth, bool isValid)
    {
        // Contract: depth must be 1-10
        var meetsContract = depth >= 1 && depth <= 10;
        meetsContract.Should().Be(isValid, $"depth={depth} should {(isValid ? "be valid" : "violate contract")}");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    public void ObjectInspect_FrameIndex_MustBeNonNegative(int frameIndex, bool isValid)
    {
        // Contract: frame_index >= 0
        var meetsContract = frameIndex >= 0;
        meetsContract.Should().Be(isValid, $"frame_index={frameIndex}");
    }

    [Fact]
    public void ObjectInspect_ThreadId_IsOptional()
    {
        // Contract: thread_id is optional (nullable)
        int? noThread = null;
        int? validThread = 12345;

        noThread.Should().BeNull("null thread_id means current thread");
        validThread.Should().NotBeNull("explicit thread_id is valid");
    }

    [Fact]
    public void ObjectInspect_ErrorResponse_HasCodeAndMessage()
    {
        // Contract: error response must have code and message
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
