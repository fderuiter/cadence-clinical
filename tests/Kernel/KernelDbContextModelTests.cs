using System.Linq;

using Cadence.Database;
using Cadence.Kernel.Persistence;
using Cadence.Testing;
using Cadence.Testing.Hosting;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

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
            "idempotency_records", "audit_chain_seals");
        entityTypes.Should().OnlyContain(e => e.GetSchema() == KernelDbContext.Schema);
        entityTypes.SelectMany(e => e.GetProperties())
            .Select(p => p.GetColumnName())
            .Should().OnlyContain(c => c == c.ToLowerInvariant() && !c.Contains(' '));
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
