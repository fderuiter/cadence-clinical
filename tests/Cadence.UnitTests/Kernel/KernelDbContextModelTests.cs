using System.Linq;

using Cadence.Database;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Testing;
using Cadence.Testing.Hosting;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Model-level checks for <see cref="KernelDbContext"/> that need no running database: schema, table and column
/// naming, and that the checked-in migrations match the model.
/// </summary>
public class KernelDbContextModelTests
{
    private static KernelDbContext CreateContext() =>
        new KernelDbContextDesignTimeFactory().CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

    /// <summary>
    /// Validates that every kernel table lives in the single <c>kernel</c> schema (ADR-2247) with snake_case columns.
    /// </summary>
    [Fact(DisplayName = "KernelDbContext_MapsEveryTable_IntoKernelSchema_WithSnakeCaseColumns")]
    [Trait("Category", "Unit")]
    public void KernelDbContextMapsEveryTableIntoKernelSchemaWithSnakeCaseColumns()
    {
        using var context = CreateContext();
        var entityTypes = context.Model.GetEntityTypes().ToList();

        entityTypes.Select(e => e.GetTableName()).Should().BeEquivalentTo(
            "tenants", "organizations", "people", "sites", "studies", "study_sites",
            "provenance_activities", "domain_events", "audit_entries", "audit_chain_heads", "outbox_messages",
            "idempotency_records", "audit_chain_seals", "tenant_memberships",
            "code_systems", "code_system_versions", "code_system_entries", "concepts", "concept_revisions", "concept_mappings",
            "standard_versions", "standard_datasets", "standard_variables", "standard_value_level_definitions",
            "standard_terminology", "library_items", "study_standards_profiles",
            "protocol_versions", "attestations", "participants", "enrollments", "enrollment_status_periods",
            "lineage_nodes", "lineage_edges", "activity_entries", "artifacts", "artifact_versions", "roles", "grants",
            "grant_periods", "work_queue_definitions", "validation_runs", "validation_run_packs", "validation_run_datasets",
            "validation_run_inputs", "validation_detections", "validation_findings", "effectivities", "effectivity_status_periods",
            "qualifications", "qualification_periods", "qualification_requirements", "qualification_requirement_periods", "proposals", "form_version_snapshots");

        entityTypes.Should().OnlyContain(e => e.GetSchema() == KernelDbContext.Schema);
        entityTypes.SelectMany(e => e.GetProperties())
            .Select(p => p.GetColumnName())
            .Should().OnlyContain(c => c == c.ToLowerInvariant() && !c.Contains(' '));
    }

    /// <summary>
    /// Validates K-ID-1 in the model: every kernel entity except <c>Tenant</c> and <c>Person</c> has a required
    /// <c>TenantId</c>, and every foreign key between two tenant-scoped entities is composite, leading with
    /// <c>TenantId</c> on both sides, so it can never point into another tenant. A plain foreign key to a tenant-scoped
    /// table fails here before any migration is written.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "KernelDbContext_TenantScopedEntities_UseCompositeTenantForeignKeys")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-CRF-013")]
    public void KernelDbContextTenantScopedEntitiesUseCompositeTenantForeignKeys()
    {
        using var context = CreateContext();
        var entityTypes = context.Model.GetEntityTypes().ToList();
        bool IsTenantScoped(IReadOnlyEntityType e) => !TenantIsolationSql.TablesWithoutTenantColumn.Contains(e.GetTableName()!);

        foreach (var entity in entityTypes.Where(IsTenantScoped))
        {
            entity.FindProperty("TenantId").Should().NotBeNull($"{entity.GetTableName()} is tenant-scoped");
            entity.FindProperty("TenantId")!.IsNullable.Should().BeFalse();
        }

        var tenantToTenantKeys = entityTypes.SelectMany(e => e.GetForeignKeys())
            .Where(fk => IsTenantScoped(fk.DeclaringEntityType) && IsTenantScoped(fk.PrincipalEntityType))
            .ToList();
        tenantToTenantKeys.Should().NotBeEmpty();
        tenantToTenantKeys.Should().OnlyContain(
            fk => fk.Properties[0].Name == "TenantId" && fk.PrincipalKey.Properties[0].Name == "TenantId",
            "every reference between tenant-scoped tables is (tenant_id, x_id) → (tenant_id, id)");
    }

    /// <summary>
    /// Validates that strongly-typed kernel IDs are stored as <c>uuid</c> columns.
    /// </summary>
    [Fact(DisplayName = "KernelDbContext_StoresStronglyTypedIds_AsUuid")]
    [Trait("Category", "Unit")]
    public void KernelDbContextStoresStronglyTypedIdsAsUuid()
    {
        using var context = CreateContext();
        var studySite = context.Model.FindEntityType(typeof(Cadence.Kernel.Domain.Entities.StudySite))!;

        foreach (var name in new[] { "Id", "TenantId", "StudyId", "SiteId" })
        {
            studySite.FindProperty(name)!.GetColumnType().Should().Be("uuid", $"{name} is a kernel ID");
        }
    }

    /// <summary>
    /// Validates that the checked-in kernel migrations are in sync with the model (no pending model changes).
    /// </summary>
    [Fact(DisplayName = "KernelDbContext_Migrations_MatchModel")]
    [Trait("Category", "Unit")]
    public void KernelDbContextMigrationsMatchModel()
    {
        using var context = CreateContext();

        context.Database.GetMigrations().Should().NotBeEmpty();
        context.Database.HasPendingModelChanges().Should().BeFalse(
            "run `scripts/manage_migrations.sh add kernel <Name>` after changing the kernel model");
    }

    /// <summary>
    /// Validates the snake_case column-name conversion, including acronyms.
    /// </summary>
    [Theory(DisplayName = "KernelDbContext_ToSnakeCase_ConvertsPropertyNames")]
    [InlineData("Id", "id")]
    [InlineData("TenantId", "tenant_id")]
    [InlineData("ReasonForChange", "reason_for_change")]
    [InlineData("UsdmPayloadJson", "usdm_payload_json")]
    [InlineData("SDTMDomain", "sdtm_domain")]
    [Trait("Category", "Unit")]
    public void KernelDbContextToSnakeCaseConvertsPropertyNames(string input, string expected)
    {
        KernelDbContext.ToSnakeCase(input).Should().Be(expected);
    }
}
