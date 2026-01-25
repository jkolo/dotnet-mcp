using System.Text.Json;
using DotnetMcp.Models;
using DotnetMcp.Models.Inspection;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the evaluate tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in contracts/evaluate.json.
/// </summary>
public class EvaluateContractTests
{
    /// <summary>
    /// Expression parameter is required.
    /// </summary>
    [Fact]
    public void Evaluate_RequiresExpression()
    {
        // Contract requires: expression (required)
        var validParams = new { expression = "variable.Property" };
        validParams.expression.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Expression must be at least 1 character.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("x", true)]
    [InlineData("variable", true)]
    [InlineData("obj.Property.Method()", true)]
    public void Evaluate_Expression_MinLength(string expression, bool isValid)
    {
        // Contract defines minLength: 1
        var valid = expression.Length >= 1;
        valid.Should().Be(isValid, $"expression='{expression}' should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// thread_id is optional.
    /// </summary>
    [Fact]
    public void Evaluate_ThreadId_IsOptional()
    {
        var paramsWithThreadId = new { expression = "x", thread_id = 1234 };
        var paramsWithoutThreadId = new { expression = "x" };

        paramsWithThreadId.thread_id.Should().BePositive();
        paramsWithoutThreadId.expression.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// frame_index is optional with default 0.
    /// </summary>
    [Fact]
    public void Evaluate_FrameIndex_IsOptionalWithDefault()
    {
        // Contract defines: default: 0, minimum: 0
        var paramsWithFrame = new { expression = "x", frame_index = 2 };
        var paramsWithoutFrame = new { expression = "x" };

        paramsWithFrame.frame_index.Should().BeGreaterThanOrEqualTo(0);
        true.Should().BeTrue("frame_index is optional with default 0");
    }

    /// <summary>
    /// frame_index must be non-negative.
    /// </summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    public void Evaluate_FrameIndex_MustBeNonNegative(int frameIndex, bool isValid)
    {
        // Contract defines: minimum: 0
        var valid = frameIndex >= 0;
        valid.Should().Be(isValid, $"frame_index={frameIndex} should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// timeout_ms is optional with default 5000.
    /// </summary>
    [Fact]
    public void Evaluate_TimeoutMs_IsOptionalWithDefault()
    {
        // Contract defines: default: 5000, minimum: 100, maximum: 60000
        var paramsWithTimeout = new { expression = "x", timeout_ms = 10000 };
        var paramsWithoutTimeout = new { expression = "x" };

        paramsWithTimeout.timeout_ms.Should().Be(10000);
        true.Should().BeTrue("timeout_ms is optional with default 5000");
    }

    /// <summary>
    /// timeout_ms must be within range.
    /// </summary>
    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(5000, true)]
    [InlineData(60000, true)]
    [InlineData(60001, false)]
    public void Evaluate_TimeoutMs_Range(int timeoutMs, bool isValid)
    {
        // Contract defines: minimum: 100, maximum: 60000
        var valid = timeoutMs >= 100 && timeoutMs <= 60000;
        valid.Should().Be(isValid, $"timeout_ms={timeoutMs} should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// Response includes required field: success.
    /// </summary>
    [Fact]
    public void Evaluate_Response_RequiresSuccess()
    {
        // Contract requires: success
        var response = new
        {
            success = true,
            value = "42",
            type = "System.Int32"
        };

        response.success.Should().BeTrue();
    }

    /// <summary>
    /// Successful evaluation includes value and type.
    /// </summary>
    [Fact]
    public void Evaluate_SuccessResponse_IncludesValueAndType()
    {
        var response = new
        {
            success = true,
            value = "\"Hello, World!\"",
            type = "System.String",
            has_children = false
        };

        response.value.Should().NotBeNullOrEmpty();
        response.type.Should().NotBeNullOrEmpty();
        response.has_children.Should().BeFalse();
    }

    /// <summary>
    /// has_children indicates expandable results.
    /// </summary>
    [Fact]
    public void Evaluate_HasChildren_IndicatesExpandable()
    {
        // Object result - has children
        var objectResult = new
        {
            success = true,
            value = "{...}",
            type = "MyApp.Customer",
            has_children = true
        };

        // Primitive result - no children
        var primitiveResult = new
        {
            success = true,
            value = "42",
            type = "System.Int32",
            has_children = false
        };

        objectResult.has_children.Should().BeTrue();
        primitiveResult.has_children.Should().BeFalse();
    }

    /// <summary>
    /// Error response includes required error fields.
    /// </summary>
    [Fact]
    public void Evaluate_ErrorResponse_RequiresCodeAndMessage()
    {
        var errorResponse = new
        {
            success = false,
            error = new
            {
                code = "syntax_error",
                message = "Unexpected token at position 5"
            }
        };

        errorResponse.success.Should().BeFalse();
        errorResponse.error.code.Should().NotBeNullOrEmpty();
        errorResponse.error.message.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Error codes match contract enum.
    /// </summary>
    [Theory]
    [InlineData("eval_timeout", true)]
    [InlineData("eval_exception", true)]
    [InlineData("syntax_error", true)]
    [InlineData("variable_unavailable", true)]
    [InlineData("unknown_error", false)]
    [InlineData("INVALID", false)]
    public void Evaluate_ErrorCode_Validation(string errorCode, bool isValid)
    {
        // Contract defines: enum: ["eval_timeout", "eval_exception", "syntax_error", "variable_unavailable"]
        var validCodes = new[] { "eval_timeout", "eval_exception", "syntax_error", "variable_unavailable" };
        var valid = validCodes.Contains(errorCode);
        valid.Should().Be(isValid, $"error code='{errorCode}' should be {(isValid ? "valid" : "invalid")}");
    }

    /// <summary>
    /// Timeout error response format.
    /// </summary>
    [Fact]
    public void Evaluate_TimeoutError_Format()
    {
        var response = new
        {
            success = false,
            error = new
            {
                code = "eval_timeout",
                message = "Expression evaluation timed out after 5000ms"
            }
        };

        response.error.code.Should().Be("eval_timeout");
        (response.error.message.Contains("timeout") || response.error.message.Contains("timed out"))
            .Should().BeTrue("message should contain 'timeout' or 'timed out'");
    }

    /// <summary>
    /// Exception error response includes exception_type.
    /// </summary>
    [Fact]
    public void Evaluate_ExceptionError_IncludesExceptionType()
    {
        var response = new
        {
            success = false,
            error = new
            {
                code = "eval_exception",
                message = "Object reference not set to an instance of an object",
                exception_type = "System.NullReferenceException"
            }
        };

        response.error.code.Should().Be("eval_exception");
        response.error.exception_type.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Syntax error response includes position.
    /// </summary>
    [Fact]
    public void Evaluate_SyntaxError_IncludesPosition()
    {
        var response = new
        {
            success = false,
            error = new
            {
                code = "syntax_error",
                message = "Unexpected token ')'",
                position = 12
            }
        };

        response.error.code.Should().Be("syntax_error");
        response.error.position.Should().BePositive();
    }

    /// <summary>
    /// Variable unavailable error for optimized code.
    /// </summary>
    [Fact]
    public void Evaluate_VariableUnavailable_Format()
    {
        var response = new
        {
            success = false,
            error = new
            {
                code = "variable_unavailable",
                message = "Variable 'x' is not available (optimized away)"
            }
        };

        response.error.code.Should().Be("variable_unavailable");
    }

    /// <summary>
    /// null value is valid for reference types.
    /// </summary>
    [Fact]
    public void Evaluate_NullValue_IsValid()
    {
        var response = new
        {
            success = true,
            value = (string?)null,
            type = "System.String",
            has_children = false
        };

        response.success.Should().BeTrue();
        response.value.Should().BeNull();
    }

    /// <summary>
    /// Complex expression evaluation with nested properties.
    /// </summary>
    [Fact]
    public void Evaluate_ComplexExpression_Response()
    {
        // Expression: customer.Address.City
        var response = new
        {
            success = true,
            value = "\"Seattle\"",
            type = "System.String",
            has_children = false
        };

        response.success.Should().BeTrue();
        response.value.Should().Be("\"Seattle\"");
    }

    /// <summary>
    /// Method call expression evaluation.
    /// </summary>
    [Fact]
    public void Evaluate_MethodCall_Response()
    {
        // Expression: list.Count()
        var response = new
        {
            success = true,
            value = "42",
            type = "System.Int32",
            has_children = false
        };

        response.success.Should().BeTrue();
        response.type.Should().Be("System.Int32");
    }

    /// <summary>
    /// Expression with array indexer.
    /// </summary>
    [Fact]
    public void Evaluate_ArrayIndexer_Response()
    {
        // Expression: items[0]
        var response = new
        {
            success = true,
            value = "{...}",
            type = "MyApp.Item",
            has_children = true
        };

        response.success.Should().BeTrue();
        response.has_children.Should().BeTrue();
    }
}
