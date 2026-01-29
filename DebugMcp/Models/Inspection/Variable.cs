namespace DebugMcp.Models.Inspection;

/// <summary>
/// Represents a named value in scope during debugging.
/// </summary>
/// <param name="Name">Variable name.</param>
/// <param name="Type">Full type name (e.g., "System.String").</param>
/// <param name="Value">Display value formatted for readability.</param>
/// <param name="Scope">Where this variable comes from.</param>
/// <param name="HasChildren">True if value can be expanded (objects, arrays).</param>
/// <param name="ChildrenCount">Number of children if known.</param>
/// <param name="Path">Expansion path for nested variables (e.g., "user.Address").</param>
public sealed record Variable(
    string Name,
    string Type,
    string Value,
    VariableScope Scope,
    bool HasChildren,
    int? ChildrenCount = null,
    string? Path = null);
