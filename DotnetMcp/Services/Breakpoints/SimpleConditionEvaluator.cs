using System.Text.RegularExpressions;

namespace DotnetMcp.Services.Breakpoints;

/// <summary>
/// Simple condition evaluator that handles hit count conditions and literals.
/// Does not require debugger access - useful for basic conditions.
/// </summary>
public sealed partial class SimpleConditionEvaluator : IConditionEvaluator
{
    /// <inheritdoc />
    public ConditionResult Evaluate(string? condition, ConditionContext context)
    {
        // Empty condition = unconditional breakpoint
        if (string.IsNullOrWhiteSpace(condition))
        {
            return ConditionResult.Ok(true);
        }

        condition = condition.Trim();

        // Handle boolean literals
        if (condition.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionResult.Ok(true);
        }

        if (condition.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionResult.Ok(false);
        }

        // Try hit count conditions
        var hitCountResult = EvaluateHitCountCondition(condition, context.HitCount);
        if (hitCountResult != null)
        {
            return hitCountResult;
        }

        // Unsupported condition
        return ConditionResult.Error($"Unsupported condition expression: '{condition}'. " +
            "Simple evaluator supports: 'hitCount <op> N', 'hitCount % N == M', 'true', 'false'");
    }

    /// <inheritdoc />
    public ConditionValidation ValidateCondition(string? condition)
    {
        // Empty is valid
        if (string.IsNullOrWhiteSpace(condition))
        {
            return ConditionValidation.Valid();
        }

        condition = condition.Trim();

        // Boolean literals are valid
        if (condition.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            condition.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionValidation.Valid();
        }

        // Check for unclosed parentheses
        var parenBalance = 0;
        for (var i = 0; i < condition.Length; i++)
        {
            if (condition[i] == '(') parenBalance++;
            else if (condition[i] == ')') parenBalance--;

            if (parenBalance < 0)
            {
                return ConditionValidation.Invalid("Unexpected closing parenthesis", i);
            }
        }

        if (parenBalance != 0)
        {
            return ConditionValidation.Invalid("Unclosed parenthesis", 0);
        }

        // Check for hit count conditions
        if (HitCountComparisonRegex().IsMatch(condition) ||
            HitCountModuloRegex().IsMatch(condition))
        {
            return ConditionValidation.Valid();
        }

        // Check for double operators (syntax error)
        if (DoubleOperatorRegex().IsMatch(condition))
        {
            var match = DoubleOperatorRegex().Match(condition);
            return ConditionValidation.Invalid("Invalid double operator", match.Index);
        }

        // Check for trailing operator (missing operand)
        if (TrailingOperatorRegex().IsMatch(condition))
        {
            return ConditionValidation.Invalid("Missing operand after operator", condition.Length - 1);
        }

        // If we don't recognize it, it might be valid for debugger evaluator
        // but not for simple evaluator
        return ConditionValidation.Invalid(
            $"Condition may require debugger evaluation: '{condition}'");
    }

    private static ConditionResult? EvaluateHitCountCondition(string condition, int hitCount)
    {
        // Pattern: hitCount <op> N
        var comparisonMatch = HitCountComparisonRegex().Match(condition);
        if (comparisonMatch.Success)
        {
            var op = comparisonMatch.Groups["op"].Value;
            var value = int.Parse(comparisonMatch.Groups["value"].Value);

            var result = op switch
            {
                "==" => hitCount == value,
                "!=" => hitCount != value,
                ">" => hitCount > value,
                ">=" => hitCount >= value,
                "<" => hitCount < value,
                "<=" => hitCount <= value,
                _ => throw new InvalidOperationException($"Unknown operator: {op}")
            };

            return ConditionResult.Ok(result);
        }

        // Pattern: hitCount % N == M
        var moduloMatch = HitCountModuloRegex().Match(condition);
        if (moduloMatch.Success)
        {
            var divisor = int.Parse(moduloMatch.Groups["divisor"].Value);
            var remainder = int.Parse(moduloMatch.Groups["remainder"].Value);

            if (divisor == 0)
            {
                return ConditionResult.Error("Division by zero in modulo condition");
            }

            var result = (hitCount % divisor) == remainder;
            return ConditionResult.Ok(result);
        }

        return null;
    }

    [GeneratedRegex(@"^\s*hitCount\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HitCountComparisonRegex();

    [GeneratedRegex(@"^\s*hitCount\s*%\s*(?<divisor>\d+)\s*==\s*(?<remainder>\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HitCountModuloRegex();

    [GeneratedRegex(@"(==\s*==|>\s*>|<\s*<|!=\s*!=|>=\s*>=|<=\s*<=)")]
    private static partial Regex DoubleOperatorRegex();

    [GeneratedRegex(@"(==|!=|>=|<=|>|<)\s*$")]
    private static partial Regex TrailingOperatorRegex();
}
