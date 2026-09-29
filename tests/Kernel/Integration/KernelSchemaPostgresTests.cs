using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// Applies the kernel migrations to a real PostgreSQL server and exercises the identity tables through the
/// production wiring (<see cref="KernelPersistenceServiceCollectionExtensions.AddKernelPersistence"/>).
/// Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelSchemaPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Integration test setup";

    /// <summary>
    /// Validates that the kernel migrations apply cleanly, leave nothing pending, and create every table plus the
    /// migrations history inside the <c>kernel</c> schema (ADR-2247).
    /// </summary>
    [Fact(DisplayName = "KernelMigrations_ApplyToPostgres_CreatingEveryTableInKernelSchema")]
    public async Task KernelMigrationsApplyToPostgresCreatingEveryTableInKernelSchema()
    {
        fixture.SkipIfUnavailable();
        await using var context = fixture.CreateContext();

        (await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();

        var tables = await context.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = {KernelDbContext.Schema}")
            .ToListAsync(TestContext.Current.CancellationToken);
        tables.Should().BeEquivalentTo(
            "__ef_migrations_history", "tenants", "organizations", "people", "sites", "studies", "study_sites",
            "audit_log_entries");
    }

    /// <summary>
    /// Validates that writing a tenant → organization → site → study → study-site graph stamps GxP audit fields
    /// from the kernel clock and actor, round-trips strongly-typed IDs, and appends one audit entry per entity.
    /// </summary>
    [Fact(DisplayName = "KernelIdentityGraph_Persists_WithAuditFieldsFromKernelClock_AndAuditEntries")]
    public async Task KernelIdentityGraphPersistsWithAuditFieldsFromKernelClockAndAuditEntries()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = NewGraph();

        await using (var write = fixture.CreateContext())
        {
            write.AddRange(graph.Tenant, graph.Sponsor, graph.Institution, graph.Site, graph.Study, graph.StudySite);
            await write.SaveChangesAsync(ct);
        }

        await using var read = fixture.CreateContext();
        var studySite = await read.StudySites.AsNoTracking().SingleAsync(s => s.Id == graph.StudySite.Id, ct);
        studySite.StudyId.Should().Be(graph.Study.Id);
        studySite.SiteId.Should().Be(graph.Site.Id);
        studySite.TenantId.Should().Be(graph.Tenant.Id);
        studySite.VersionIndex.Should().Be(1);
        studySite.CreatedBy.Should().Be(KernelPostgresFixture.Actor);
        studySite.CreatedAt.Should().Be(fixture.Clock.UtcNow.UtcDateTime);

        var entityIds = new[]
        {
            graph.Tenant.Id.ToString(), graph.Sponsor.Id.ToString(), graph.Institution.Id.ToString(),
            graph.Site.Id.ToString(), graph.Study.Id.ToString(), graph.StudySite.Id.ToString(),
        };
        var audit = await read.AuditLogEntries.AsNoTracking()
            .Where(a => entityIds.Contains(a.EntityId))
            .ToListAsync(ct);
        audit.Should().HaveCount(6).And.OnlyContain(a => a.Action == "Added" && a.UserId == KernelPostgresFixture.Actor);
    }

    /// <summary>
    /// Validates that the database, not only the model, enforces tenant-code uniqueness and study-site foreign keys.
    /// </summary>
    [Fact(DisplayName = "KernelConstraints_RejectDuplicateTenantCode_AndDanglingStudySite")]
    public async Task KernelConstraintsRejectDuplicateTenantCodeAndDanglingStudySite()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = NewGraph();

        await using (var seed = fixture.CreateContext())
        {
            seed.Add(graph.Tenant);
            await seed.SaveChangesAsync(ct);
        }

        await using (var duplicate = fixture.CreateContext())
        {
            duplicate.Add(new Tenant(TenantId.New(), graph.Tenant.Code, "Duplicate", KernelPostgresFixture.Actor, Reason));
            var act = () => duplicate.SaveChangesAsync(ct);
            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        await using (var dangling = fixture.CreateContext())
        {
            dangling.Add(new StudySite(
                StudySiteId.New(), graph.Tenant.Id, StudyId.New(), SiteId.New(), "Selected", KernelPostgresFixture.Actor, Reason));
            var act = () => dangling.SaveChangesAsync(ct);
            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        }
    }

    /// <summary>
    /// Validates optimistic concurrency on <c>VersionIndex</c>: one change advances the version by exactly one, and
    /// of two writers holding the same version the second is rejected instead of silently overwriting the first.
    /// </summary>
    [Fact(DisplayName = "KernelStudySite_ConcurrentUpdates_SecondWriterIsRejected")]
    public async Task KernelStudySiteConcurrentUpdatesSecondWriterIsRejected()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = NewGraph();

        await using (var seed = fixture.CreateContext())
        {
            seed.AddRange(graph.Tenant, graph.Sponsor, graph.Institution, graph.Site, graph.Study, graph.StudySite);
            await seed.SaveChangesAsync(ct);
        }

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var firstCopy = await first.StudySites.SingleAsync(s => s.Id == graph.StudySite.Id, ct);
        var secondCopy = await second.StudySites.SingleAsync(s => s.Id == graph.StudySite.Id, ct);

        firstCopy.Activate("Site initiation visit complete");
        await first.SaveChangesAsync(ct);
        firstCopy.VersionIndex.Should().Be(2, "one change is one version");
        firstCopy.CreatedBy.Should().Be(KernelPostgresFixture.Actor, "an update never rewrites the creator");

        secondCopy.Activate("Concurrent activation");
        var act = () => second.SaveChangesAsync(ct);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private static (Tenant Tenant, Organization Sponsor, Organization Institution, Site Site, Study Study, StudySite StudySite) NewGraph()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string actor = KernelPostgresFixture.Actor;
        var tenant = new Tenant(TenantId.New(), $"T-{suffix}", "Integration tenant", actor, Reason);
        var sponsor = new Organization(OrganizationId.New(), tenant.Id, $"SP-{suffix}", "Sponsor", OrganizationKind.Sponsor, actor, Reason);
        var institution = new Organization(OrganizationId.New(), tenant.Id, $"IN-{suffix}", "Hospital", OrganizationKind.SiteInstitution, actor, Reason);
        var site = new Site(SiteId.New(), tenant.Id, institution.Id, $"S-{suffix}", "Site 101", "US", actor, Reason);
        var study = new Study(StudyId.New(), tenant.Id, sponsor.Id, $"ST-{suffix}", "Integration study", "Phase II", actor, Reason);
        var studySite = new StudySite(StudySiteId.New(), tenant.Id, study.Id, site.Id, "Selected", actor, Reason);
        return (tenant, sponsor, institution, site, study, studySite);
    }
}
