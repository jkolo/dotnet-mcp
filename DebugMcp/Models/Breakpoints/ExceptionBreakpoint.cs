namespace DebugMcp.Models.Breakpoints;

/// <summary>
/// Breakpoint that triggers on exception throws.
/// </summary>
/// <param name="Id">Unique identifier (UUID format).</param>
/// <param name="ExceptionType">Full type name (e.g., "System.NullReferenceException").</param>
/// <param name="BreakOnFirstChance">Break when thrown (before catch handlers).</param>
/// <param name="BreakOnSecondChance">Break when unhandled.</param>
/// <param name="IncludeSubtypes">Match derived exception types.</param>
/// <param name="Enabled">User-controlled enable flag.</param>
/// <param name="Verified">True if exception type exists in loaded assemblies.</param>
/// <param name="HitCount">Number of times triggered.</param>
public record ExceptionBreakpoint(
    string Id,
    string ExceptionType,
    bool BreakOnFirstChance,
    bool BreakOnSecondChance,
    bool IncludeSubtypes,
    bool Enabled,
    bool Verified,
    int HitCount);
