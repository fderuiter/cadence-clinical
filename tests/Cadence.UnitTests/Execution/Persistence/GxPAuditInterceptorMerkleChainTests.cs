using System.Data.Common;

using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Configurations;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

internal sealed class AuditTipCommandRecorder : DbCommandInterceptor
{
    public sealed record CapturedCommand(string CommandText, IReadOnlyList<(string Name, object? Value)> Parameters);

    public List<CapturedCommand> Commands { get; } = [];

    public void Clear() => Commands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<(string Name, object? Value)>(command.Parameters.Count);
        foreach (DbParameter parameter in command.Parameters)
        {
            parameters.Add((parameter.ParameterName, parameter.Value));
        }

        Commands.Add(new CapturedCommand(command.CommandText, parameters));
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public static async Task<List<string>> ExplainSqlitePlanAsync(SqliteConnection connection, CapturedCommand captured, CancellationToken ct)
    {
        await using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + captured.CommandText;
        foreach ((string name, object? value) in captured.Parameters)
        {
            explain.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var details = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            details.Add(reader.GetString(3));
        }

        return details;
    }

    public static void AssertSqlitePlanUsesOnlyIndexSeeks(
        IReadOnlyCollection<string> planDetails,
        string sequenceIndexName,
        string predecessorIndexName)
    {
        planDetails.Should().NotBeEmpty();
        var coroutineScans = planDetails
            .Where(d => d.StartsWith("CO-ROUTINE ", StringComparison.Ordinal))
            .Select(d => "SCAN " + d["CO-ROUTINE ".Length..])
            .ToHashSet(StringComparer.Ordinal);

        planDetails.Where(d => d.StartsWith("SCAN ", StringComparison.Ordinal))
            .Should().OnlyContain(
                d => coroutineScans.Contains(d),
                "only 1-row UNION ALL co-routines may be scanned; every physical table access must be an index SEARCH");
        planDetails.Should().NotContain(
            d => d.Contains("USE TEMP B-TREE", StringComparison.OrdinalIgnoreCase),
            "descending tip order must be satisfied directly by the composite sequence index");
        planDetails.Should().Contain(d => d.Contains(sequenceIndexName, StringComparison.Ordinal));
        planDetails.Should().Contain(d => d.Contains(predecessorIndexName, StringComparison.Ordinal));
    }
}

/// <summary>
/// Coverage for `MIG-SUBJ-007`'s <see cref="GxPAuditInterceptor"/> generalization: field stamping for
/// non-`string`-`TId` aggregates and the per-entity-type Merkle chain write path.
/// </summary>
public class GxPAuditInterceptorMerkleChainTests
{
    private sealed class GuidKeyedAggregate : AggregateRoot<Guid>
    {
        public string Name { get; set; } = string.Empty;

        private GuidKeyedAggregate() { }

        public GuidKeyedAggregate(Guid id, string name) : base(id)
        {
            Name = name;
        }
    }

    private sealed class RawAuditableEntity : IGxPAuditable
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string ReasonForChange { get; set; } = string.Empty;
        public long VersionIndex { get; set; }
    }

    private sealed class MerkleChainTestDbContext(DbContextOptions<MerkleChainTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<GuidKeyedAggregate> GuidAggregates => Set<GuidKeyedAggregate>();
        public DbSet<TestSubject> StringAggregates => Set<TestSubject>();
        public DbSet<RawAuditableEntity> RawEntities => Set<RawAuditableEntity>();
        public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        }
    }

    private static MerkleChainTestDbContext CreateContext(string databaseName, IClock? clock = null) =>
        new(new DbContextOptionsBuilder<MerkleChainTestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(TestSupport.TestEfServiceProviders.InMemory)
            .AddInterceptors(new GxPAuditInterceptor(() => "qa.reviewer@site.org", clock ?? SystemClock.Instance))
            .Options);

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var aggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");

        context.GuidAggregates.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        aggregate.CreatedAt.Should().NotBe(default);
        aggregate.CreatedBy.Should().Be("qa.reviewer@site.org");
        aggregate.VersionIndex.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var aggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha") { CreatedBy = "original.author@site.org" };
        context.GuidAggregates.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        aggregate.Name = "Beta";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        aggregate.CreatedBy.Should().Be("original.author@site.org");
        aggregate.VersionIndex.Should().Be(2);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-003")]
    public async Task SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType()
    {
        var clock = new FakeClock();
        await using var context = CreateContext(Guid.NewGuid().ToString(), clock);
        var aggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");
        context.GuidAggregates.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(1));
        aggregate.Name = "Beta";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entries = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(GuidKeyedAggregate))
            .OrderBy(e => e.Timestamp)
            .ToListAsync(TestContext.Current.CancellationToken);

        entries.Should().HaveCount(2);
        entries[0].PredecessorHash.Should().BeEmpty();
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash);
        entries[1].BlockHash.Should().NotBe(entries[0].BlockHash);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-003")]
    public async Task SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently()
    {
        var clock = new FakeClock();
        await using var context = CreateContext(Guid.NewGuid().ToString(), clock);
        var guidAggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");
        var stringAggregate = new TestSubject(Guid.NewGuid().ToString(), "SITE-A");
        context.GuidAggregates.Add(guidAggregate);
        context.StringAggregates.Add(stringAggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(1));
        guidAggregate.Name = "Beta";
        stringAggregate.Status = "ENROLLED";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var guidEntries = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(GuidKeyedAggregate))
            .OrderBy(e => e.Timestamp)
            .ToListAsync(TestContext.Current.CancellationToken);
        var stringEntries = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(TestSubject))
            .OrderBy(e => e.Timestamp)
            .ToListAsync(TestContext.Current.CancellationToken);

        guidEntries.Should().HaveCount(2);
        stringEntries.Should().HaveCount(2);
        guidEntries[1].PredecessorHash.Should().Be(guidEntries[0].BlockHash);
        stringEntries[1].PredecessorHash.Should().Be(stringEntries[0].BlockHash);
        guidEntries[1].PredecessorHash.Should().NotBe(stringEntries[0].BlockHash);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var entity = new RawAuditableEntity();

        context.RawEntities.Add(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity.CreatedAt.Should().Be(default);
        entity.CreatedBy.Should().BeEmpty();
        entity.VersionIndex.Should().Be(0);

        var written = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(RawAuditableEntity))
            .ToListAsync(TestContext.Current.CancellationToken);
        written.Should().HaveCount(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-003")]
    public async Task SaveChangesAsync_GivenSequentialSavesWithIdenticalTimestamps_ProducesValidMerkleChain()
    {
        string dbName = Guid.NewGuid().ToString();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        var aggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");

        await using (var context1 = CreateContext(dbName, clock))
        {
            context1.GuidAggregates.Add(aggregate);
            await context1.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context2 = CreateContext(dbName, clock))
        {
            var tracked = await context2.GuidAggregates.SingleAsync(a => a.Id == aggregate.Id, TestContext.Current.CancellationToken);
            tracked.Name = "Beta";
            await context2.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context3 = CreateContext(dbName, clock))
        {
            var tracked = await context3.GuidAggregates.SingleAsync(a => a.Id == aggregate.Id, TestContext.Current.CancellationToken);
            tracked.Name = "Gamma";
            await context3.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context4 = CreateContext(dbName, clock))
        {
            var tracked = await context4.GuidAggregates.SingleAsync(a => a.Id == aggregate.Id, TestContext.Current.CancellationToken);
            tracked.Name = "Delta";
            await context4.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verifyContext = CreateContext(dbName);
        var entries = await verifyContext.AuditLogEntries
            .Where(e => e.EntityName == nameof(GuidKeyedAggregate))
            .OrderBy(e => e.VersionIndex)
            .ToListAsync(TestContext.Current.CancellationToken);

        entries.Should().HaveCount(4);
        entries[0].PredecessorHash.Should().BeEmpty();
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash);
        entries[2].PredecessorHash.Should().Be(entries[1].BlockHash);
        entries[3].PredecessorHash.Should().Be(entries[2].BlockHash);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string expectedPredecessor = i == 0 ? string.Empty : entries[i - 1].BlockHash;
            e.PredecessorHash.Should().Be(expectedPredecessor);

            string computedHash = AuditLogEntry.ComputeBlockHash(
                e.EntityName, e.EntityId, e.Action, e.Timestamp, e.UserId, e.VersionIndex, e.ChangesJson, e.PredecessorHash);
            e.BlockHash.Should().Be(computedHash);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void SaveChanges_Synchronously_WithAnAuditedChange_FailsInsteadOfSkippingTheAudit()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        context.GuidAggregates.Add(new GuidKeyedAggregate(Guid.NewGuid(), "sync"));

        Action save = () => context.SaveChanges();

        save.Should().Throw<InvalidOperationException>().WithMessage("*SaveChangesAsync*");
        context.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-003")]
    public async Task SaveChangesAsync_GivenSharedAuditConfigurationOnSqlite_PrefetchesThreeChainsInOneIndexedQueryAndChainsSameTypeInMemory()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 2, 1, 8, 30, 0, TimeSpan.Zero));
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var recorder = new AuditTipCommandRecorder();
        MerkleChainTestDbContext CreateSqliteContext() =>
            new(new DbContextOptionsBuilder<MerkleChainTestDbContext>()
                .UseSqlite(connection)
                .UseInternalServiceProvider(TestSupport.TestEfServiceProviders.Sqlite)
                .AddInterceptors(recorder, new GxPAuditInterceptor(() => "qa.reviewer@site.org", clock))
                .Options);

        await using (var setup = CreateSqliteContext())
        {
            await setup.Database.EnsureCreatedAsync(ct);
        }

        Guid firstGuidId = Guid.NewGuid();
        string firstStringId = Guid.NewGuid().ToString();

        await using (var seed = CreateSqliteContext())
        {
            seed.GuidAggregates.Add(new GuidKeyedAggregate(firstGuidId, "Seed-A"));
            seed.StringAggregates.Add(new TestSubject(firstStringId, "SITE-1"));
            await seed.SaveChangesAsync(ct);
        }

        clock.Advance(TimeSpan.FromSeconds(5));

        await using (var batch = CreateSqliteContext())
        {
            var existingGuid = await batch.GuidAggregates.SingleAsync(a => a.Id == firstGuidId, ct);
            var existingString = await batch.StringAggregates.SingleAsync(a => a.Id == firstStringId, ct);
            existingGuid.Name = "Updated-A";
            existingString.Status = "ENROLLED";

            batch.GuidAggregates.Add(new GuidKeyedAggregate(Guid.NewGuid(), "Added-B"));
            batch.RawEntities.Add(new RawAuditableEntity());
            AuditActions.Record(batch, new AuditAction(
                nameof(GuidKeyedAggregate),
                Guid.NewGuid().ToString(),
                "AUDIT_ACTION_NOTE",
                "auditor@site.org",
                "Action-only event on same chain",
                "{\"note\":\"verified\"}"));

            recorder.Clear();
            await batch.SaveChangesAsync(ct);

            var tipReads = recorder.Commands
                .Where(c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                    && c.CommandText.Contains("audit_log_entries", StringComparison.Ordinal))
                .ToList();
            tipReads.Should().ContainSingle("three distinct EntityName chains (two persisted, one genesis, plus in-memory chaining and AuditActions) must issue one batched SELECT");

            var planDetails = await AuditTipCommandRecorder.ExplainSqlitePlanAsync(connection, tipReads[0], ct);
            AuditTipCommandRecorder.AssertSqlitePlanUsesOnlyIndexSeeks(
                planDetails,
                "IX_audit_log_entries_EntityName_Timestamp_VersionIndex_Id",
                "IX_audit_log_entries_EntityName_PredecessorHash");
        }

        await using var verify = CreateSqliteContext();
        var entries = await verify.AuditLogEntries.AsNoTracking().ToListAsync(ct);
        entries.Should().HaveCount(7);

        var results = AuditLogChainVerifier.Verify(entries);
        results.Should().HaveCount(3).And.OnlyContain(r => r.Valid);
        results.Single(r => r.EntityName == nameof(GuidKeyedAggregate)).VerifiedCount.Should().Be(4,
            "the persisted GuidKeyedAggregate entry, two GuidKeyedAggregate mutations, and unmatched AuditAction must form one unbroken 4-block chain");
    }
}
