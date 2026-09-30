using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Grant: an actor's authority to exercise a capability, or a role's
/// capabilities, within a scope for a period (spec §9.3). A delegation is a grant issued under another grant.
/// </summary>
[TypeConverter(typeof(GrantIdTypeConverter))]
[JsonConverter(typeof(GrantIdJsonConverter))]
public readonly record struct GrantId(Guid Value) : IEntityId<GrantId>
{
    public static GrantId New() => new(Guid.CreateVersion7());

    public static GrantId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static GrantId FromGuid(Guid value) => new(value);

    public static GrantId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(GrantId id) => id.Value;

    public static explicit operator GrantId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(GrantId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        GrantId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(GrantId)}", nameof(obj))
    };

    public static bool operator <(GrantId left, GrantId right) => left.CompareTo(right) < 0;
    public static bool operator <=(GrantId left, GrantId right) => left.CompareTo(right) <= 0;
    public static bool operator >(GrantId left, GrantId right) => left.CompareTo(right) > 0;
    public static bool operator >=(GrantId left, GrantId right) => left.CompareTo(right) >= 0;

    public static GrantId Parse(string s) => Parse(s, null);

    public static GrantId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new GrantId(Guid.Parse(s, provider));
    }

    public static GrantId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static GrantId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out GrantId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out GrantId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out GrantId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new GrantId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out GrantId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new GrantId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class GrantIdTypeConverter : EntityIdTypeConverter<GrantId>;

public sealed class GrantIdJsonConverter : EntityIdJsonConverter<GrantId>;
