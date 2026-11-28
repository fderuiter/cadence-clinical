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
[TypeConverter(typeof(EntityIdTypeConverter<SubscriptionId>))]
[JsonConverter(typeof(EntityIdJsonConverter<SubscriptionId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SubscriptionId left, SubscriptionId right) => left.CompareTo(right) >= 0;

    public static SubscriptionId Parse(string s) => EntityIdOperations.Parse<SubscriptionId>(s);

    public static SubscriptionId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<SubscriptionId>(s, provider);

    public static SubscriptionId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<SubscriptionId>(s);

    public static SubscriptionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<SubscriptionId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out SubscriptionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out SubscriptionId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SubscriptionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SubscriptionId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}
