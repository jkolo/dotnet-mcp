namespace DebugMcp.Models.Modules;

/// <summary>
/// Represents a field in a type.
/// </summary>
/// <param name="Name">Field name.</param>
/// <param name="Type">Field type.</param>
/// <param name="Visibility">Field visibility.</param>
/// <param name="IsStatic">True for static fields.</param>
/// <param name="IsReadOnly">True for readonly fields.</param>
/// <param name="IsConst">True for const fields.</param>
/// <param name="ConstValue">Const value if applicable.</param>
public sealed record FieldMemberInfo(
    string Name,
    string Type,
    Visibility Visibility,
    bool IsStatic,
    bool IsReadOnly,
    bool IsConst,
    string? ConstValue);
