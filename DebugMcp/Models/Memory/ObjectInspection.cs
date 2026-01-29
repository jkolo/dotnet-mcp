using System.Text.Json.Serialization;

namespace DebugMcp.Models.Memory;

/// <summary>
/// Result of inspecting a heap object's contents.
/// </summary>
public sealed record ObjectInspection
{
    /// <summary>Hex address of the object (e.g., "0x00007FF8A1234560").</summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    /// <summary>Full type name (e.g., "MyApp.Models.Customer").</summary>
    [JsonPropertyName("typeName")]
    public required string TypeName { get; init; }

    /// <summary>Total object size in bytes (including header).</summary>
    [JsonPropertyName("size")]
    public required int Size { get; init; }

    /// <summary>Instance fields with values.</summary>
    [JsonPropertyName("fields")]
    public required IReadOnlyList<FieldDetail> Fields { get; init; }

    /// <summary>True if the reference was null.</summary>
    [JsonPropertyName("isNull")]
    public required bool IsNull { get; init; }

    /// <summary>True if circular reference detected during expansion.</summary>
    [JsonPropertyName("hasCircularRef")]
    public bool HasCircularRef { get; init; }

    /// <summary>True if field list was truncated (too many fields).</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}
