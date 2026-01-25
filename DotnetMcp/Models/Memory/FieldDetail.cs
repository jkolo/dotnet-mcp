using System.Text.Json.Serialization;

namespace DotnetMcp.Models.Memory;

/// <summary>
/// Information about a single object field.
/// </summary>
public sealed record FieldDetail
{
    /// <summary>Field name (e.g., "_customerId").</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Field type name (e.g., "System.Int32").</summary>
    [JsonPropertyName("typeName")]
    public required string TypeName { get; init; }

    /// <summary>Formatted value (e.g., "42", "\"Hello\"", "{Customer}").</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Byte offset from object start.</summary>
    [JsonPropertyName("offset")]
    public required int Offset { get; init; }

    /// <summary>Field size in bytes.</summary>
    [JsonPropertyName("size")]
    public required int Size { get; init; }

    /// <summary>True if field can be expanded (reference/complex type).</summary>
    [JsonPropertyName("hasChildren")]
    public required bool HasChildren { get; init; }

    /// <summary>Number of children (for arrays/collections), null if not applicable.</summary>
    [JsonPropertyName("childCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChildCount { get; init; }

    /// <summary>True if static field (included optionally).</summary>
    [JsonPropertyName("isStatic")]
    public bool IsStatic { get; init; }
}
