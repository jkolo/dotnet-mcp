namespace DebugMcp.Models.Modules;

/// <summary>
/// Represents a property in a type.
/// </summary>
/// <param name="Name">Property name.</param>
/// <param name="Type">Property type.</param>
/// <param name="Visibility">Most accessible visibility (getter or setter).</param>
/// <param name="IsStatic">True for static properties.</param>
/// <param name="HasGetter">Has get accessor.</param>
/// <param name="HasSetter">Has set accessor.</param>
/// <param name="GetterVisibility">Getter visibility (null if no getter).</param>
/// <param name="SetterVisibility">Setter visibility (null if no setter).</param>
/// <param name="IsIndexer">True for indexers (this[]).</param>
/// <param name="IndexerParameters">Indexer parameters (null if not indexer).</param>
public sealed record PropertyMemberInfo(
    string Name,
    string Type,
    Visibility Visibility,
    bool IsStatic,
    bool HasGetter,
    bool HasSetter,
    Visibility? GetterVisibility,
    Visibility? SetterVisibility,
    bool IsIndexer,
    ParameterInfo[]? IndexerParameters);
