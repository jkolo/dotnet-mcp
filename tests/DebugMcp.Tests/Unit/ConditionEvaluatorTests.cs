using DebugMcp.Services.Breakpoints;
using FluentAssertions;

namespace DebugMcp.Tests.Unit;

/// <summary>
/// Unit tests for condition evaluation logic.
/// </summary>
public class ConditionEvaluatorTests
{
    /// <summary>
    /// Condition evaluator can parse simple hit count conditions.
    /// </summary>
    [Theory]
    [InlineData("hitCount == 5", 5, true)]
    [InlineData("hitCount == 5", 4, false)]
    [InlineData("hitCount > 3", 5, true)]
    [InlineData("hitCount > 3", 3, false)]
    [InlineData("hitCount >= 3", 3, true)]
    [InlineData("hitCount < 10", 5, true)]
    [InlineData("hitCount <= 10", 10, true)]
    public void SimpleConditionEvaluator_HitCountConditions_EvaluateCorrectly(
        string condition, int hitCount, bool expected)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();
        var context = new ConditionContext { HitCount = hitCount };

        // Act
        var result = evaluator.Evaluate(condition, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    /// <summary>
    /// Condition evaluator can parse modulo conditions (every Nth hit).
    /// </summary>
    [Theory]
    [InlineData("hitCount % 10 == 0", 10, true)]
    [InlineData("hitCount % 10 == 0", 20, true)]
    [InlineData("hitCount % 10 == 0", 5, false)]
    [InlineData("hitCount % 5 == 0", 15, true)]
    public void SimpleConditionEvaluator_ModuloConditions_EvaluateCorrectly(
        string condition, int hitCount, bool expected)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();
        var context = new ConditionContext { HitCount = hitCount };

        // Act
        var result = evaluator.Evaluate(condition, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    /// <summary>
    /// Condition evaluator returns true for null/empty conditions (unconditional).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SimpleConditionEvaluator_NullOrEmptyCondition_ReturnsTrue(string? condition)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();
        var context = new ConditionContext { HitCount = 1 };

        // Act
        var result = evaluator.Evaluate(condition, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Value.Should().BeTrue("empty condition means unconditional breakpoint");
    }

    /// <summary>
    /// Condition evaluator rejects unsupported conditions gracefully.
    /// </summary>
    [Theory]
    [InlineData("x > 5")] // Variable access - not supported by simple evaluator
    [InlineData("foo.Bar()")] // Method call - not supported
    public void SimpleConditionEvaluator_UnsupportedCondition_ReturnsError(string condition)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();
        var context = new ConditionContext { HitCount = 1 };

        // Act
        var result = evaluator.Evaluate(condition, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Condition evaluator handles boolean literals.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public void SimpleConditionEvaluator_BooleanLiterals_EvaluateCorrectly(
        string condition, bool expected)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();
        var context = new ConditionContext { HitCount = 1 };

        // Act
        var result = evaluator.Evaluate(condition, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    /// <summary>
    /// Condition validator identifies valid conditions.
    /// </summary>
    [Theory]
    [InlineData("hitCount > 5", true)]
    [InlineData("hitCount == 10", true)]
    [InlineData("true", true)]
    [InlineData("hitCount % 10 == 0", true)]
    [InlineData("", true)] // Empty is valid (unconditional)
    public void SimpleConditionEvaluator_ValidateCondition_IdentifiesValidConditions(
        string condition, bool expectedValid)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();

        // Act
        var result = evaluator.ValidateCondition(condition);

        // Assert
        result.IsValid.Should().Be(expectedValid);
    }

    /// <summary>
    /// Condition validator identifies syntax errors.
    /// </summary>
    [Theory]
    [InlineData("hitCount >")] // Missing operand
    [InlineData("hitCount == == 5")] // Double operator
    [InlineData("(hitCount > 5")] // Unclosed parenthesis
    public void SimpleConditionEvaluator_ValidateCondition_ReportsSyntaxErrors(
        string condition)
    {
        // Arrange
        var evaluator = new SimpleConditionEvaluator();

        // Act
        var result = evaluator.ValidateCondition(condition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
