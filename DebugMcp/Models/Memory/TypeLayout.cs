using System.Text.Json.Serialization;

namespace DebugMcp.Models.Memory;

/// <summary>
/// Memory layout information for a type.
/// </summary>
public sealed record TypeLayout
{
    /// <summary>Full type name.</summary>
    [JsonPropertyName("typeName")]
    public required string TypeName { get; init; }

    /// <summary>Total size in bytes (runtime size).</summary>
    [JsonPropertyName("totalSize")]
    public required int TotalSize { get; init; }

    /// <summary>Object header size (0 for value types).</summary>
    [JsonPropertyName("headerSize")]
    public required int HeaderSize { get; init; }

    /// <summary>Size of fields (totalSize - headerSize).</summary>
    [JsonPropertyName("dataSize")]
    public required int DataSize { get; init; }

    /// <summary>Fields with layout info.</summary>
    [JsonPropertyName("fields")]
    public required IReadOnlyList<LayoutField> Fields { get; init; }

    /// <summary>Padding between fields.</summary>
    [JsonPropertyName("padding")]
    public IReadOnlyList<PaddingRegion> Padding { get; init; } = [];

    /// <summary>True for struct, false for class.</summary>
    [JsonPropertyName("isValueType")]
    public required bool IsValueType { get; init; }

    /// <summary>Base type name if any.</summary>
    [JsonPropertyName("baseType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseType { get; init; }
}
