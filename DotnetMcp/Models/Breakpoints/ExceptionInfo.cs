namespace DotnetMcp.Models.Breakpoints;

/// <summary>
/// Information about an exception that triggered an exception breakpoint.
/// </summary>
/// <param name="Type">Exception type name.</param>
/// <param name="Message">Exception message.</param>
/// <param name="IsFirstChance">True if first-chance, false if unhandled.</param>
/// <param name="StackTrace">Exception stack trace if available.</param>
public record ExceptionInfo(
    string Type,
    string Message,
    bool IsFirstChance,
    string? StackTrace = null);
