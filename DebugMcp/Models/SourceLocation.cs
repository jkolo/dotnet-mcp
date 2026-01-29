namespace DebugMcp.Models;

/// <summary>
/// Represents a position in source code.
/// </summary>
/// <param name="File">Absolute path to source file.</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number (optional).</param>
/// <param name="FunctionName">Name of containing function (optional).</param>
/// <param name="ModuleName">Name of containing module/assembly (optional).</param>
public record SourceLocation(
    string File,
    int Line,
    int? Column = null,
    string? FunctionName = null,
    string? ModuleName = null);
