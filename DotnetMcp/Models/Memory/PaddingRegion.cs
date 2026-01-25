using System.Text.Json.Serialization;

namespace DotnetMcp.Models.Memory;

/// <summary>
/// Padding between fields for alignment.
/// </summary>
public sealed record PaddingRegion
{
    /// <summary>Start offset of padding.</summary>
    [JsonPropertyName("offset")]
    public required int Offset { get; init; }

    /// <summary>Padding size in bytes.</summary>
    [JsonPropertyName("size")]
    public required int Size { get; init; }

    /// <summary>Reason for padding (e.g., "alignment for Int64").</summary>
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}
