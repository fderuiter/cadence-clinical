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
/// basic CRUD round-trips, seeding rows directly through <see cref="ExecutionDbContext"/> (matching
/// `SiteComplianceCacheConfigurationTests`' own established round-trip convention) for the read-only cases.
/// <see cref="SiteComplianceRepository.AddAsync"/> and <see cref="SiteComplianceRepository.GetAsync"/>'s tracked-for-mutation contract
/// are MIG-SUBJ-020 additions — this port originally shipped read-only (see this ticket's git history) with
/// the write side explicitly deferred to "a later ticket," which turned out to be MIG-SUBJ-020's
/// eTMF-webhook upsert; their coverage lives in the last two `[Fact]`s below. Uses the InMemory provider (no
/// <c>ComplexProperty</c>/value-converter combination is involved for this aggregate, so InMemory materializes
/// it without the limitation `ConsentRepositoryTests` works around).
/// </summary>
public class SiteComplianceRepositoryTests
{
    private static readonly StudyCode Study = new("STUDY-COMPLIANCE-REPO");
    private static readonly SiteCode Site = new("SITE-COMPLIANCE-REPO");
    private const string Milestone = "SITE_ACTIVATION";

    private static ExecutionDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseInMemoryDatabase(databaseName).Options);

    [Fact]
    public async Task GetAsync_GivenNoMatchingRow_ReturnsNull()
    {
        string databaseName = System.Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new SiteComplianceRepository(context);

        var found = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
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
        var repository = new SiteComplianceRepository(context);

        var found = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.IsComplete.Should().BeTrue();
        found.MissingDocuments.Should().Equal("Form-A");
    }

    [Fact]
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
        var repository = new SiteComplianceRepository(context);

        var found = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
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
        var repository = new SiteComplianceRepository(context);

        var found = await repository.GetAsync(Study, siteCode: null, Milestone, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync()
    {
        string databaseName = System.Guid.NewGuid().ToString();
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "1572" });

        await using (var writeContext = CreateContext(databaseName))
        {
            var writeRepository = new SiteComplianceRepository(writeContext);
            await writeRepository.AddAsync(cache, TestContext.Current.CancellationToken);
            await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateContext(databaseName);
        var readRepository = new SiteComplianceRepository(readContext);
        var found = await readRepository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.Version.Should().Be(1);
        found.MissingDocuments.Should().Equal("1572");
    }

    [Fact]
    public async Task GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate()
    {
        // Regression test for MIG-SUBJ-020's removal of AsNoTracking() from GetAsync: the eTMF-webhook
        // upsert flow loads via GetAsync, calls ApplyWebhookUpdate on the result, and saves through the
        // very same repository instance with no explicit re-attach -- that only works if GetAsync's result
        // is tracked by this repository's own ExecutionDbContext.
        string databaseName = System.Guid.NewGuid().ToString();
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "1572" });

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(cache);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var updateContext = CreateContext(databaseName))
        {
            var repository = new SiteComplianceRepository(updateContext);
            var tracked = await repository.GetAsync(Study, Site, Milestone, TestContext.Current.CancellationToken);
            tracked!.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "CV" });

            await repository.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateContext(databaseName);
        var reloaded = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.Version.Should().Be(2);
        reloaded.IsComplete.Should().BeTrue();
        reloaded.MissingDocuments.Should().Equal("CV");
    }
}
