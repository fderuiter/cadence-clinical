using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-013`'s <see cref="SiteComplianceRepository"/>: <see cref="SiteComplianceRepository.GetAsync"/>
/// basic CRUD round-trips. The repository itself exposes no write method (the ticket names only
/// <c>GetAsync</c> for this port — a later ticket owns the eTMF-webhook write path), so rows are seeded
/// directly through <see cref="ExecutionDbContext"/>, matching `SiteComplianceCacheConfigurationTests`' own
/// established round-trip convention. Uses the InMemory provider (no <c>ComplexProperty</c>/value-converter
/// combination is involved for this aggregate, so InMemory materializes it without the limitation
/// `ConsentRepositoryTests` works around).
/// @req:PRD-SUB-001
/// </summary>
public class SiteComplianceRepositoryTests
{
    private static readonly StudyCode Study = new("STUDY-COMPLIANCE-REPO");
    private static readonly SiteCode Site = new("SITE-COMPLIANCE-REPO");
    private const string Milestone = "SITE_ACTIVATION";

    private static ExecutionDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseInMemoryDatabase(databaseName).Options);

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetAsync_GivenNoMatchingRow_ReturnsNull()
    {
        string databaseName = System.Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create());

        var found = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow()
    {
        string databaseName = System.Guid.NewGuid().ToString();
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: true, new[] { "Form-A" });

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(cache);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(databaseName);
        var repository = new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create());

        var found = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.IsComplete.Should().BeTrue();
        found.MissingDocuments.Should().Equal("Form-A");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetAsync_GivenRowForADifferentMilestone_ReturnsNull()
    {
        string databaseName = System.Guid.NewGuid().ToString();
        var cache = SiteComplianceCache.Create(Study, Site, "OTHER_MILESTONE", isComplete: true, new[] { "Form-A" });

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(cache);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(databaseName);
        var repository = new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create());

        var found = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone()
    {
        // A ClinicalSubject may legally have no assigned site; SiteComplianceCache.SiteCode never is null.
        // Querying with a null site can therefore never match any row -- a subject with no site cannot
        // satisfy a site-specific compliance gate (see ISiteComplianceRepository.GetAsync's doc comment).
        string databaseName = System.Guid.NewGuid().ToString();
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: true, new[] { "Form-A" });

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(cache);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(databaseName);
        var repository = new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create());

        var found = await repository.GetAsync(Study, siteCode: null, Milestone, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }
}
