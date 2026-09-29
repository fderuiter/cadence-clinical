using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Domain;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Kernel;

public class TestClinicalEntity
{
    public StudyId Id { get; set; }
    public TenantId TenantId { get; set; }
    public OrganizationId SponsorId { get; set; }
    public ParticipantId ParticipantId { get; set; }
    public SiteId? OptionalSiteId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TestKernelDbContext : DbContext
{
    public DbSet<TestClinicalEntity> Entities => Set<TestClinicalEntity>();

    public TestKernelDbContext(DbContextOptions<TestKernelDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.ConfigureKernelEntityIds();
    }
}

public class KernelEfCoreConfigurationTests
{
    /// <summary>
    /// Validates that ConfigureKernelEntityIds correctly maps strongly-typed UUIDv7 value structs to/from database Guid columns.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "EFCore_ConfigureKernelEntityIds_PersistsAndQueriesCorrectly")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task EfCoreConfigureKernelEntityIdsPersistsAndQueriesCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestKernelDbContext>()
            .UseInMemoryDatabase(databaseName: $"KernelEfCoreTest_{Guid.NewGuid()}")
            .Options;

        var studyId = StudyId.New();
        var tenantId = TenantId.New();
        var sponsorId = OrganizationId.New();
        var participantId = ParticipantId.New();
        var siteId = SiteId.New();

        var entity1 = new TestClinicalEntity
        {
            Id = studyId,
            TenantId = tenantId,
            SponsorId = sponsorId,
            ParticipantId = participantId,
            OptionalSiteId = siteId,
            Name = "Active Protocol 101"
        };

        var otherStudyId = StudyId.New();
        var entity2 = new TestClinicalEntity
        {
            Id = otherStudyId,
            TenantId = tenantId,
            SponsorId = sponsorId,
            ParticipantId = ParticipantId.New(),
            OptionalSiteId = null,
            Name = "Unassigned Protocol 102"
        };

        // Act - Save changes
        await using (var context = new TestKernelDbContext(options))
        {
            context.Entities.AddRange(entity1, entity2);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Assert - Query and verify mapping
        await using (var context = new TestKernelDbContext(options))
        {
            var retrieved1 = await context.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == studyId, TestContext.Current.CancellationToken);

            retrieved1.Should().NotBeNull();
            retrieved1!.Id.Should().Be(studyId);
            retrieved1.TenantId.Should().Be(tenantId);
            retrieved1.SponsorId.Should().Be(sponsorId);
            retrieved1.ParticipantId.Should().Be(participantId);
            retrieved1.OptionalSiteId.Should().Be(siteId);
            retrieved1.Name.Should().Be("Active Protocol 101");

            // Query entity with null optional SiteId
            var retrieved2 = await context.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == otherStudyId, TestContext.Current.CancellationToken);

            retrieved2.Should().NotBeNull();
            retrieved2!.Id.Should().Be(otherStudyId);
            retrieved2.OptionalSiteId.Should().BeNull();

            // Query by predicate on strongly typed ID
            var count = await context.Entities
                .AsNoTracking()
                .CountAsync(e => e.TenantId == tenantId, TestContext.Current.CancellationToken);

            count.Should().Be(2);
        }
    }
}
