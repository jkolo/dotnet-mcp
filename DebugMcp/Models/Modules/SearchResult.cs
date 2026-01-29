namespace DebugMcp.Models.Modules;

/// <summary>
/// Type of search to perform.
/// </summary>
public enum SearchType
{
    /// <summary>Search types only.</summary>
    Types,

    /// <summary>Search methods only.</summary>
    Methods,

    /// <summary>Search both types and methods.</summary>
    Both
}

/// <summary>
/// A method that matched a search query.
/// </summary>
/// <param name="DeclaringType">Type containing the method.</param>
/// <param name="ModuleName">Module containing the type.</param>
/// <param name="Method">The matching method info.</param>
/// <param name="MatchReason">Why it matched (name, signature).</param>
public sealed record MethodSearchMatch(
    string DeclaringType,
    string ModuleName,
    MethodMemberInfo Method,
    string MatchReason);

/// <summary>
/// Result from searching across modules.
/// </summary>
/// <param name="Query">Original search pattern.</param>
/// <param name="SearchType">What was searched (Types, Methods, Both).</param>
/// <param name="Types">Matching types.</param>
/// <param name="Methods">Matching methods.</param>
/// <param name="TotalMatches">Total matches found.</param>
/// <param name="ReturnedMatches">Matches returned (may be limited).</param>
/// <param name="Truncated">True if results were limited.</param>
/// <param name="ContinuationToken">Token to get more results.</param>
public sealed record SearchResult(
    string Query,
    SearchType SearchType,
    TypeInfo[] Types,
    MethodSearchMatch[] Methods,
    int TotalMatches,
    int ReturnedMatches,
    bool Truncated,
    string? ContinuationToken);
