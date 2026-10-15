using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Bitemporal;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>Kernel database with a test-only bitemporal table guarded exactly as a production one would be.</summary>
public sealed class BitemporalPostgresFixture : KernelPostgresFixture
{
    protected override async ValueTask OnMigratedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        var ddl = ProbeDbContext.CreateTableSql + BitemporalSql.CreateGuards(KernelDbContext.Schema, ProbeDbContext.Table, "subject_key");
        await using var command = new NpgsqlCommand(ddl, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>Checks that a kernel table carries the K-TIME-1 and K-TIME-2 guards.</summary>
internal static class BitemporalGuardAssertions
{
    public static async Task AssertGuardedAsync(DbContext context, string table, CancellationToken ct)
    {
        var constraints = await context.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = {KernelDbContext.Schema} AND t.relname = {table} AND c.contype = 'x' AND c.condeferred AND c.conname = {BitemporalSql.ExclusionConstraintName(table)}")
            .SingleAsync(ct);
        constraints.Should().Be(1, $"{table} needs a deferred K-TIME-2 exclusion constraint");

        var triggers = await context.Database.SqlQuery<string>(
                $"SELECT tg.tgname AS \"Value\" FROM pg_trigger tg JOIN pg_class t ON t.oid = tg.tgrelid JOIN pg_namespace n ON n.oid = t.relnamespace JOIN pg_proc p ON p.oid = tg.tgfoid WHERE n.nspname = {KernelDbContext.Schema} AND t.relname = {table} AND p.proname = {BitemporalSql.AppendOnlyFunctionName}")
            .ToListAsync(ct);
        triggers.Should().BeEquivalentTo(
            [BitemporalSql.RowTriggerName(table), BitemporalSql.TruncateTriggerName(table)],
            $"{table} needs the K-TIME-1 append-only triggers");
    }
}

/// <summary>
/// ADR-2246 database layer against a real PostgreSQL: <c>tstzrange</c> storage, <c>AsOf</c> as range containment,
/// K-TIME-2 single-truth exclusion and K-TIME-1 append-only rows. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BitemporalPostgresTests(BitemporalPostgresFixture fixture) : IClassFixture<BitemporalPostgresFixture>
{
    private static readonly DateTimeOffset TEvent = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TEntry = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TAudit1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TCorrection = new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TAudit2 = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    private ProbeDbContext NewContext()
    {
        fixture.SkipIfUnavailable();
        return ProbeDbContext.Create(fixture.AdminConnectionString!);
    }

    /// <summary>
    /// Validates that the kernel migrations install <c>btree_gist</c> and the shared K-TIME-1 trigger function, and that
    /// the guard helper produces what the kernel-wide guard check expects.
    /// </summary>
    [Fact(DisplayName = "BitemporalFoundation_Migration_InstallsBtreeGist_AndAppendOnlyFunction")]
    public async Task BitemporalFoundationMigrationInstallsBtreeGistAndAppendOnlyFunction()
    {
        await using var context = NewContext();
        var ct = TestContext.Current.CancellationToken;

        (await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = 'btree_gist'").SingleAsync(ct))
            .Should().Be(1);
        (await context.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = {KernelDbContext.Schema} AND p.proname = {BitemporalSql.AppendOnlyFunctionName}")
            .SingleAsync(ct))
            .Should().Be(1);

        await BitemporalGuardAssertions.AssertGuardedAsync(context, ProbeDbContext.Table, ct);
    }

    /// <summary>
    /// Validates that periods round-trip through <c>tstzrange</c> columns, open upper bounds included.
    /// </summary>
    [Fact(DisplayName = "Bitemporal_Periods_RoundTripThroughTstzrange")]
    public async Task BitemporalPeriodsRoundTripThroughTstzrange()
    {
        var ct = TestContext.Current.CancellationToken;
        var fact = new ProbeFact(Guid.NewGuid(), "SYSBP", "140", TemporalPeriod.CreateBounded(TEvent, TAudit1, TEntry));

        await using (var write = NewContext())
        {
            write.Facts.Add(fact);
            await write.SaveChangesAsync(ct);
        }

        await using var read = NewContext();
        var stored = await read.Facts.AsNoTracking().SingleAsync(f => f.Id == fact.Id, ct);
        stored.Period.Should().Be(fact.Period);
        stored.Period.Recorded.IsOpen.Should().BeTrue();
        (await read.Database.SqlQuery<string>($"SELECT recorded_period::text AS \"Value\" FROM kernel.probe_facts WHERE id = {fact.Id}").SingleAsync(ct))
            .Should().EndWith(",)", "an open recorded period is stored with an infinite upper bound");
    }

    /// <summary>
    /// Validates spec §14 correction semantics: a correction closes the old row and inserts the new one in one save,
    /// and <c>AsOf</c> (range containment in SQL) returns the pre-correction value for an earlier <c>knownAt</c>.
    /// </summary>
    [Fact(DisplayName = "Bitemporal_Correction_ClosesOldRow_AndAsOfReconstructsEachKnownAt")]
    public async Task BitemporalCorrectionClosesOldRowAndAsOfReconstructsEachKnownAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var original = new ProbeFact(tenant, "SYSBP", "140", TemporalPeriod.CreateCurrent(TEvent, TEntry));

        await using (var write = NewContext())
        {
            write.Facts.Add(original);
            await write.SaveChangesAsync(ct);
        }

        await using (var correct = NewContext())
        {
            var tracked = await correct.Facts.SingleAsync(f => f.Id == original.Id, ct);
            tracked.Supersede(TCorrection);
            correct.Facts.Add(new ProbeFact(tenant, "SYSBP", "120", TemporalPeriod.CreateCurrent(TEvent, TCorrection)));
            await correct.SaveChangesAsync(ct);
        }

        await using var read = NewContext();
        var facts = read.Facts.AsNoTracking().Where(f => f.TenantId == tenant);

        (await facts.AsOf(TEvent.AddMinutes(5), TAudit1).Select(f => f.Value).ToListAsync(ct)).Should().Equal("140");
        (await facts.AsOf(TEvent.AddMinutes(5), TAudit2).Select(f => f.Value).ToListAsync(ct)).Should().Equal("120");
        (await facts.AsOf(TEvent.AddMinutes(-5), TAudit2).ToListAsync(ct)).Should().BeEmpty("nothing was valid before the event");
        (await facts.AsOfCurrent(TAudit2).Select(f => f.Value).ToListAsync(ct)).Should().Equal("120");
    }

    /// <summary>
    /// Validates K-TIME-2: a second open row for the same tenant and entity key overlapping in valid and recorded time
    /// is rejected by the database, while the same key in another tenant is accepted.
    /// </summary>
    [Fact(DisplayName = "Bitemporal_OverlappingInsert_IsRejectedByExclusionConstraint")]
    public async Task BitemporalOverlappingInsertIsRejectedByExclusionConstraint()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();

        await using (var seed = NewContext())
        {
            seed.Facts.Add(new ProbeFact(tenant, "WEIGHT", "70", TemporalPeriod.CreateCurrent(TEvent, TEntry)));
            seed.Facts.Add(new ProbeFact(Guid.NewGuid(), "WEIGHT", "80", TemporalPeriod.CreateCurrent(TEvent, TEntry)));
            await seed.SaveChangesAsync(ct);
        }

        await using var conflicting = NewContext();
        conflicting.Facts.Add(new ProbeFact(tenant, "WEIGHT", "71", TemporalPeriod.CreateCurrent(TEvent.AddHours(1), TCorrection)));

        await AssertSqlStateAsync(() => conflicting.SaveChangesAsync(ct), PostgresErrorCodes.ExclusionViolation);
    }

    /// <summary>
    /// Validates K-TIME-1: changing a value, reopening or moving a closed recorded period, and deleting or truncating
    /// are all rejected by the database.
    /// </summary>
    [Fact(DisplayName = "Bitemporal_Rows_AreAppendOnly_ExceptClosingRecordedPeriod")]
    public async Task BitemporalRowsAreAppendOnlyExceptClosingRecordedPeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        var open = new ProbeFact(Guid.NewGuid(), "HR", "72", TemporalPeriod.CreateCurrent(TEvent, TEntry));
        var closed = new ProbeFact(Guid.NewGuid(), "HR", "80", new TemporalPeriod(TEvent, null, TEntry, TCorrection));

        await using (var seed = NewContext())
        {
            seed.Facts.AddRange(open, closed);
            await seed.SaveChangesAsync(ct);
        }

        await AssertRejectedAsync(open.Id, f => f.OverwriteValue("75"));
        await AssertRejectedAsync(open.Id, f => f.ReplacePeriod(new TemporalPeriod(TEvent.AddHours(1), null, TEntry, null)));
        await AssertRejectedAsync(open.Id, f => f.ReplacePeriod(new TemporalPeriod(TEvent, null, TEntry.AddHours(1), TCorrection)));
        await AssertRejectedAsync(closed.Id, f => f.ReplacePeriod(TemporalPeriod.CreateCurrent(TEvent, TEntry)));
        await AssertRejectedAsync(closed.Id, f => f.ReplacePeriod(new TemporalPeriod(TEvent, null, TEntry, TAudit2)));

        await using (var delete = NewContext())
        {
            delete.Facts.Remove(await delete.Facts.SingleAsync(f => f.Id == open.Id, ct));
            await AssertSqlStateAsync(() => delete.SaveChangesAsync(ct), BitemporalSql.AppendOnlyViolationSqlState);
        }

        await using (var truncate = NewContext())
        {
            await AssertSqlStateAsync(
                () => truncate.Database.ExecuteSqlRawAsync("TRUNCATE kernel.probe_facts", ct),
                BitemporalSql.AppendOnlyViolationSqlState);
        }

        await using var read = NewContext();
        (await read.Facts.AsNoTracking().SingleAsync(f => f.Id == open.Id, ct)).Value.Should().Be("72");
    }

    private async Task AssertRejectedAsync(Guid id, Action<ProbeFact> change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = NewContext();
        change(await context.Facts.SingleAsync(f => f.Id == id, ct));
        await AssertSqlStateAsync(() => context.SaveChangesAsync(ct), BitemporalSql.AppendOnlyViolationSqlState);
    }

    private static async Task AssertSqlStateAsync(Func<Task> act, string sqlState)
    {
        var thrown = await act.Should().ThrowAsync<Exception>();
        var exception = thrown.Which;
        while (exception is not null and not PostgresException)
        {
            exception = exception.InnerException;
        }

        exception.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(sqlState);
    }
}
