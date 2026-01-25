namespace DotnetMcp.Models.Inspection;

/// <summary>
/// Result of expression evaluation.
/// </summary>
/// <param name="Success">True if evaluation succeeded.</param>
/// <param name="Value">Result value as display string.</param>
/// <param name="Type">Result type name.</param>
/// <param name="HasChildren">True if result can be expanded.</param>
/// <param name="Error">Error details if Success is false.</param>
public sealed record EvaluationResult(
    bool Success,
    string? Value = null,
    string? Type = null,
    bool HasChildren = false,
    EvaluationError? Error = null);
