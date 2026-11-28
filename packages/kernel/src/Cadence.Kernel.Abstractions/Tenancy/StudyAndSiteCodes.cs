using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Human-facing clinical study code carried on edge/wire contracts that resolve against <c>IStudyDirectory</c>
/// (K-ID-5). Serialized as a JSON string while typing the property inside the Kernel boundary so module contracts do
/// not pass untyped <see cref="string"/> study identifiers.
/// </summary>
[JsonConverter(typeof(KernelStudyCodeJsonConverter))]
public readonly record struct KernelStudyCode(string Value)
{
    /// <summary>Gets the underlying raw code string, or empty string when default.</summary>
    public string Raw => Value ?? string.Empty;

    /// <summary>True when <see cref="Raw"/> contains non-whitespace characters.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Returns the trimmed study code, or null when empty or whitespace.</summary>
    public string? TrimmedOrNull => string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();

    /// <summary>Implicitly wraps a string study code.</summary>
    public static implicit operator KernelStudyCode(string value) => new(value);

    /// <summary>Implicitly unwraps a study code string.</summary>
    public static implicit operator string(KernelStudyCode code) => code.Raw;

    /// <inheritdoc />
    public override string ToString() => Raw;
}

/// <summary>
/// Human-facing clinical site code carried on edge/wire contracts that resolve against <c>IStudyDirectory</c>
/// (K-ID-5). Serialized as a JSON string while typing the property inside the Kernel boundary so module contracts do
/// not pass untyped <see cref="string"/> site identifiers.
/// </summary>
[JsonConverter(typeof(KernelSiteCodeJsonConverter))]
public readonly record struct KernelSiteCode(string Value)
{
    /// <summary>Gets the underlying raw code string, or empty string when default.</summary>
    public string Raw => Value ?? string.Empty;

    /// <summary>True when <see cref="Raw"/> contains non-whitespace characters.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Returns the trimmed site code, or null when empty or whitespace.</summary>
    public string? TrimmedOrNull => string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();

    /// <summary>Implicitly wraps a string site code.</summary>
    public static implicit operator KernelSiteCode(string value) => new(value);

    /// <summary>Implicitly unwraps a site code string.</summary>
    public static implicit operator string(KernelSiteCode code) => code.Raw;

    /// <inheritdoc />
    public override string ToString() => Raw;
}

/// <summary>Serializes <see cref="KernelStudyCode"/> as a JSON string.</summary>
public sealed class KernelStudyCodeJsonConverter : JsonConverter<KernelStudyCode>
{
    /// <inheritdoc />
    public override KernelStudyCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, KernelStudyCode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Raw);
    }
}

/// <summary>Serializes <see cref="KernelSiteCode"/> as a JSON string.</summary>
public sealed class KernelSiteCodeJsonConverter : JsonConverter<KernelSiteCode>
{
    /// <inheritdoc />
    public override KernelSiteCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, KernelSiteCode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Raw);
    }
}
