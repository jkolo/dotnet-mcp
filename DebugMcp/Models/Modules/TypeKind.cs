namespace DebugMcp.Models.Modules;

/// <summary>
/// Categorizes the kind of a .NET type.
/// </summary>
public enum TypeKind
{
    /// <summary>A class type.</summary>
    Class,

    /// <summary>An interface type.</summary>
    Interface,

    /// <summary>A struct (value type).</summary>
    Struct,

    /// <summary>An enum type.</summary>
    Enum,

    /// <summary>A delegate type.</summary>
    Delegate
}
