using System;
using System.Linq;

using Cadence.Database;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.TestSupport.Kernel.Bitemporal;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using NpgsqlTypes;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Database-free checks of the ADR-2246 persistence layer: <see cref="TimeRange"/> semantics, the <c>tstzrange</c>
/// converter, SQL translation of <c>AsOf</c>, and the guard DDL.
/// </summary>
public class BitemporalPersistenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Validates that <see cref="TimeRange"/> is half-open like <c>tstzrange '[)'</c>: the start is included, the end
    /// is not, an open range contains every later instant, and ranges touching at a bound do not overlap.
    /// </summary>
    [Fact(DisplayName = "TimeRange_IsHalfOpen_LikeTstzrange")]
    [Trait("Category", "Unit")]
    public void TimeRangeIsHalfOpenLikeTstzrange()
    {
        var closed = new TimeRange(T0, T1);
        var open = TimeRange.From(T1);

        closed.Contains(T0).Should().BeTrue();
        closed.Contains(T1).Should().BeFalse();
        open.Contains(T2.AddYears(50)).Should().BeTrue();
        open.Contains(T0).Should().BeFalse();

        closed.Overlaps(open).Should().BeFalse("[T0,T1) and [T1,∞) only touch");
        open.Overlaps(new TimeRange(T0, T2)).Should().BeTrue();
        open.Close(T2).Should().Be(new TimeRange(T1, T2));

        var closeTwice = () => closed.Close(T2);
        closeTwice.Should().Throw<InvalidOperationException>();
        var backwards = () => new TimeRange(T1, T0);
        backwards.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates that ranges are normalised to UTC, so a local-offset instant equals the same UTC instant and is
    /// accepted by Npgsql for <c>timestamptz</c>.
    /// </summary>
    [Fact(DisplayName = "TimeRange_NormalisesToUtc")]
    [Trait("Category", "Unit")]
    public void TimeRangeNormalisesToUtc()
    {
        var local = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.FromHours(2));

        var range = TimeRange.From(local);

        range.Start.Offset.Should().Be(TimeSpan.Zero);
        range.Should().Be(TimeRange.From(T0));
    }

    /// <summary>
    /// Validates the <c>tstzrange</c> converter: closed ranges map to <c>[start,end)</c> and open ranges to an infinite
    /// upper bound, and both round-trip.
    /// </summary>
    [Fact(DisplayName = "TimeRangeValueConverter_RoundTrips_ClosedAndOpenRanges")]
    [Trait("Category", "Unit")]
    public void TimeRangeValueConverterRoundTripsClosedAndOpenRanges()
    {
        var closed = TimeRangeValueConverter.ToProvider(new TimeRange(T0, T1));
        closed.Should().Be(new NpgsqlRange<DateTimeOffset>(T0, true, T1, false));

        var open = TimeRangeValueConverter.ToProvider(TimeRange.From(T0));
        open.UpperBoundInfinite.Should().BeTrue();
        open.LowerBoundIsInclusive.Should().BeTrue();

        TimeRangeValueConverter.FromProvider(closed).Should().Be(new TimeRange(T0, T1));
        TimeRangeValueConverter.FromProvider(open).Should().Be(TimeRange.From(T0));
    }

    /// <summary>
    /// Validates that <c>AsOf</c> runs in the database as index-friendly range containment
    /// (<c>valid_period @&gt; @validAt AND recorded_period @&gt; @knownAt</c>) and that <c>AsOfCurrent</c> uses
    /// <c>upper_inf</c>/<c>upper</c>, rather than evaluating on the client.
    /// </summary>
    [Fact(DisplayName = "AsOf_TranslatesToRangeContainment_InSql")]
    [Trait("Category", "Unit")]
    public void AsOfTranslatesToRangeContainmentInSql()
    {
        using var context = ProbeDbContext.Create("Host=localhost;Database=translation_only");

        var asOf = context.Facts.AsOf(T1, T2).ToQueryString();
        asOf.Should().Contain("p.valid_period @> @validAt").And.Contain("p.recorded_period @> @knownAt");

        var current = context.Facts.AsOfCurrent(T2).ToQueryString();
        current.Should().Contain("upper_inf(p.recorded_period)").And.Contain("upper(p.valid_period) > @now");
    }

    /// <summary>
    /// Validates the K-TIME-1/K-TIME-2 guard DDL: tenant first in a deferred GiST exclusion, row and truncate triggers,
    /// and refusal of identifiers that are not plain snake_case.
    /// </summary>
    [Fact(DisplayName = "BitemporalSql_Guards_ExcludeOverlapsPerTenantAndKey_AndRejectUnsafeIdentifiers")]
    [Trait("Category", "Unit")]
    public void BitemporalSqlGuardsExcludeOverlapsPerTenantAndKeyAndRejectUnsafeIdentifiers()
    {
        var sql = BitemporalSql.CreateGuards(KernelDbContext.Schema, "observations", "participant_id", "concept_id");

        sql.Should().Contain("EXCLUDE USING gist (\"tenant_id\" WITH =, \"participant_id\" WITH =, \"concept_id\" WITH =, \"valid_period\" WITH &&, \"recorded_period\" WITH &&)")
            .And.Contain("DEFERRABLE INITIALLY DEFERRED")
            .And.Contain("BEFORE UPDATE OR DELETE ON \"kernel\".\"observations\"")
            .And.Contain("BEFORE TRUNCATE ON \"kernel\".\"observations\"");

        var injection = () => BitemporalSql.CreateGuards("kernel", "observations; DROP TABLE x", "participant_id");
        injection.Should().Throw<ArgumentException>();
        var noKey = () => BitemporalSql.CreateGuards("kernel", "observations");
        noKey.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates that the kernel model enables <c>btree_gist</c>, which the exclusion constraints require.
    /// </summary>
    [Fact(DisplayName = "KernelDbContext_EnablesBtreeGist")]
    [Trait("Category", "Unit")]
    public void KernelDbContextEnablesBtreeGist()
    {
        using var context = new KernelDbContextDesignTimeFactory().CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

        var designModel = context.GetService<IDesignTimeModel>().Model;

        designModel.GetPostgresExtensions().Select(e => e.Name).Should().Contain("btree_gist");
    }
}
