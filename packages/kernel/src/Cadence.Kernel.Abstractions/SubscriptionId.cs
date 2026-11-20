using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUID identifier for a commercial Subscription attached to a tenant.
/// Commercial state never grants clinical data access or bypasses kernel authority checks.
/// </summary>
[TypeConverter(typeof(SubscriptionIdTypeConverter))]
[JsonConverter(typeof(SubscriptionIdJsonConverter))]
public readonly record struct SubscriptionId(Guid Value) : IEntityId<SubscriptionId>
{
    public static SubscriptionId New() => new(Guid.CreateVersion7());

    public static SubscriptionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static SubscriptionId FromGuid(Guid value) => new(value);

    public static SubscriptionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(SubscriptionId id) => id.Value;

    public static explicit operator SubscriptionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(SubscriptionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        SubscriptionId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(SubscriptionId)}", nameof(obj))
    };

    public static bool operator <(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) >= 0;

    public static SubscriptionId Parse(string s) => Parse(s, null);

    public static SubscriptionId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new SubscriptionId(Guid.Parse(s, provider));
    }

    public static SubscriptionId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static SubscriptionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out SubscriptionId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out SubscriptionId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SubscriptionId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new SubscriptionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SubscriptionId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new SubscriptionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class SubscriptionIdTypeConverter : EntityIdTypeConverter<SubscriptionId>;

public sealed class SubscriptionIdJsonConverter : EntityIdJsonConverter<SubscriptionId>;
