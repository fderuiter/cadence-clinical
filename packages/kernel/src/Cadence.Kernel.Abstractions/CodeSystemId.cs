using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a code system (a terminology such as LOINC, UCUM or CDISC CT) registered in a tenant's Concept Registry.
/// </summary>
[TypeConverter(typeof(CodeSystemIdTypeConverter))]
[JsonConverter(typeof(CodeSystemIdJsonConverter))]
public readonly record struct CodeSystemId(Guid Value) : IEntityId<CodeSystemId>
{
    public static CodeSystemId New() => new(Guid.CreateVersion7());

    public static CodeSystemId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static CodeSystemId FromGuid(Guid value) => new(value);

    public static CodeSystemId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(CodeSystemId id) => id.Value;

    public static explicit operator CodeSystemId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(CodeSystemId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        CodeSystemId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(CodeSystemId)}", nameof(obj))
    };

    public static bool operator <(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) < 0;
    public static bool operator <=(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) <= 0;
    public static bool operator >(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) > 0;
    public static bool operator >=(CodeSystemId left, CodeSystemId right) => left.CompareTo(right) >= 0;

    public static CodeSystemId Parse(string s) => Parse(s, null);

    public static CodeSystemId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new CodeSystemId(Guid.Parse(s, provider));
    }

    public static CodeSystemId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static CodeSystemId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out CodeSystemId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out CodeSystemId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CodeSystemId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new CodeSystemId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CodeSystemId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new CodeSystemId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class CodeSystemIdTypeConverter : EntityIdTypeConverter<CodeSystemId>;

public sealed class CodeSystemIdJsonConverter : EntityIdJsonConverter<CodeSystemId>;
