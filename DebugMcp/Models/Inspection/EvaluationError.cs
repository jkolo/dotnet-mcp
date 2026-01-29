namespace DebugMcp.Models.Inspection;

/// <summary>
/// Error details for failed expression evaluation.
/// </summary>
/// <param name="Code">Error code (eval_timeout, eval_exception, syntax_error, variable_unavailable).</param>
/// <param name="Message">Human-readable error message.</param>
/// <param name="ExceptionType">Exception type if evaluation threw an exception.</param>
/// <param name="Position">Character position for syntax errors.</param>
public sealed record EvaluationError(
    string Code,
    string Message,
    string? ExceptionType = null,
    int? Position = null);
