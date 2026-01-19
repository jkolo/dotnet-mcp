namespace DotnetMcp.Services.Breakpoints;

/// <summary>
/// Interface for evaluating breakpoint condition expressions.
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// Evaluates a condition expression against the given context.
    /// </summary>
    /// <param name="condition">The condition expression to evaluate (e.g., "hitCount > 5").</param>
    /// <param name="context">The context containing values for evaluation.</param>
    /// <returns>Evaluation result indicating success/failure and the boolean value.</returns>
    ConditionResult Evaluate(string? condition, ConditionContext context);

    /// <summary>
    /// Validates a condition expression for syntax errors.
    /// </summary>
    /// <param name="condition">The condition expression to validate.</param>
    /// <returns>Validation result with error details if invalid.</returns>
    ConditionValidation ValidateCondition(string? condition);
}

/// <summary>
/// Context for condition evaluation containing runtime values.
/// </summary>
public sealed class ConditionContext
{
    /// <summary>
    /// Current hit count for the breakpoint.
    /// </summary>
    public int HitCount { get; init; }

    /// <summary>
    /// Thread ID where breakpoint was hit.
    /// </summary>
    public int ThreadId { get; init; }

    /// <summary>
    /// Function to evaluate expressions against debugger state.
    /// Used by DebuggerConditionEvaluator for variable access.
    /// </summary>
    public Func<string, Task<object?>>? EvaluateExpression { get; init; }
}

/// <summary>
/// Result of condition evaluation.
/// </summary>
public sealed class ConditionResult
{
    /// <summary>
    /// Whether evaluation succeeded (syntax valid, no runtime errors).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The boolean result of the condition (true = break, false = continue).
    /// </summary>
    public bool Value { get; init; }

    /// <summary>
    /// Error message if evaluation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ConditionResult Ok(bool value) => new() { Success = true, Value = value };

    /// <summary>
    /// Creates a failed result with error message.
    /// </summary>
    public static ConditionResult Error(string message) => new() { Success = false, Value = false, ErrorMessage = message };
}

/// <summary>
/// Result of condition validation.
/// </summary>
public sealed class ConditionValidation
{
    /// <summary>
    /// Whether the condition is syntactically valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Position in the condition string where error was found (0-based).
    /// </summary>
    public int? ErrorPosition { get; init; }

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static ConditionValidation Valid() => new() { IsValid = true };

    /// <summary>
    /// Creates an invalid result with error details.
    /// </summary>
    public static ConditionValidation Invalid(string message, int? position = null) =>
        new() { IsValid = false, ErrorMessage = message, ErrorPosition = position };
}
