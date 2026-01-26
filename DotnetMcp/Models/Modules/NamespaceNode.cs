namespace DotnetMcp.Models.Modules;

/// <summary>
/// Represents a namespace in the namespace hierarchy.
/// </summary>
/// <param name="Name">Namespace name (last segment).</param>
/// <param name="FullName">Full namespace path.</param>
/// <param name="TypeCount">Types directly in namespace.</param>
/// <param name="ChildNamespaces">Child namespace names.</param>
/// <param name="Depth">Nesting level (0 = root).</param>
public sealed record NamespaceNode(
    string Name,
    string FullName,
    int TypeCount,
    string[] ChildNamespaces,
    int Depth);
