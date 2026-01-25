using System.Text.Json.Serialization;

namespace DotnetMcp.Models.Memory;

/// <summary>
/// Field layout information within a type.
/// </summary>
public sealed record LayoutField
{
    /// <summary>Field name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Field type name.</summary>
    [JsonPropertyName("typeName")]
    public required string TypeName { get; init; }

    /// <summary>Byte offset from data start (after header).</summary>
    [JsonPropertyName("offset")]
    public required int Offset { get; init; }

    /// <summary>Field size in bytes.</summary>
    [JsonPropertyName("size")]
    public required int Size { get; init; }

    /// <summary>Required alignment.</summary>
    [JsonPropertyName("alignment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Alignment { get; init; }

    /// <summary>True for reference types (pointer-sized).</summary>
    [JsonPropertyName("isReference")]
    public bool IsReference { get; init; }

    /// <summary>Declaring type if inherited field.</summary>
    [JsonPropertyName("declaringType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaringType { get; init; }
}
