using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Tenant isolation enforced by PostgreSQL (K-ID-1, K-ID-2, K-ID-4, ADR-2250; spec §14 scenario 5), checked as the
/// application role: not the owner, no <c>BYPASSRLS</c>. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelTenantIsolationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Tenant isolation test";

    /// <summary>
    /// Validates the schema-wide rule: every kernel table has Row-Level Security enabled and forced with at least one
    /// policy, and every table except <c>tenants</c> and <c>people</c> has a non-null <c>tenant_id</c>. A new table
    /// that a migration leaves unprotected fails here.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "EveryKernelTable_HasTenantIdAndForcedRowLevelSecurity")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task EveryKernelTableHasTenantIdAndForcedRowLevelSecurity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateAdminContext();

        var tables = await db.Database.SqlQuery<TableSecurity>(
                $"""
                SELECT c.relname AS "Table", c.relrowsecurity AS "Enabled", c.relforcerowsecurity AS "Forced",
                       (SELECT count(*)::int FROM pg_policy p WHERE p.polrelid = c.oid) AS "Policies",
                       EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'tenant_id'
                               AND a.attnotnull AND NOT a.attisdropped) AS "HasTenantId"
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = {KernelDbContext.Schema} AND c.relkind IN ('r', 'p')
                  AND c.relname <> {KernelDatabase.MigrationsHistoryTable}
                """)
            .ToListAsync(ct);

        tables.Select(t => t.Table).Should().BeEquivalentTo(db.Model.GetEntityTypes().Select(e => e.GetTableName()));
        foreach (var table in tables)
        {
            table.Enabled.Should().BeTrue($"{table.Table} needs row-level security");
            table.Forced.Should().BeTrue($"{table.Table} needs FORCE ROW LEVEL SECURITY so the owner is bound too");
            table.Policies.Should().BePositive($"{table.Table} needs a tenant policy");
            table.HasTenantId.Should().Be(
                !TenantIsolationSql.TablesWithoutTenantColumn.Contains(table.Table),
                $"{table.Table} must carry tenant_id NOT NULL unless it is tenants or people");
        }
    }

    /// <summary>
    /// Validates that the application role cannot escape the policies: it is not a superuser, has no
    /// <c>BYPASSRLS</c>, owns no kernel table, cannot update the insert-only tables and cannot delete anywhere.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "ApplicationRole_IsNotOwner_AndCannotBypassRowLevelSecurity")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task ApplicationRoleIsNotOwnerAndCannotBypassRowLevelSecurity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        (await AppScalarAsync<bool>("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", null, ct))
            .Should().BeFalse();
        (await AppScalarAsync<long>(
                "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'kernel' AND pg_has_role(current_user, c.relowner, 'MEMBER')",
                null,
                ct))
            .Should().Be(0);

        foreach (var table in InsertOnlySql.Tables)
        {
            (await AppScalarAsync<bool>($"SELECT has_table_privilege('kernel.{table}', 'UPDATE')", null, ct))
                .Should().BeFalse($"{table} is insert-only");
        }

        (await AppScalarAsync<long>(
                "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'kernel' AND c.relkind = 'r' AND has_table_privilege(c.oid, 'DELETE')",
                null,
                ct))
            .Should().Be(0);
    }

    /// <summary>
    /// Validates spec §14 scenario 5, reads: a context scoped to tenant A sees only A's tenant, organizations and
    /// studies, even when it asks for B's rows by id, and an unfiltered SQL count sees only A's rows.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "CrossTenantRead_ReturnsNothing")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task CrossTenantReadReturnsNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var a = KernelSchemaPostgresTests.NewGraph();
        var b = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(a.Tenant, a.Sponsor, a.Institution, a.Site, a.Study, a.StudySite);
        await fixture.SeedAsync(b.Tenant, b.Sponsor, b.Institution, b.Site, b.Study, b.StudySite);

        await using var db = fixture.CreateContext(a.Tenant.Id);

        (await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct)).Should().Equal(a.Tenant.Id);
        (await db.Organizations.AsNoTracking().Select(o => o.TenantId).Distinct().ToListAsync(ct)).Should().Equal(a.Tenant.Id);
        (await db.Studies.AsNoTracking().AnyAsync(s => s.Id == b.Study.Id, ct)).Should().BeFalse();
        (await db.StudySites.AsNoTracking().AnyAsync(s => s.TenantId == b.Tenant.Id, ct)).Should().BeFalse();
        (await AppScalarAsync<long>("SELECT count(*) FROM kernel.sites", a.Tenant.Id, ct)).Should().Be(1);
    }

    /// <summary>
    /// Validates spec §14 scenario 5, writes: a row in tenant A that names tenant B's organization fails the composite
    /// foreign key, and a row stamped with tenant B while connected as tenant A fails the policy check, both inside
    /// PostgreSQL.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "CrossTenantForeignKey_AndForeignTenantRow_AreRejectedByTheDatabase")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task CrossTenantForeignKeyAndForeignTenantRowAreRejectedByTheDatabase()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var a = KernelSchemaPostgresTests.NewGraph();
        var b = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(a.Tenant, a.Sponsor);
        await fixture.SeedAsync(b.Tenant, b.Sponsor, b.Institution);

        var crossReference = new Site(
            SiteId.New(), a.Tenant.Id, b.Institution.Id, "S-X", "Borrowed institution", "US", KernelPostgresFixture.ActorId, Reason);
        (await InsertAsTenantAsync(a.Tenant.Id, crossReference, ct)).SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        var foreignRow = new Organization(
            OrganizationId.New(), b.Tenant.Id, "ORG-X", "Planted", OrganizationKind.Vendor, KernelPostgresFixture.ActorId, Reason);
        (await InsertAsTenantAsync(a.Tenant.Id, foreignRow, ct)).SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>
    /// Validates that a kernel context with no tenant cannot open a connection, and that a raw connection as the
    /// application role with no tenant set sees no rows and can insert none.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "NoTenant_CannotConnect_AndSeesNothing")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task NoTenantCannotConnectAndSeesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var a = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(a.Tenant, a.Sponsor);

        await using (var unbound = fixture.CreateUnboundContext())
        {
            var read = () => unbound.Organizations.AsNoTracking().ToListAsync(ct);
            (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage("K-ID-2*");
        }

        (await AppScalarAsync<long>("SELECT count(*) FROM kernel.organizations", null, ct)).Should().Be(0);
        (await AppScalarAsync<long>("SELECT count(*) FROM kernel.tenants", null, ct)).Should().Be(0);
        var insert = () => AppScalarAsync<long>(
            $"INSERT INTO kernel.idempotency_records (tenant_id, idempotency_key, command_type, request_hash, result_json, activity_id, recorded_at) VALUES ('{a.Tenant.Id}', 'k', 't', 'h', '{{}}', gen_random_uuid(), now()) RETURNING 1",
            null,
            ct);
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>
    /// Validates K-ID-4: a person has one tenant-independent identity, and a tenant sees that person only through a
    /// membership of its own.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Person_IsVisibleOnlyToTenantsWithAMembership")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task PersonIsVisibleOnlyToTenantsWithAMembership()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var a = KernelSchemaPostgresTests.NewGraph();
        var b = KernelSchemaPostgresTests.NewGraph();
        var person = new Person(PersonId.New(), KernelPostgresFixture.ActorId, Reason);
        await fixture.SeedAsync(a.Tenant, b.Tenant, person);
        await fixture.SeedAsync(new TenantMembership(
            TenantMembershipId.New(), a.Tenant.Id, person.Id, KernelPostgresFixture.ActorId, Reason));

        await using (var member = fixture.CreateContext(a.Tenant.Id))
        {
            (await member.People.AsNoTracking().AnyAsync(p => p.Id == person.Id, ct)).Should().BeTrue();
            (await member.TenantMemberships.AsNoTracking().CountAsync(ct)).Should().Be(1);
        }

        await using var stranger = fixture.CreateContext(b.Tenant.Id);
        (await stranger.People.AsNoTracking().AnyAsync(p => p.Id == person.Id, ct)).Should().BeFalse();
        (await stranger.TenantMemberships.AsNoTracking().AnyAsync(ct)).Should().BeFalse();
    }

    /// <summary>
    /// Validates the tenant directory: system jobs can list every tenant id through the directory function, while
    /// the application role can neither read the tenants table unscoped nor switch the directory on for itself.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "TenantDirectory_ListsIds_OnlyThroughTheDirectoryFunction")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task TenantDirectoryListsIdsOnlyThroughTheDirectoryFunction()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var a = KernelSchemaPostgresTests.NewGraph();
        var b = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(a.Tenant, b.Tenant);

        var tenants = await fixture.TenantContexts.ListTenantsAsync(ct);

        tenants.Should().Contain([a.Tenant.Id, b.Tenant.Id]);
        (await AppScalarAsync<long>(
                $"SELECT count(*) FROM (SELECT set_config('{TenantIsolationSql.DirectorySetting}', 'on', false)) s, kernel.tenants",
                null,
                ct))
            .Should().Be(0);
    }

    /// <summary>
    /// Validates that the command pipeline runs under the policies end to end: a command for tenant A commits as the
    /// application role, and its provenance, audit link and outbox row are visible to tenant A only.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "PipelineCommand_CommitsUnderRowLevelSecurity_VisibleToItsTenantOnly")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task PipelineCommandCommitsUnderRowLevelSecurityVisibleToItsTenantOnly()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var a = new ProvisionTenant(TenantId.New(), $"T-{Guid.NewGuid():N}"[..20], "Tenant A", $"key-{Guid.NewGuid():N}", Reason);
        var b = new ProvisionTenant(TenantId.New(), $"T-{Guid.NewGuid():N}"[..20], "Tenant B", $"key-{Guid.NewGuid():N}", Reason);

        (await fixture.Sender.SendAsync(a, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(b, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        await using var db = fixture.CreateContext(a.TenantId);
        (await db.AuditEntries.AsNoTracking().Select(e => e.TenantId).Distinct().ToListAsync(ct)).Should().Equal(a.TenantId);
        (await db.ProvenanceActivities.AsNoTracking().Select(e => e.TenantId).Distinct().ToListAsync(ct)).Should().Equal(a.TenantId);
        (await db.OutboxMessages.AsNoTracking().Select(e => e.TenantId).Distinct().ToListAsync(ct)).Should().Equal(a.TenantId);
    }

    private async Task<PostgresException> InsertAsTenantAsync(TenantId tenant, object entity, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        using var writes = db.PermitPipelineWrites();
        db.Add(entity);
        var act = () => db.SaveChangesAsync(ct);
        return (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which;
    }

    /// <summary>Runs one statement as the application role, with <paramref name="tenant"/> set or no tenant at all.</summary>
    private async Task<T> AppScalarAsync<T>(string sql, TenantId? tenant, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        if (tenant is { } scoped)
        {
            await using var set = new NpgsqlCommand($"SELECT set_config('{TenantIsolationSql.TenantSetting}', '{scoped.Value}', false)", connection);
            await set.ExecuteNonQueryAsync(ct);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private sealed record TableSecurity(string Table, bool Enabled, bool Forced, int Policies, bool HasTenantId);
}
