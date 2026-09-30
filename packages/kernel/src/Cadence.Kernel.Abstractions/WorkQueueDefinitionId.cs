using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed UUIDv7 identifier for one version of a work-queue definition: which items are due for whoever holds a
/// capability (spec §9.3, Canvas D10, ADR-2486).
/// </summary>
[TypeConverter(typeof(WorkQueueDefinitionIdTypeConverter))]
[JsonConverter(typeof(WorkQueueDefinitionIdJsonConverter))]
public readonly record struct WorkQueueDefinitionId(Guid Value) : IEntityId<WorkQueueDefinitionId>
{
    public static WorkQueueDefinitionId New() => new(Guid.CreateVersion7());

    public static WorkQueueDefinitionId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static WorkQueueDefinitionId FromGuid(Guid value) => new(value);

    public static WorkQueueDefinitionId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(WorkQueueDefinitionId id) => id.Value;

    public static explicit operator WorkQueueDefinitionId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(WorkQueueDefinitionId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        WorkQueueDefinitionId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(WorkQueueDefinitionId)}", nameof(obj))
    };

    public static bool operator <(WorkQueueDefinitionId left, WorkQueueDefinitionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(WorkQueueDefinitionId left, WorkQueueDefinitionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(WorkQueueDefinitionId left, WorkQueueDefinitionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(WorkQueueDefinitionId left, WorkQueueDefinitionId right) => left.CompareTo(right) >= 0;

    public static WorkQueueDefinitionId Parse(string s) => Parse(s, null);

    public static WorkQueueDefinitionId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new WorkQueueDefinitionId(Guid.Parse(s, provider));
    }

    public static WorkQueueDefinitionId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static WorkQueueDefinitionId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out WorkQueueDefinitionId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out WorkQueueDefinitionId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out WorkQueueDefinitionId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new WorkQueueDefinitionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out WorkQueueDefinitionId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new WorkQueueDefinitionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class WorkQueueDefinitionIdTypeConverter : EntityIdTypeConverter<WorkQueueDefinitionId>;

public sealed class WorkQueueDefinitionIdJsonConverter : EntityIdJsonConverter<WorkQueueDefinitionId>;
