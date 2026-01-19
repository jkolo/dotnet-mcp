namespace DotnetMcp.Services.Breakpoints;

/// <summary>
/// Reads PDB symbol information for source-to-IL mapping.
/// </summary>
public interface IPdbSymbolReader
{
    /// <summary>
    /// Finds the IL offset for a given source location.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly (DLL or EXE).</param>
    /// <param name="sourceFile">Absolute path to source file.</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="column">Optional 1-based column for targeting specific sequence point.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>IL offset if found, null if no executable code at location.</returns>
    Task<ILOffsetResult?> FindILOffsetAsync(
        string assemblyPath,
        string sourceFile,
        int line,
        int? column = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sequence points on a specific line.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly.</param>
    /// <param name="sourceFile">Absolute path to source file.</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of sequence points on the line.</returns>
    Task<IReadOnlyList<SequencePointInfo>> GetSequencePointsOnLineAsync(
        string assemblyPath,
        string sourceFile,
        int line,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the nearest valid line with executable code.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly.</param>
    /// <param name="sourceFile">Absolute path to source file.</param>
    /// <param name="line">1-based line number to search from.</param>
    /// <param name="searchRange">Number of lines to search above and below.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Nearest valid line number, or null if none found within range.</returns>
    Task<int?> FindNearestValidLineAsync(
        string assemblyPath,
        string sourceFile,
        int line,
        int searchRange = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of IL offset lookup containing offset and method token.
/// </summary>
/// <param name="ILOffset">IL offset within the method.</param>
/// <param name="MethodToken">Metadata token of the containing method.</param>
/// <param name="StartLine">Start line of the sequence point.</param>
/// <param name="StartColumn">Start column of the sequence point.</param>
/// <param name="EndLine">End line of the sequence point.</param>
/// <param name="EndColumn">End column of the sequence point.</param>
public record ILOffsetResult(
    int ILOffset,
    int MethodToken,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

/// <summary>
/// Information about a sequence point in a method.
/// </summary>
/// <param name="ILOffset">IL offset within the method.</param>
/// <param name="StartLine">1-based start line.</param>
/// <param name="StartColumn">1-based start column.</param>
/// <param name="EndLine">1-based end line.</param>
/// <param name="EndColumn">1-based end column.</param>
/// <param name="IsHidden">True if this is a hidden sequence point.</param>
public record SequencePointInfo(
    int ILOffset,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    bool IsHidden);
