using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Serialization;

/// <summary>
/// Wire-shape adapters for strongly-typed ID/code value objects and status enums across the Execution bounded context.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-001
/// @req:PRD-SYS-007
/// @req:SRS-EDC-001
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
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

public sealed class ObservationIdJsonConverter : JsonConverter<ObservationId>
{
    public override ObservationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for ObservationId.")
            : new ObservationId(Guid.Parse(raw));
    }

    public override void Write(Utf8JsonWriter writer, ObservationId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class DomainCodeJsonConverter : JsonConverter<DomainCode>
{
    public override DomainCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for DomainCode.")
            : new DomainCode(raw);
    }

    public override void Write(Utf8JsonWriter writer, DomainCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class TestCodeJsonConverter : JsonConverter<TestCode>
{
    public override TestCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for TestCode.")
            : new TestCode(raw);
    }

    public override void Write(Utf8JsonWriter writer, TestCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class FormSubmissionIdJsonConverter : JsonConverter<FormSubmissionId>
{
    public override FormSubmissionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for FormSubmissionId.")
            : new FormSubmissionId(Guid.Parse(raw));
    }

    public override void Write(Utf8JsonWriter writer, FormSubmissionId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class FormIdJsonConverter : JsonConverter<FormId>
{
    public override FormId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for FormId.")
            : new FormId(raw);
    }

    public override void Write(Utf8JsonWriter writer, FormId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class QueryIdJsonConverter : JsonConverter<QueryId>
{
    public override QueryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return raw is null
            ? throw new JsonException("Expected a non-null JSON string for QueryId.")
            : new QueryId(Guid.Parse(raw));
    }

    public override void Write(Utf8JsonWriter writer, QueryId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class FormSubmissionStatusJsonConverter : JsonConverter<FormSubmissionStatus>
{
    public override FormSubmissionStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return FormSubmissionStatusCodec.TryParse(raw, out var status)
            ? status
            : throw new JsonException($"Invalid FormSubmissionStatus value: '{raw}'.");
    }

    public override void Write(Utf8JsonWriter writer, FormSubmissionStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(FormSubmissionStatusCodec.ToCode(value));
}

public sealed class QueryStatusJsonConverter : JsonConverter<QueryStatus>
{
    public override QueryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return QueryStatusCodec.TryParse(raw, out var status)
            ? status
            : throw new JsonException($"Invalid QueryStatus value: '{raw}'.");
    }

    public override void Write(Utf8JsonWriter writer, QueryStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(QueryStatusCodec.ToCode(value));
}
