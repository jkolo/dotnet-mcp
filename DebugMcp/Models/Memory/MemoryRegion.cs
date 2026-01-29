using System.Text.Json.Serialization;

namespace DebugMcp.Models.Memory;

/// <summary>
/// Result of reading raw memory bytes.
/// </summary>
public sealed record MemoryRegion
{
    /// <summary>Start address in hex (e.g., "0x00007FF8A1234560").</summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    /// <summary>Bytes requested.</summary>
    [JsonPropertyName("requestedSize")]
    public required int RequestedSize { get; init; }

    /// <summary>Bytes actually read (may be less at boundary).</summary>
    [JsonPropertyName("actualSize")]
    public required int ActualSize { get; init; }

    /// <summary>Hex dump (e.g., "48 65 6C 6C 6F ...").</summary>
    [JsonPropertyName("bytes")]
    public required string Bytes { get; init; }

    /// <summary>ASCII representation (non-printable as '.').</summary>
    [JsonPropertyName("ascii")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ascii { get; init; }

    /// <summary>Error message if partial read.</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}
