using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Kernel.Execution;

/// <summary>
/// The human-facing code of a participant in a study (screening or subject number, spec §3.2). It is unique within a
/// study, never a key: commands and events carry <see cref="ParticipantId"/> and <see cref="EnrollmentId"/>, and a
/// code is turned into an id only by the resolution query (K-ID-5). A code is 1 to <see cref="MaxLength"/> printable
/// characters with no leading or trailing whitespace, compared ordinally. Serialized as a JSON string.
/// </summary>
[JsonConverter(typeof(SubjectCodeJsonConverter))]
public readonly record struct SubjectCode
{
    /// <summary>The longest code the kernel stores.</summary>
    public const int MaxLength = 64;

    private SubjectCode(string value) => Value = value;

    /// <summary>The code as entered.</summary>
    public string Value { get; }

    /// <summary>Creates a code; throws when <paramref name="value"/> is not a valid code.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, too long, padded or has control characters.</exception>
    public static SubjectCode From(string? value) =>
        TryFrom(value, out var code)
            ? code
            : throw new ArgumentException(
                $"A subject code is 1 to {MaxLength} printable characters with no leading or trailing whitespace.", nameof(value));

    /// <summary>Creates a code, or returns false when <paramref name="value"/> is not a valid code.</summary>
    public static bool TryFrom([NotNullWhen(true)] string? value, out SubjectCode code)
    {
        code = default;
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxLength
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
            || value.Any(char.IsControl))
        {
            return false;
        }

        code = new SubjectCode(value);
        return true;
    }

    /// <summary>True for a code built by <see cref="From"/> or <see cref="TryFrom"/>; false for <c>default</c>.</summary>
    public bool IsValid => Value is not null;

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Writes a <see cref="SubjectCode"/> as a JSON string and rejects an invalid one on read.</summary>
public sealed class SubjectCodeJsonConverter : JsonConverter<SubjectCode>
{
    public override SubjectCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        SubjectCode.TryFrom(reader.GetString(), out var code)
            ? code
            : throw new JsonException("Invalid subject code.");

    public override void Write(Utf8JsonWriter writer, SubjectCode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
