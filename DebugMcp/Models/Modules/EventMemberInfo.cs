namespace DebugMcp.Models.Modules;

/// <summary>
/// Represents an event in a type.
/// </summary>
/// <param name="Name">Event name.</param>
/// <param name="Type">Event handler type.</param>
/// <param name="Visibility">Event visibility.</param>
/// <param name="IsStatic">True for static events.</param>
/// <param name="AddMethod">Add accessor signature.</param>
/// <param name="RemoveMethod">Remove accessor signature.</param>
public sealed record EventMemberInfo(
    string Name,
    string Type,
    Visibility Visibility,
    bool IsStatic,
    string? AddMethod,
    string? RemoveMethod);
