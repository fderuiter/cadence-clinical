using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for a Proposal: a suggested change to a study, by a person or an automation agent,
/// that has no effect on kernel state until a person executes it (spec §11, ADR-2489).
/// </summary>
[TypeConverter(typeof(ProposalIdTypeConverter))]
[JsonConverter(typeof(ProposalIdJsonConverter))]
public readonly record struct ProposalId(Guid Value) : IEntityId<ProposalId>
{
    public static ProposalId New() => new(Guid.CreateVersion7());

    public static ProposalId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static ProposalId FromGuid(Guid value) => new(value);

    public static ProposalId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(ProposalId id) => id.Value;

    public static explicit operator ProposalId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(ProposalId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        ProposalId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(ProposalId)}", nameof(obj))
    };

    public static bool operator <(ProposalId left, ProposalId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProposalId left, ProposalId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProposalId left, ProposalId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProposalId left, ProposalId right) => left.CompareTo(right) >= 0;

    public static ProposalId Parse(string s) => Parse(s, null);

    public static ProposalId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ProposalId(Guid.Parse(s, provider));
    }

    public static ProposalId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static ProposalId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out ProposalId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ProposalId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProposalId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ProposalId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ProposalId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new ProposalId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class ProposalIdTypeConverter : EntityIdTypeConverter<ProposalId>;

public sealed class ProposalIdJsonConverter : EntityIdJsonConverter<ProposalId>;
