using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using NpgsqlTypes;

namespace Cadence.Database.Temporal;

/// <summary>
/// Contract implemented by standalone bitemporal period rows carrying PostgreSQL <c>tstzrange</c>
/// <c>valid_period</c> and <c>recorded_period</c> columns (ADR-2246, CLINICAL_KERNEL_SPECIFICATION §4.1).
/// </summary>
public interface IBitemporalPeriodEntity
{
    NpgsqlRange<DateTimeOffset> ValidPeriod { get; set; }

    NpgsqlRange<DateTimeOffset> RecordedPeriod { get; set; }
}

/// <summary>
/// Provider-aware EF Core mapping and <c>as_of(valid_at, known_at)</c> query helpers for standalone bitemporal
/// period tables. Maps <see cref="NpgsqlRange{T}"/> natively to <c>tstzrange</c> on Npgsql and uses an ISO-8601
/// string converter on non-Npgsql unit-test providers (InMemory, SQLite).
/// </summary>
public static class StandaloneBitemporalModelExtensions
{
    private const long MicrosecondTicks = 10L;

    private static readonly ValueConverter<NpgsqlRange<DateTimeOffset>, string> NonNpgsqlRangeConverter = new(
        range => FormatRange(range),
        text => ParseRange(text));

    public static void ConfigureStandaloneBitemporal<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        DbContext context)
        where TEntity : class, IBitemporalPeriodEntity
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(context);

        bool isNpgsql = context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;

        var validProp = builder.Property(x => x.ValidPeriod)
            .HasColumnName(StandaloneTemporalSql.ValidPeriodColumn)
            .IsRequired();

        var recordedProp = builder.Property(x => x.RecordedPeriod)
            .HasColumnName(StandaloneTemporalSql.RecordedPeriodColumn)
            .IsRequired();

        if (isNpgsql)
        {
            validProp.HasColumnType("tstzrange");
            recordedProp.HasColumnType("tstzrange");
        }
        else
        {
            validProp.HasConversion(NonNpgsqlRangeConverter);
            recordedProp.HasConversion(NonNpgsqlRangeConverter);
        }

        builder.Property<long>("_version")
            .IsConcurrencyToken()
            .HasDefaultValue(0L);
    }

    public static NpgsqlRange<DateTimeOffset> OpenFrom(DateTimeOffset start) =>
        new(TruncateToMicroseconds(start), true, false, default, false, true);

    public static NpgsqlRange<DateTimeOffset> CreateOpenPeriod(DateTimeOffset start) =>
        OpenFrom(start);

    public static NpgsqlRange<DateTimeOffset> HalfOpen(DateTimeOffset start, DateTimeOffset end)
    {
        DateTimeOffset s = TruncateToMicroseconds(start);
        DateTimeOffset e = EnsureAfter(s, TruncateToMicroseconds(end));
        return new(s, true, false, e, false, false);
    }

    public static NpgsqlRange<DateTimeOffset> CreateClosedPeriod(DateTimeOffset start, DateTimeOffset end) =>
        HalfOpen(start, end);

    public static NpgsqlRange<DateTimeOffset> CloseRecordedAt(
        NpgsqlRange<DateTimeOffset> openRecordedPeriod,
        DateTimeOffset closedAt)
    {
        DateTimeOffset lower = TruncateToMicroseconds(openRecordedPeriod.LowerBound);
        DateTimeOffset effectiveClose = EnsureAfter(lower, TruncateToMicroseconds(closedAt));
        return new NpgsqlRange<DateTimeOffset>(lower, true, false, effectiveClose, false, false);
    }

    public static bool ContainsInstant(NpgsqlRange<DateTimeOffset> range, DateTimeOffset instant)
    {
        if (range.IsEmpty)
        {
            return false;
        }

        if (!range.LowerBoundInfinite)
        {
            bool lowerOk = range.LowerBoundIsInclusive
                ? instant >= range.LowerBound
                : instant > range.LowerBound;
            if (!lowerOk)
            {
                return false;
            }
        }

        if (!range.UpperBoundInfinite)
        {
            bool upperOk = range.UpperBoundIsInclusive
                ? instant <= range.UpperBound
                : instant < range.UpperBound;
            if (!upperOk)
            {
                return false;
            }
        }

        return true;
    }

    public static async Task<List<TEntity>> QueryAsOfAsync<TEntity>(
        this IQueryable<TEntity> query,
        DbContext context,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default)
        where TEntity : class, IBitemporalPeriodEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        bool isNpgsql = context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;
        if (isNpgsql)
        {
            return await query
                .Where(p => p.ValidPeriod.Contains(validAt) && p.RecordedPeriod.Contains(knownAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var candidates = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidates
            .Where(p => ContainsInstant(p.ValidPeriod, validAt) && ContainsInstant(p.RecordedPeriod, knownAt))
            .ToList();
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        long remainder = value.UtcTicks % MicrosecondTicks;
        return remainder == 0 ? value : new DateTimeOffset(value.UtcTicks - remainder, TimeSpan.Zero);
    }

    private static DateTimeOffset EnsureAfter(DateTimeOffset lower, DateTimeOffset candidate) =>
        candidate > lower ? candidate : lower.AddTicks(MicrosecondTicks);

    private static string FormatRange(NpgsqlRange<DateTimeOffset> range)
    {
        if (range.IsEmpty)
        {
            return "empty";
        }

        char left = range.LowerBoundIsInclusive ? '[' : '(';
        char right = range.UpperBoundIsInclusive ? ']' : ')';
        string lower = range.LowerBoundInfinite ? "-infinity" : range.LowerBound.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        string upper = range.UpperBoundInfinite ? "infinity" : range.UpperBound.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return $"{left}{lower},{upper}{right}";
    }

    private static NpgsqlRange<DateTimeOffset> ParseRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "empty", StringComparison.OrdinalIgnoreCase))
        {
            return NpgsqlRange<DateTimeOffset>.Empty;
        }

        bool lowerInc = text[0] == '[';
        bool upperInc = text[^1] == ']';
        string inner = text[1..^1];
        int comma = inner.IndexOf(',', StringComparison.Ordinal);
        string lowerStr = inner[..comma];
        string upperStr = inner[(comma + 1)..];

        bool lowerInf = string.Equals(lowerStr, "-infinity", StringComparison.OrdinalIgnoreCase);
        bool upperInf = string.Equals(upperStr, "infinity", StringComparison.OrdinalIgnoreCase);

        DateTimeOffset lower = lowerInf ? default : DateTimeOffset.Parse(lowerStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DateTimeOffset upper = upperInf ? default : DateTimeOffset.Parse(upperStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        return new NpgsqlRange<DateTimeOffset>(lower, lowerInc && !lowerInf, lowerInf, upper, upperInc && !upperInf, upperInf);
    }
}
