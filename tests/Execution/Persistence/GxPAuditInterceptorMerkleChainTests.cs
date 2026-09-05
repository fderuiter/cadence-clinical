using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-007`'s <see cref="GxPAuditInterceptor"/> generalization: field stamping for
/// non-`string`-`TId` aggregates and the per-entity-type Merkle chain write path.
/// @req:PRD-SYS-053
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

    private static MerkleChainTestDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<MerkleChainTestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => "qa.reviewer@site.org"))
            .Options);

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
    public async Task SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var aggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");
        context.GuidAggregates.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Task.Delay(5, TestContext.Current.CancellationToken);
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
    [GxPRequirement("PRD-SYS-053")]
    public async Task SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var guidAggregate = new GuidKeyedAggregate(Guid.NewGuid(), "Alpha");
        var stringAggregate = new TestSubject(Guid.NewGuid().ToString(), "SITE-A");
        context.GuidAggregates.Add(guidAggregate);
        context.StringAggregates.Add(stringAggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

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
    [GxPRequirement("PRD-SYS-053")]
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
}
