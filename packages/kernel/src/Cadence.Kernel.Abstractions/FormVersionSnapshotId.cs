using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for an immutable FormVersionSnapshot in the Clinical Kernel.
/// </summary>
[TypeConverter(typeof(FormVersionSnapshotIdTypeConverter))]
[JsonConverter(typeof(FormVersionSnapshotIdJsonConverter))]
public readonly record struct FormVersionSnapshotId(Guid Value) : IEntityId<FormVersionSnapshotId>
{
    public static FormVersionSnapshotId New() => new(Guid.CreateVersion7());

    public static FormVersionSnapshotId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static FormVersionSnapshotId FromGuid(Guid value) => new(value);

    public static FormVersionSnapshotId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(FormVersionSnapshotId id) => id.Value;

    public static explicit operator FormVersionSnapshotId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(FormVersionSnapshotId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        FormVersionSnapshotId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(FormVersionSnapshotId)}", nameof(obj))
    };

    public static bool operator <(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) < 0;
    public static bool operator <=(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) <= 0;
    public static bool operator >(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) > 0;
    public static bool operator >=(FormVersionSnapshotId left, FormVersionSnapshotId right) => left.CompareTo(right) >= 0;

    public static FormVersionSnapshotId Parse(string s) => Parse(s, null);

    public static FormVersionSnapshotId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new FormVersionSnapshotId(Guid.Parse(s, provider));
    }

    public static FormVersionSnapshotId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static FormVersionSnapshotId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out FormVersionSnapshotId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out FormVersionSnapshotId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out FormVersionSnapshotId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new FormVersionSnapshotId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out FormVersionSnapshotId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new FormVersionSnapshotId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class FormVersionSnapshotIdTypeConverter : EntityIdTypeConverter<FormVersionSnapshotId>;

public sealed class FormVersionSnapshotIdJsonConverter : EntityIdJsonConverter<FormVersionSnapshotId>;
