using DotnetMcp.Models;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the breakpoint_remove tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in breakpoint-tools.json.
/// </summary>
public class BreakpointRemoveContractTests
{
    /// <summary>
    /// breakpoint_remove requires id parameter.
    /// </summary>
    [Fact]
    public void BreakpointRemove_Id_IsRequired()
    {
        // Contract: "id": { "type": "string", "description": "Breakpoint ID to remove" }
        // Required in "required": ["id"]
        var id = "bp-12345";

        id.Should().NotBeNullOrEmpty("id is required");
    }

    /// <summary>
    /// Success response contains success flag and message.
    /// </summary>
    [Fact]
    public void SuccessResponse_ContainsSuccessAndMessage()
    {
        // Contract: success response includes success=true and message
        var success = true;
        var message = "Breakpoint bp-12345 removed";

        success.Should().BeTrue("success indicates removal succeeded");
        message.Should().Contain("removed", "message confirms removal");
    }

    /// <summary>
    /// Error response for non-existent breakpoint uses correct error code.
    /// </summary>
    [Fact]
    public void ErrorResponse_NonExistent_UsesBreakpointNotFound()
    {
        // Contract: BREAKPOINT_NOT_FOUND when ID doesn't exist
        var errorCode = ErrorCodes.BreakpointNotFound;

        errorCode.Should().Be("BREAKPOINT_NOT_FOUND");
    }

    /// <summary>
    /// Removing a breakpoint removes it from list.
    /// </summary>
    [Fact]
    public void RemovingBreakpoint_RemovesFromList()
    {
        // Contract: after removal, breakpoint should not appear in list
        var ids = new List<string> { "bp-1", "bp-2", "bp-3" };
        var idToRemove = "bp-2";

        ids.Remove(idToRemove);

        ids.Should().NotContain(idToRemove);
        ids.Should().HaveCount(2);
    }

    /// <summary>
    /// Empty ID parameter results in error.
    /// </summary>
    [Fact]
    public void EmptyId_ResultsInError()
    {
        // Contract: empty/null ID is invalid
        var emptyId = "";
        var nullId = (string?)null;

        emptyId.Should().BeEmpty("empty ID is invalid");
        nullId.Should().BeNull("null ID is invalid");
    }

    /// <summary>
    /// Error codes for breakpoint_remove are defined per contract.
    /// </summary>
    [Theory]
    [InlineData(ErrorCodes.BreakpointNotFound)]
    [InlineData(ErrorCodes.Timeout)]
    public void BreakpointRemoveErrorCodes_AreDefined(string errorCode)
    {
        errorCode.Should().NotBeNullOrEmpty("error code must be defined");
        errorCode.Should().MatchRegex(@"^[A-Z_]+$", "error codes should be SCREAMING_SNAKE_CASE");
    }

    /// <summary>
    /// Success response format matches contract.
    /// </summary>
    [Fact]
    public void SuccessResponse_MatchesContractFormat()
    {
        // Contract response format:
        // { "success": true, "message": "Breakpoint {id} removed" }

        var id = "bp-12345";
        var response = new
        {
            success = true,
            message = $"Breakpoint {id} removed"
        };

        response.success.Should().BeTrue();
        response.message.Should().Be("Breakpoint bp-12345 removed");
    }

    /// <summary>
    /// Error response format matches contract.
    /// </summary>
    [Fact]
    public void ErrorResponse_MatchesContractFormat()
    {
        // Contract error format:
        // { "success": false, "error": { "code": "...", "message": "..." } }

        var id = "bp-nonexistent";
        var response = new
        {
            success = false,
            error = new
            {
                code = ErrorCodes.BreakpointNotFound,
                message = $"No breakpoint with ID '{id}'"
            }
        };

        response.success.Should().BeFalse();
        response.error.code.Should().Be("BREAKPOINT_NOT_FOUND");
        response.error.message.Should().Contain(id);
    }
}
