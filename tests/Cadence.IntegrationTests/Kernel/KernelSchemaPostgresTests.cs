using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Applies the kernel migrations to a real PostgreSQL server and checks the schema itself: tables, bitemporal guards,
/// database constraints and the K-TX-2 write guard. Commands are covered by <see cref="KernelCommandPipelinePostgresTests"/>.
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
        await using var context = fixture.CreateAdminContext();

        (await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();

        var tables = await context.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = {KernelDbContext.Schema}")
            .ToListAsync(TestContext.Current.CancellationToken);
        tables.Should().BeEquivalentTo(
            "__ef_migrations_history", "tenants", "organizations", "people", "sites", "studies", "study_sites",
            "provenance_activities", "domain_events", "audit_entries", "audit_chain_heads", "outbox_messages",
            "idempotency_records", "audit_chain_seals", "tenant_memberships",
            "code_systems", "code_system_versions", "code_system_entries", "concepts", "concept_revisions", "concept_mappings",
            "standard_versions", "standard_datasets", "standard_variables", "standard_value_level_definitions",
            "standard_terminology", "library_items", "study_standards_profiles",
            "protocol_versions", "attestations", "participants", "enrollments", "enrollment_status_periods",
            "lineage_nodes", "lineage_edges", "activity_entries", "artifacts", "artifact_versions", "roles", "grants",
            "grant_periods", "work_queue_definitions", "validation_runs", "validation_run_packs", "validation_run_datasets",
            "validation_run_inputs", "validation_detections", "validation_findings", "effectivities", "effectivity_status_periods",
            "qualifications", "qualification_periods", "qualification_requirements", "qualification_requirement_periods", "proposals",
            "form_version_snapshots");
    }

    /// <summary>
    /// Validates that every bitemporal table the kernel maps carries the K-TIME-2 exclusion constraint and the K-TIME-1
    /// append-only triggers, so a migration that forgets <c>CreateBitemporalGuards</c> fails here.
    /// </summary>
    [Fact(DisplayName = "KernelBitemporalTables_AllCarryExclusionConstraint_AndAppendOnlyTriggers")]
    public async Task KernelBitemporalTablesAllCarryExclusionConstraintAndAppendOnlyTriggers()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var context = fixture.CreateAdminContext();

        var bitemporalTables = context.Model.GetEntityTypes()
            .Where(e => typeof(IBitemporalEntity).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName()!)
            .ToList();

        foreach (var table in bitemporalTables)
        {
            await BitemporalGuardAssertions.AssertGuardedAsync(context, table, ct);
        }
    }

    /// <summary>
    /// Validates that a tenant → organization → site → study → study-site graph round-trips strongly-typed IDs and
    /// foreign keys through PostgreSQL.
    /// </summary>
    [Fact(DisplayName = "KernelIdentityGraph_Persists_WithTypedIdsAndForeignKeys")]
    public async Task KernelIdentityGraphPersistsWithTypedIdsAndForeignKeys()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = NewGraph();

        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Institution, graph.Site, graph.Study, graph.StudySite);

        await using var read = fixture.CreateAdminContext();
        var studySite = await read.StudySites.AsNoTracking().SingleAsync(s => s.Id == graph.StudySite.Id, ct);
        studySite.StudyId.Should().Be(graph.Study.Id);
        studySite.SiteId.Should().Be(graph.Site.Id);
        studySite.TenantId.Should().Be(graph.Tenant.Id);
        studySite.CreatedBy.Should().Be(KernelPostgresFixture.ActorId);
    }

    /// <summary>
    /// Validates the K-TX-2 write guard: a kernel context refuses <c>SaveChanges</c> outside the command pipeline, so
    /// no code path can change kernel state without provenance, an event and an audit link.
    /// </summary>
    [Fact(DisplayName = "KernelDbContext_SaveOutsidePipeline_IsRefused")]
    public async Task KernelDbContextSaveOutsidePipelineIsRefused()
    {
        fixture.SkipIfUnavailable();
        await using var context = fixture.CreateAdminContext();
        context.Add(NewGraph().Tenant);

        var act = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("K-TX-2*");
    }

    /// <summary>
    /// Validates that the database, not only the model, enforces tenant-code uniqueness and study-site foreign keys.
    /// </summary>
    [Fact(DisplayName = "KernelConstraints_RejectDuplicateTenantCode_AndDanglingStudySite")]
    public async Task KernelConstraintsRejectDuplicateTenantCodeAndDanglingStudySite()
    {
        fixture.SkipIfUnavailable();
        var graph = NewGraph();

        await fixture.SeedAsync(graph.Tenant);

        var duplicate = () => fixture.SeedAsync(
            new Tenant(TenantId.New(), graph.Tenant.Code, "Duplicate", KernelPostgresFixture.ActorId, Reason));
        (await duplicate.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

        var dangling = () => fixture.SeedAsync(new StudySite(
            StudySiteId.New(), graph.Tenant.Id, StudyId.New(), SiteId.New(), "Selected", KernelPostgresFixture.ActorId, Reason));
        (await dangling.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    internal static (Tenant Tenant, Organization Sponsor, Organization Institution, Site Site, Study Study, StudySite StudySite) NewGraph()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string actor = KernelPostgresFixture.ActorId;
        var tenant = new Tenant(TenantId.New(), $"T-{suffix}", "Integration tenant", actor, Reason);
        var sponsor = new Organization(OrganizationId.New(), tenant.Id, $"SP-{suffix}", "Sponsor", OrganizationKind.Sponsor, actor, Reason);
        var institution = new Organization(OrganizationId.New(), tenant.Id, $"IN-{suffix}", "Hospital", OrganizationKind.SiteInstitution, actor, Reason);
        var site = new Site(SiteId.New(), tenant.Id, institution.Id, $"S-{suffix}", "Site 101", "US", actor, Reason);
        var study = new Study(StudyId.New(), tenant.Id, sponsor.Id, $"ST-{suffix}", "Integration study", "Phase II", actor, Reason);
        var studySite = new StudySite(StudySiteId.New(), tenant.Id, study.Id, site.Id, "Selected", actor, Reason);
        return (tenant, sponsor, institution, site, study, studySite);
    }
}
