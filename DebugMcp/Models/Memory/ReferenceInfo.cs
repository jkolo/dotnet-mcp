using System.Text.Json.Serialization;

namespace DebugMcp.Models.Memory;

/// <summary>
/// Information about an object reference relationship.
/// </summary>
public sealed record ReferenceInfo
{
    /// <summary>Address of referencing object.</summary>
    [JsonPropertyName("sourceAddress")]
    public required string SourceAddress { get; init; }

    /// <summary>Type of referencing object.</summary>
    [JsonPropertyName("sourceType")]
    public required string SourceType { get; init; }

    /// <summary>Address of referenced object.</summary>
    [JsonPropertyName("targetAddress")]
    public required string TargetAddress { get; init; }

    /// <summary>Type of referenced object.</summary>
    [JsonPropertyName("targetType")]
    public required string TargetType { get; init; }

    /// <summary>Path from source to target (e.g., "_customer", "[0]").</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Type of reference.</summary>
    [JsonPropertyName("referenceType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ReferenceType ReferenceType { get; init; }
}
