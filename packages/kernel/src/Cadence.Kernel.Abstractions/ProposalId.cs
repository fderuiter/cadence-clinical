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
[TypeConverter(typeof(EntityIdTypeConverter<ProposalId>))]
[JsonConverter(typeof(EntityIdJsonConverter<ProposalId>))]
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

    public int CompareTo(object? obj) => EntityIdOperations.CompareTo(this, obj);

    public static bool operator <(ProposalId left, ProposalId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProposalId left, ProposalId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProposalId left, ProposalId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProposalId left, ProposalId right) => left.CompareTo(right) >= 0;

    public static ProposalId Parse(string s) => EntityIdOperations.Parse<ProposalId>(s);

    public static ProposalId Parse(string s, IFormatProvider? provider) => EntityIdOperations.Parse<ProposalId>(s, provider);

    public static ProposalId Parse(ReadOnlySpan<char> s) => EntityIdOperations.Parse<ProposalId>(s);

    public static ProposalId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => EntityIdOperations.Parse<ProposalId>(s, provider);

    public static bool TryParse([NotNullWhen(true)] string? s, out ProposalId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ProposalId result) => EntityIdOperations.TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProposalId result) =>
        EntityIdOperations.TryParse(s, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ProposalId result) =>
        EntityIdOperations.TryParse(s, provider, out result);
}
