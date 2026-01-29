namespace DebugMcp.Models.Breakpoints;

/// <summary>
/// Represents a position in source code with optional column for lambda targeting.
/// </summary>
/// <param name="File">Absolute path to source file.</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column for targeting lambdas/inline statements (optional).</param>
/// <param name="EndLine">End line from PDB sequence point (optional).</param>
/// <param name="EndColumn">End column from PDB sequence point (optional).</param>
/// <param name="FunctionName">Name of containing function (optional).</param>
/// <param name="ModuleName">Name of containing module/assembly (optional).</param>
public record BreakpointLocation(
    string File,
    int Line,
    int? Column = null,
    int? EndLine = null,
    int? EndColumn = null,
    string? FunctionName = null,
    string? ModuleName = null);
