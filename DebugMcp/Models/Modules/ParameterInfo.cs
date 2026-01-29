namespace DebugMcp.Models.Modules;

/// <summary>
/// Represents a method or indexer parameter.
/// </summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Type">Parameter type.</param>
/// <param name="IsOptional">Has default value.</param>
/// <param name="IsOut">Out parameter.</param>
/// <param name="IsRef">Ref parameter.</param>
/// <param name="DefaultValue">Default value if optional.</param>
public sealed record ParameterInfo(
    string Name,
    string Type,
    bool IsOptional,
    bool IsOut,
    bool IsRef,
    string? DefaultValue);
