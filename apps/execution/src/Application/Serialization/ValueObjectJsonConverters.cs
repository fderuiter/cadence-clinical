using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Serialization;

/// <summary>
/// Wire-shape adapters for the five MIG-SUBJ-001 strongly-typed ID/code value objects
/// (<see cref="SubjectId"/>, <see cref="SubjectConsentId"/>, <see cref="SubjectCode"/>,
/// <see cref="StudyCode"/>, <see cref="SiteCode"/>). Without a custom converter, System.Text.Json
/// serializes a <c>readonly record struct</c> wrapping one <c>Value</c>/<c>Guid</c> property as a
/// nested <c>{ "value": ... }</c> object rather than the bare JSON string every response DTO in this
/// slice (and every fixture in `fixtures/output_expected_nominal.json`) expects for fields like
/// <c>subject_id</c>/<c>study_id</c>/<c>site_id</c>. Each converter here simply defers to the type's
/// own <c>ToString()</c> override (MIG-SUBJ-001: "All five override <c>ToString()</c> to return the raw
/// value") for writing, and to the type's own validating constructor for reading — no independent
/// validation logic lives here. Registered once via <see cref="ExecutionJsonSerialization.Configure"/>.
/// A <see langword="null"/> JSON token reaching a non-nullable field of one of these types throws
/// <see cref="JsonException"/> rather than silently producing a default/empty instance; a genuinely
/// absent value must instead be modeled as a nullable field (e.g. <c>SiteCode?</c>), which
/// System.Text.Json handles automatically on top of these converters without any extra wiring.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectIdJsonConverter : JsonConverter<SubjectId>
{
    public override SubjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for SubjectId.")
            : new SubjectId(Guid.Parse(raw));
    }

    public override void Write(Utf8JsonWriter writer, SubjectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>See <see cref="SubjectIdJsonConverter"/> — identical shape for the consent aggregate's key.</summary>
public sealed class SubjectConsentIdJsonConverter : JsonConverter<SubjectConsentId>
{
    public override SubjectConsentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for SubjectConsentId.")
            : new SubjectConsentId(Guid.Parse(raw));
    }

    public override void Write(Utf8JsonWriter writer, SubjectConsentId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>See <see cref="SubjectIdJsonConverter"/> — string-wrapper variant for the domain subject code.</summary>
public sealed class SubjectCodeJsonConverter : JsonConverter<SubjectCode>
{
    public override SubjectCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for SubjectCode.")
            : new SubjectCode(raw);
    }

    public override void Write(Utf8JsonWriter writer, SubjectCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>See <see cref="SubjectIdJsonConverter"/> — string-wrapper variant for the domain study code.</summary>
public sealed class StudyCodeJsonConverter : JsonConverter<StudyCode>
{
    public override StudyCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for StudyCode.")
            : new StudyCode(raw);
    }

    public override void Write(Utf8JsonWriter writer, StudyCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>See <see cref="SubjectIdJsonConverter"/> — string-wrapper variant for the domain site code;
/// also transparently covers <c>SiteCode?</c> fields (System.Text.Json wraps any registered value-type
/// converter for the corresponding nullable form automatically).</summary>
public sealed class SiteCodeJsonConverter : JsonConverter<SiteCode>
{
    public override SiteCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for SiteCode.")
            : new SiteCode(raw);
    }

    public override void Write(Utf8JsonWriter writer, SiteCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
