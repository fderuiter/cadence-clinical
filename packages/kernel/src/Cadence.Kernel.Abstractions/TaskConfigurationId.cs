using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Serialization;

namespace Cadence.Kernel;

/// <summary>
/// Strongly-typed deterministic UUID identifier for a shipped intelligence task configuration version (ADR-2631),
/// allowing <see cref="References.NodeRef"/> and attestations to name a signed task configuration.
/// </summary>
[TypeConverter(typeof(TaskConfigurationIdTypeConverter))]
[JsonConverter(typeof(TaskConfigurationIdJsonConverter))]
public readonly record struct TaskConfigurationId(Guid Value) : IEntityId<TaskConfigurationId>
{
    public static TaskConfigurationId New() => new(Guid.CreateVersion7());

    public static TaskConfigurationId New(DateTimeOffset timestamp) => new(Guid.CreateVersion7(timestamp));

    public static TaskConfigurationId FromGuid(Guid value) => new(value);

    public static TaskConfigurationId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static explicit operator Guid(TaskConfigurationId id) => id.Value;

    public static explicit operator TaskConfigurationId(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    public int CompareTo(TaskConfigurationId other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        TaskConfigurationId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(TaskConfigurationId)}", nameof(obj))
    };

    public static bool operator <(TaskConfigurationId left, TaskConfigurationId right) => left.CompareTo(right) < 0;
    public static bool operator <=(TaskConfigurationId left, TaskConfigurationId right) => left.CompareTo(right) <= 0;
    public static bool operator >(TaskConfigurationId left, TaskConfigurationId right) => left.CompareTo(right) > 0;
    public static bool operator >=(TaskConfigurationId left, TaskConfigurationId right) => left.CompareTo(right) >= 0;

    public static TaskConfigurationId Parse(string s) => Parse(s, null);

    public static TaskConfigurationId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new TaskConfigurationId(Guid.Parse(s, provider));
    }

    public static TaskConfigurationId Parse(ReadOnlySpan<char> s) => Parse(s, null);

    public static TaskConfigurationId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, out TaskConfigurationId result) =>
        TryParse(s, null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out TaskConfigurationId result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TaskConfigurationId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new TaskConfigurationId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TaskConfigurationId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new TaskConfigurationId(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}

public sealed class TaskConfigurationIdTypeConverter : EntityIdTypeConverter<TaskConfigurationId>;

public sealed class TaskConfigurationIdJsonConverter : EntityIdJsonConverter<TaskConfigurationId>;
