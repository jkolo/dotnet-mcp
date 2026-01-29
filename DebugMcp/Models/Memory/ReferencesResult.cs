using System.Text.Json.Serialization;

namespace DebugMcp.Models.Memory;

/// <summary>
/// Result of reference analysis for an object.
/// </summary>
public sealed record ReferencesResult
{
    /// <summary>Address of the analyzed object.</summary>
    [JsonPropertyName("targetAddress")]
    public required string TargetAddress { get; init; }

    /// <summary>Type of the analyzed object.</summary>
    [JsonPropertyName("targetType")]
    public required string TargetType { get; init; }

    /// <summary>Objects this object references.</summary>
    [JsonPropertyName("outbound")]
    public IReadOnlyList<ReferenceInfo> Outbound { get; init; } = [];

    /// <summary>Objects referencing this object.</summary>
    [JsonPropertyName("inbound")]
    public IReadOnlyList<ReferenceInfo> Inbound { get; init; } = [];

    /// <summary>Total outbound refs (may exceed returned list).</summary>
    [JsonPropertyName("outboundCount")]
    public int OutboundCount { get; init; }

    /// <summary>Total inbound refs (may exceed returned list).</summary>
    [JsonPropertyName("inboundCount")]
    public int InboundCount { get; init; }

    /// <summary>True if results were truncated.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}
