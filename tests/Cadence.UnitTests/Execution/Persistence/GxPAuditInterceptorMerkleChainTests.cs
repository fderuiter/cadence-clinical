using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

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
    }

    private static MerkleChainTestDbContext CreateContext(string databaseName, IClock? clock = null) =>
        new(new DbContextOptionsBuilder<MerkleChainTestDbContext>()
            .UseInMemoryDatabase(databaseName)
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
    public async Task SaveChangesAsync_GivenSameMicrosecondSequentialSaves_OrdersByTimestampDeterministically()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await using var context = CreateContext(Guid.NewGuid().ToString(), clock);
        var aggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");

        context.GuidAggregates.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        aggregate.Name = "Beta";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entries = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(GuidKeyedAggregate))
            .OrderBy(e => e.Timestamp)
            .ToListAsync(TestContext.Current.CancellationToken);

        entries.Should().HaveCount(2);
        entries[0].PredecessorHash.Should().BeEmpty();
        entries[0].Action.Should().Be("Added");
        entries[1].Action.Should().Be("Modified");
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash);
        entries[1].Timestamp.Should().BeAfter(entries[0].Timestamp);
    }
}
