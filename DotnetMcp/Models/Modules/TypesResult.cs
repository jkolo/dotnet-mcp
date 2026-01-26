namespace DotnetMcp.Models.Modules;

/// <summary>
/// Result from type browsing in a module.
/// </summary>
/// <param name="ModuleName">Module that was queried.</param>
/// <param name="NamespaceFilter">Namespace filter (if applied).</param>
/// <param name="Types">Types matching criteria.</param>
/// <param name="Namespaces">Namespace hierarchy.</param>
/// <param name="TotalCount">Total types in module.</param>
/// <param name="ReturnedCount">Types returned.</param>
/// <param name="Truncated">True if paginated.</param>
/// <param name="ContinuationToken">Token for next page.</param>
public sealed record TypesResult(
    string ModuleName,
    string? NamespaceFilter,
    TypeInfo[] Types,
    NamespaceNode[] Namespaces,
    int TotalCount,
    int ReturnedCount,
    bool Truncated,
    string? ContinuationToken);
