namespace DebugMcp.Models.Modules;

/// <summary>
/// Represents a type defined in a module.
/// </summary>
/// <param name="FullName">Namespace.TypeName (e.g., "MyApp.Models.Customer").</param>
/// <param name="Name">Simple type name (e.g., "Customer").</param>
/// <param name="Namespace">Namespace (e.g., "MyApp.Models").</param>
/// <param name="Kind">Type kind (Class, Interface, Struct, etc.).</param>
/// <param name="Visibility">Access modifier (Public, Internal, etc.).</param>
/// <param name="IsGeneric">True if type has generic parameters.</param>
/// <param name="GenericParameters">Generic type parameter names (e.g., ["T", "TKey"]).</param>
/// <param name="IsNested">True if nested inside another type.</param>
/// <param name="DeclaringType">Parent type if nested.</param>
/// <param name="ModuleName">Module that defines this type.</param>
/// <param name="BaseType">Base class (null for interfaces/Object).</param>
/// <param name="Interfaces">Implemented interfaces.</param>
public sealed record TypeInfo(
    string FullName,
    string Name,
    string? Namespace,
    TypeKind Kind,
    Visibility Visibility,
    bool IsGeneric,
    string[]? GenericParameters,
    bool IsNested,
    string? DeclaringType,
    string ModuleName,
    string? BaseType,
    string[]? Interfaces);
