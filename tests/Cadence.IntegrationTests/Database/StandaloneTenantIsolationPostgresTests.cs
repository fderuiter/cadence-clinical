using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Workers;
using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Apps.Fileshare.Workers;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Audit;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using NSubstitute;

using Xunit;

namespace Cadence.IntegrationTests.Database;

/// <summary>
/// Verifies PostgreSQL Row-Level Security (<c>FORCE ROW LEVEL SECURITY</c>), <c>cadence_app</c> (<c>NOSUPERUSER NOBYPASSRLS</c>)
/// enforcement, <c>kernel.tenant_id_by_code(text)</c> session binding + reset, and cross-tenant isolation under wildcard
/// <c>*</c> study scope across all 15 standalone clinical <see cref="DbContext"/> schemas (K-ID-1, K-ID-2, ADR-2250, PRD-CRF-013).
/// </summary>
[Trait("Category", "Integration")]
public sealed class StandaloneTenantIsolationPostgresTests(ThrowawayPostgresDatabase database)
    : IClassFixture<ThrowawayPostgresDatabase>
{
    private static readonly string[] ExpectedStandaloneSchemas =
    [
        "ctms",
        "econsent",
        "eisf",
        "etmf",
        "edc",
        "fileshare",
        "imaging",
        "interop",
        "knowledge",
        "notifications",
        "org",
        "payments",
        "quality",
        "safety",
        "tickets",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task AllFifteenStandaloneSchemas_ForceRowLevelSecurity_AndEnforceTenantIsolationAsCadenceApp()
    {
        database.SkipIfUnavailable();

        var migratedSchemas = new List<string>();

        foreach (Type factoryType in ServiceDbContextFactories.All)
        {
            string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
            Type contextType = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.ReturnType;

            var migrateMethod = typeof(StandaloneTenantIsolationPostgresTests)
                .GetMethod(nameof(MigrateAndVerifySchemaAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contextType);

            await (Task)migrateMethod.Invoke(null, [database, schema, Ct])!;
            migratedSchemas.Add(schema);
        }

        migratedSchemas.Should().BeEquivalentTo(ExpectedStandaloneSchemas);

        // Verify cadence_app is strictly NOSUPERUSER NOBYPASSRLS.
        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var roleCmd = adminConn.CreateCommand();
            roleCmd.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'cadence_app'";
            await using var reader = await roleCmd.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).Should().BeTrue("cadence_app role must exist");
            reader.GetBoolean(0).Should().BeFalse("cadence_app must be NOSUPERUSER");
            reader.GetBoolean(1).Should().BeFalse("cadence_app must be NOBYPASSRLS");
        }

        // Seed rows in Tenant A and Tenant B into payments.payments_participant_stipend_rules via owner connection,
        // then connect strictly as cadence_app to verify unbound, cross-tenant read, insert, and update denials.
        Guid tenantAlpha = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha");
        Guid tenantBeta = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-beta");

        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var seedCmd = adminConn.CreateCommand();
            seedCmd.CommandText = """
                SELECT pg_catalog.set_config('cadence.tenant_id', @tenantA, false);
                INSERT INTO "payments"."payments_participant_stipend_rules"
                    ("Id", "StudyId", "VisitCode", "VisitName", "Amount", "Currency", "RequiresVerification", "Taxable", "IsMilestone", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "VersionIndex", "tenant_id")
                VALUES
                    ('cfg-rls-alpha', 'STUDY-RLS', 'V1', 'Alpha Visit', 100.00, 'USD', true, false, false, true, NOW(), 'seed', NOW(), 1, @tenantAUuid);

                SELECT pg_catalog.set_config('cadence.tenant_id', @tenantB, false);
                INSERT INTO "payments"."payments_participant_stipend_rules"
                    ("Id", "StudyId", "VisitCode", "VisitName", "Amount", "Currency", "RequiresVerification", "Taxable", "IsMilestone", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "VersionIndex", "tenant_id")
                VALUES
                    ('cfg-rls-beta', 'STUDY-RLS', 'V1', 'Beta Visit', 200.00, 'USD', true, false, false, true, NOW(), 'seed', NOW(), 1, @tenantBUuid);
                """;
            seedCmd.Parameters.AddWithValue("tenantA", tenantAlpha.ToString("D"));
            seedCmd.Parameters.AddWithValue("tenantAUuid", tenantAlpha);
            seedCmd.Parameters.AddWithValue("tenantB", tenantBeta.ToString("D"));
            seedCmd.Parameters.AddWithValue("tenantBUuid", tenantBeta);
            await seedCmd.ExecuteNonQueryAsync(Ct);
        }

        string cadenceAppConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);

        // 1. Unbound cadence_app session sees zero rows and cannot INSERT.
        await using (var unboundConn = new NpgsqlConnection(cadenceAppConnString))
        {
            await unboundConn.OpenAsync(Ct);

            await using (var selectCmd = unboundConn.CreateCommand())
            {
                selectCmd.CommandText = """SELECT COUNT(*) FROM "payments"."payments_participant_stipend_rules" WHERE "StudyId" = 'STUDY-RLS'""";
                long count = (long)(await selectCmd.ExecuteScalarAsync(Ct))!;
                count.Should().Be(0, "an unbound session must read zero tenant rows under FORCE ROW LEVEL SECURITY");
            }

            await using var insertCmd = unboundConn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO "payments"."payments_participant_stipend_rules"
                    ("Id", "StudyId", "VisitCode", "VisitName", "Amount", "Currency", "RequiresVerification", "Taxable", "IsMilestone", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "VersionIndex", "tenant_id")
                VALUES
                    ('cfg-rls-unbound', 'STUDY-RLS', 'V9', 'Unbound Visit', 50.00, 'USD', true, false, false, true, NOW(), 'seed', NOW(), 1, @tenantA)
                """;
            insertCmd.Parameters.AddWithValue("tenantA", tenantAlpha);
            Func<Task> act = () => insertCmd.ExecuteNonQueryAsync(Ct);
            (await act.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("42501");
        }

        // 2. cadence_app bound to Tenant Alpha sees ONLY Tenant Alpha rows and cannot forge/mutate into Tenant Beta.
        await using var alphaConn = new NpgsqlConnection(cadenceAppConnString);
        await alphaConn.OpenAsync(Ct);

        await using (var bindCmd = alphaConn.CreateCommand())
        {
            bindCmd.CommandText = "SELECT pg_catalog.set_config('cadence.tenant_id', @tenantA, false)";
            bindCmd.Parameters.AddWithValue("tenantA", tenantAlpha.ToString("D"));
            await bindCmd.ExecuteNonQueryAsync(Ct);
        }

        await using (var selectCmd = alphaConn.CreateCommand())
        {
            selectCmd.CommandText = """SELECT "Id" FROM "payments"."payments_participant_stipend_rules" WHERE "StudyId" = 'STUDY-RLS'""";
            var ids = new List<string>();
            await using var reader = await selectCmd.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                ids.Add(reader.GetString(0));
            }

            ids.Should().Equal("cfg-rls-alpha");
        }

        // Forging Tenant Beta's tenant_id during INSERT fails WITH CHECK (42501).
        await using (var crossInsertCmd = alphaConn.CreateCommand())
        {
            crossInsertCmd.CommandText = """
                INSERT INTO "payments"."payments_participant_stipend_rules"
                    ("Id", "StudyId", "VisitCode", "VisitName", "Amount", "Currency", "RequiresVerification", "Taxable", "IsMilestone", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "VersionIndex", "tenant_id")
                VALUES
                    ('cfg-rls-forged', 'STUDY-RLS', 'V2', 'Forged Beta', 999.00, 'USD', true, false, false, true, NOW(), 'seed', NOW(), 1, @tenantB)
                """;
            crossInsertCmd.Parameters.AddWithValue("tenantB", tenantBeta);
            Func<Task> crossInsert = () => crossInsertCmd.ExecuteNonQueryAsync(Ct);
            (await crossInsert.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("42501");
        }

        // Reassigning Tenant Alpha's row to Tenant Beta fails WITH CHECK (42501).
        await using var crossUpdateCmd = alphaConn.CreateCommand();
        crossUpdateCmd.CommandText = """
            UPDATE "payments"."payments_participant_stipend_rules"
            SET "tenant_id" = @tenantB
            WHERE "Id" = 'cfg-rls-alpha'
            """;
        crossUpdateCmd.Parameters.AddWithValue("tenantB", tenantBeta);
        Func<Task> crossUpdate = () => crossUpdateCmd.ExecuteNonQueryAsync(Ct);
        (await crossUpdate.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("42501");
    }

    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    public async Task ConnectionInterceptor_ResolvesTenantViaKernelFunction_BindsOnOpen_ResetsOnClose_AndBlocksWildcardCrossTenantReads()
    {
        database.SkipIfUnavailable();

        // Ensure payments schema is migrated and granted to cadence_app.
        await MigrateAndVerifySchemaAsync<PaymentsDbContext>(database, PaymentsDbContextFactory.Schema, Ct);

        // Register an inactive tenant in kernel.tenants to verify kernel.tenant_id_by_code returns null for inactive codes.
        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO "kernel"."tenants" (id, code, name, is_active)
                VALUES (@id, 'tenant-inactive', 'Inactive Tenant', false)
                ON CONFLICT (code) DO UPDATE SET is_active = false;
                """;
            cmd.Parameters.AddWithValue("id", DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-inactive"));
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        string cadenceAppConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);

        await using var resolver = new PostgresTenantCodeResolver(cadenceAppConnString);
        Guid? alphaGuid = await resolver.ResolveTenantIdAsync("tenant-alpha", Ct);
        Guid? betaGuid = await resolver.ResolveTenantIdAsync("tenant-beta", Ct);
        Guid? inactiveGuid = await resolver.ResolveTenantIdAsync("tenant-inactive", Ct);
        Guid? missingGuid = await resolver.ResolveTenantIdAsync("tenant-does-not-exist", Ct);

        alphaGuid.Should().Be(DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha"));
        betaGuid.Should().Be(DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-beta"));
        inactiveGuid.Should().BeNull("inactive tenants must not resolve through kernel.tenant_id_by_code");
        missingGuid.Should().BeNull("unknown tenant codes must not resolve through kernel.tenant_id_by_code");

        var activeIds = await resolver.ListActiveTenantIdsAsync(Ct);
        activeIds.Should().Contain(alphaGuid!.Value).And.Contain(betaGuid!.Value);
        activeIds.Should().NotContain(DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-inactive"));

        // Verify interceptor binds cadence.tenant_id on open and resets it to empty string on close.
        var accessor = new CurrentTenantAccessor(resolver, httpContextAccessor: null);
        string paymentsConnString = CadenceDatabaseExtensions.WithModuleSchema(cadenceAppConnString, PaymentsDbContextFactory.Schema);
        await using var dataSource = NpgsqlDataSource.Create(paymentsConnString);

        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        optionsBuilder.UseNpgsql(dataSource);
        optionsBuilder.UseCadenceStandaloneTenancy(accessor, isRelational: true);
        optionsBuilder.AddInterceptors(
            StandaloneTenantConnectionInterceptor.Instance,
            StandaloneTenantSaveChangesInterceptor.Instance);
        var dbOptions = optionsBuilder.Options;

        using (accessor.Bind(alphaGuid.Value))
        {
            await using var alphaDb = new PaymentsDbContext(dbOptions, TestFieldProtection.CreateEncryptor());
            await alphaDb.Database.OpenConnectionAsync(Ct);
            DbConnection openConn = alphaDb.Database.GetDbConnection();
            await using (var checkCmd = openConn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT pg_catalog.current_setting('cadence.tenant_id', true)";
                string bound = (string)(await checkCmd.ExecuteScalarAsync(Ct))!;
                bound.Should().Be(alphaGuid.Value.ToString("D"));
            }

            await alphaDb.Database.CloseConnectionAsync();
        }

        // Open a raw connection from the same NpgsqlDataSource pool and confirm cadence.tenant_id was reset on close.
        await using (var pooledRaw = await dataSource.OpenConnectionAsync(Ct))
        {
            await using var verifyResetCmd = pooledRaw.CreateCommand();
            verifyResetCmd.CommandText = "SELECT COALESCE(pg_catalog.current_setting('cadence.tenant_id', true), '')";
            string afterClose = (string)(await verifyResetCmd.ExecuteScalarAsync(Ct))!;
            afterClose.Should().BeEmpty("closing an EF connection must reset cadence.tenant_id before returning to the pool");
        }

        // Boot the real Payments host connected strictly as cadence_app and verify wildcard StudyIds = ["*"]
        // in ClinicalScopeGuard cannot see or mutate another tenant's wallets or site configs, and inactive/missing tenants get 403.
        await using var paymentsHost = new CadenceAppPaymentsHost(cadenceAppConnString);

        using var alphaSponsorClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-alpha",
            Roles: ["Finance Officer", "sysadmin"],
            SiteIds: [],
            TenantId: "tenant-alpha",
            StudyIds: ["*"],
            ProductFamilies: TestSecurityContext.AllProductFamilies));

        using var betaSponsorClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-beta",
            Roles: ["Finance Officer", "sysadmin"],
            SiteIds: [],
            TenantId: "tenant-beta",
            StudyIds: ["*"],
            ProductFamilies: TestSecurityContext.AllProductFamilies));

        // Seed a stipend rule via HTTP in Tenant Alpha and another in Tenant Beta on the exact same study ID!
        const string sharedStudyId = "STUDY-WILDCARD-ISO";
        var createAlphaRule = await alphaSponsorClient.PostAsJsonAsync(
            "/api/v1/payments/participant/stipend-rules",
            new
            {
                studyId = sharedStudyId,
                visitCode = "V1-ALPHA",
                visitName = "Alpha Baseline Visit",
                amount = 125.00m,
                currency = "USD",
            },
            Ct);
        createAlphaRule.StatusCode.Should().Be(HttpStatusCode.OK, await createAlphaRule.Content.ReadAsStringAsync(Ct));

        var createBetaRule = await betaSponsorClient.PostAsJsonAsync(
            "/api/v1/payments/participant/stipend-rules",
            new
            {
                studyId = sharedStudyId,
                visitCode = "V1-BETA",
                visitName = "Beta Baseline Visit",
                amount = 250.00m,
                currency = "USD",
            },
            Ct);
        createBetaRule.StatusCode.Should().Be(HttpStatusCode.OK, await createBetaRule.Content.ReadAsStringAsync(Ct));

        // Wildcard StudyIds = ["*"] caller in Tenant Alpha sees ONLY Alpha's rule and zero rows from Tenant Beta!
        var alphaListResp = await alphaSponsorClient.GetAsync($"/api/v1/payments/participant/stipend-rules?studyId={sharedStudyId}", Ct);
        alphaListResp.StatusCode.Should().Be(HttpStatusCode.OK);
        string alphaJson = await alphaListResp.Content.ReadAsStringAsync(Ct);
        alphaJson.Should().Contain("V1-ALPHA").And.NotContain("V1-BETA");

        // Wildcard StudyIds = ["*"] caller in Tenant Beta sees ONLY Beta's rule and zero rows from Tenant Alpha!
        var betaListResp = await betaSponsorClient.GetAsync($"/api/v1/payments/participant/stipend-rules?studyId={sharedStudyId}", Ct);
        betaListResp.StatusCode.Should().Be(HttpStatusCode.OK);
        string betaJson = await betaListResp.Content.ReadAsStringAsync(Ct);
        betaJson.Should().Contain("V1-BETA").And.NotContain("V1-ALPHA");

        // Inactive tenant and missing X-Tenant-Id both receive HTTP 403 Forbidden.
        using var inactiveClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-inactive",
            Roles: ["Finance Officer"],
            SiteIds: [],
            TenantId: "tenant-inactive",
            StudyIds: ["*"],
            ProductFamilies: TestSecurityContext.AllProductFamilies));
        var inactiveResp = await inactiveClient.GetAsync($"/api/v1/payments/participant/stipend-rules?studyId={sharedStudyId}", Ct);
        inactiveResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var missingTenantClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-no-tenant",
            Roles: ["Finance Officer"],
            SiteIds: [],
            TenantId: null,
            StudyIds: ["*"]));
        var missingTenantResp = await missingTenantClient.GetAsync($"/api/v1/payments/participant/stipend-rules?studyId={sharedStudyId}", Ct);
        missingTenantResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task MigrateAndVerifySchemaAsync<TContext>(
        ThrowawayPostgresDatabase database,
        string schema,
        CancellationToken ct)
        where TContext : DbContext
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = database.ConnectionString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);

        await using var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(ct).ConfigureAwait(false);

        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(ct).ConfigureAwait(false);
            await using var grantCmd = adminConn.CreateCommand();
            grantCmd.CommandText = StandaloneTenantIsolationSql.GrantAppRoleSql(schema, "cadence_app");
            await grantCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var tenantTables = context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned()
                && e.BaseType is null
                && !e.HasSharedClrType
                && e.FindPrimaryKey() is not null
                && e.GetViewName() is null
                && e.GetSchema() is null)
            .Select(e => e.GetTableName())
            .Where(t => t is not null && !StandaloneTenantModelExtensions.NonTenantTableNames.Contains(t))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        tenantTables.Should().NotBeEmpty($"schema '{schema}' ({typeof(TContext).Name}) must contain tenant-scoped tables");

        await using var verifyConn = new NpgsqlConnection(database.AdminDatabaseConnectionString);
        await verifyConn.OpenAsync(ct).ConfigureAwait(false);

        foreach (string table in tenantTables)
        {
            await using var colCmd = verifyConn.CreateCommand();
            colCmd.CommandText = """
                SELECT data_type, is_nullable
                FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = @table AND column_name = 'tenant_id'
                """;
            colCmd.Parameters.AddWithValue("schema", schema);
            colCmd.Parameters.AddWithValue("table", table);
            await using (var colReader = await colCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                (await colReader.ReadAsync(ct).ConfigureAwait(false))
                    .Should().BeTrue($"{schema}.{table} must have a tenant_id column");
                colReader.GetString(0).Should().Be("uuid", $"{schema}.{table}.tenant_id must be uuid");
                colReader.GetString(1).Should().Be("NO", $"{schema}.{table}.tenant_id must be NOT NULL");
            }

            await using var rlsCmd = verifyConn.CreateCommand();
            rlsCmd.CommandText = """
                SELECT c.relrowsecurity, c.relforcerowsecurity,
                       EXISTS (
                           SELECT 1 FROM pg_policy p
                           WHERE p.polrelid = c.oid
                             AND p.polname = 'tenant_isolation'
                             AND pg_get_expr(p.polqual, p.polrelid) LIKE '%current_tenant_id%'
                             AND pg_get_expr(p.polwithcheck, p.polrelid) LIKE '%current_tenant_id%'
                       ) AS has_policy
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table
                """;
            rlsCmd.Parameters.AddWithValue("schema", schema);
            rlsCmd.Parameters.AddWithValue("table", table);
            await using var rlsReader = await rlsCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            (await rlsReader.ReadAsync(ct).ConfigureAwait(false))
                .Should().BeTrue($"{schema}.{table} must exist in pg_class");
            rlsReader.GetBoolean(0).Should().BeTrue($"{schema}.{table} must ENABLE ROW LEVEL SECURITY");
            rlsReader.GetBoolean(1).Should().BeTrue($"{schema}.{table} must FORCE ROW LEVEL SECURITY");
            rlsReader.GetBoolean(2).Should().BeTrue($"{schema}.{table} must define policy tenant_isolation using kernel.current_tenant_id()");
        }
    }

    private static string BuildCadenceAppConnectionString(string adminDbConnectionString) =>
        new NpgsqlConnectionStringBuilder(adminDbConnectionString)
        {
            Username = "cadence_app",
            Password = "cadence_app", // pragma: allowlist secret
        }.ConnectionString;

    private sealed class CadenceAppPaymentsHost(string cadenceAppConnectionString)
        : WebApplicationFactory<Cadence.Apps.Payments.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestFieldProtectionKeys();
            builder.UseSetting("ConnectionStrings:cadence", cadenceAppConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            base.ConfigureWebHost(builder);
        }
    }

    private sealed class FixedCurrentTenantAccessor(Guid tenantId) : ICurrentTenantAccessor
    {
        public Guid? GetCurrentTenantId() => tenantId;

        public ValueTask<Guid?> GetCurrentTenantIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Guid?>(tenantId);

        public bool IsSystemSuppressed => false;

        public bool HasHttpRequestWithoutTenant => false;

        public bool IsCurrentTenantReadOnly => false;

        public bool IsHttpWriteExemptFromExpiry => false;

        public bool IsHttpProductFamilyExempt => false;

        public IReadOnlyList<string>? CurrentTenantProductFamilies => null;

        public TenantScopingCaps? CurrentTenantScoping => null;
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BitemporalAndInsertOnlyGuards_EnforceKt001_Ka001_Exclusion_And_AsOf_UnderAppRole()
    {
        database.SkipIfUnavailable();

        await MigrateAndVerifySchemaAsync<Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContext>(database, "ctms", Ct);
        await MigrateAndVerifySchemaAsync<Cadence.Apps.Tickets.Infrastructure.Persistence.TicketsDbContext>(database, "tickets", Ct);

        string appConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);
        Guid tenantId = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-temporal-oq");
        var t0 = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(2);
        var tMid = t0.AddHours(1);

        var tenantAccessor = new FixedCurrentTenantAccessor(tenantId);
        var ctmsOptionsBuilder = new DbContextOptionsBuilder<Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContext>();
        ctmsOptionsBuilder
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(appConnString, "ctms"))
            .UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true)
            .AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance,
                Cadence.Database.Concurrency.StandaloneConcurrencyTokenInterceptor.Instance);
        var ctmsOptions = ctmsOptionsBuilder.Options;

        string doaId = Guid.NewGuid().ToString();
        await using (var ctms = new Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContext(ctmsOptions))
        {
            var doa = new Cadence.Apps.Ctms.Domain.DoaDelegationRecord(doaId, Cadence.Apps.Ctms.Domain.DoaDelegationStatus.Active)
            {
                SiteId = "SITE-01",
                StaffUserId = "user-cra-1",
                TaskCode = "REVIEW",
                StartDate = t0,
                IsActive = true,
                CreatedAt = t0.UtcDateTime,
                CreatedBy = "pi-1",
                ReasonForChange = "Initial delegation",
                VersionIndex = 1,
            };
            ctms.DoaDelegationRecords.Add(doa);
            ctms.DoaDelegationPeriods.Add(new Cadence.Apps.Ctms.Domain.DoaDelegationPeriod
            {
                DelegationRecordId = doaId,
                SiteCode = doa.SiteId,
                StaffUserId = doa.StaffUserId,
                TaskCode = doa.TaskCode,
                StartDate = t0,
                Status = Cadence.Apps.Ctms.Domain.DoaDelegationStatus.Active,
                IsActive = true,
                ValidPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CreateOpenPeriod(t0),
                RecordedPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CreateOpenPeriod(t0),
                CreatedBy = "pi-1",
                ReasonForChange = "Initial delegation",
            });
            await ctms.SaveChangesAsync(Ct);

            // Overlapping open period for same DelegationRecordId must fail GiST exclusion (23P01)
            await using var rawConn = new NpgsqlConnection(CadenceDatabaseExtensions.WithModuleSchema(appConnString, "ctms"));
            await rawConn.OpenAsync(Ct);
            await using (var overlapTx = await rawConn.BeginTransactionAsync(Ct))
            {
                await using (var setTenant = new NpgsqlCommand(
                    """
                    SELECT pg_catalog.set_config('cadence.tenant_id', @tenant, true);
                    SET CONSTRAINTS ALL IMMEDIATE;
                    """,
                    rawConn,
                    overlapTx))
                {
                    setTenant.Parameters.AddWithValue("tenant", tenantId.ToString("D"));
                    await setTenant.ExecuteNonQueryAsync(Ct);
                }

                await using var dupCmd = new NpgsqlCommand(
                    """
                    INSERT INTO ctms.doa_delegation_periods
                        ("Id", "DelegationRecordId", "SiteCode", "StaffUserId", "TaskCode", "StartDate",
                         "Status", "IsActive", valid_period, recorded_period, "CreatedBy", "ReasonForChange", tenant_id, _version)
                    VALUES
                        (@id, @doaId, 'SITE-01', 'user-cra-1', 'REVIEW', @t0,
                         'Active', true, tstzrange(@t0, 'infinity'::timestamptz, '[)'), tstzrange(@t0, 'infinity'::timestamptz, '[)'), 'pi-1', 'Overlap', @tenantUuid, 0);
                    """,
                    rawConn,
                    overlapTx);
                dupCmd.Parameters.AddWithValue("id", Guid.NewGuid().ToString());
                dupCmd.Parameters.AddWithValue("doaId", doaId);
                dupCmd.Parameters.AddWithValue("t0", t0);
                dupCmd.Parameters.AddWithValue("tenantUuid", tenantId);
                var pgEx = await Assert.ThrowsAsync<PostgresException>(() => dupCmd.ExecuteNonQueryAsync(Ct));
                Assert.Equal("23P01", pgEx.SqlState);
                await overlapTx.RollbackAsync(Ct);
            }

            // KT001 rejects in-place mutation of Status or DELETE on doa_delegation_periods
            await using (var ktTx = await rawConn.BeginTransactionAsync(Ct))
            {
                await using (var setTenant = new NpgsqlCommand("SELECT pg_catalog.set_config('cadence.tenant_id', @tenant, true);", rawConn, ktTx))
                {
                    setTenant.Parameters.AddWithValue("tenant", tenantId.ToString("D"));
                    await setTenant.ExecuteNonQueryAsync(Ct);
                }

                await using var mutateCmd = new NpgsqlCommand(
                    """UPDATE ctms.doa_delegation_periods SET "Status" = 'Revoked' WHERE "DelegationRecordId" = @doaId;""",
                    rawConn,
                    ktTx);
                mutateCmd.Parameters.AddWithValue("doaId", doaId);
                var ktUpdateEx = await Assert.ThrowsAsync<PostgresException>(() => mutateCmd.ExecuteNonQueryAsync(Ct));
                Assert.Equal("KT001", ktUpdateEx.SqlState);
                await ktTx.RollbackAsync(Ct);
            }

            await using var ktDelTx = await rawConn.BeginTransactionAsync(Ct);
            await using (var setTenant = new NpgsqlCommand("SELECT pg_catalog.set_config('cadence.tenant_id', @tenant, true);", rawConn, ktDelTx))
            {
                setTenant.Parameters.AddWithValue("tenant", tenantId.ToString("D"));
                await setTenant.ExecuteNonQueryAsync(Ct);
            }

            await using var delCmd = new NpgsqlCommand(
                """DELETE FROM ctms.doa_delegation_periods WHERE "DelegationRecordId" = @doaId;""",
                rawConn,
                ktDelTx);
            delCmd.Parameters.AddWithValue("doaId", doaId);
            var ktDelEx = await Assert.ThrowsAsync<PostgresException>(() => delCmd.ExecuteNonQueryAsync(Ct));
            Assert.Equal("KT001", ktDelEx.SqlState);
            await ktDelTx.RollbackAsync(Ct);
        }

        // Transition DOA at t1 (closing open recorded_period is allowed by KT001) and verify as_of(valid_at, known_at)
        await using (var ctms2 = new Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContext(ctmsOptions))
        {
            var openRows = await ctms2.DoaDelegationPeriods
                .Where(p => p.DelegationRecordId == doaId && p.RecordedPeriod.UpperBoundInfinite && p.ValidPeriod.UpperBoundInfinite)
                .ToListAsync(Ct);
            Assert.Single(openRows);
            var prior = openRows[0];
            prior.RecordedPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CloseRecordedAt(prior.RecordedPeriod, t1);

            ctms2.DoaDelegationPeriods.Add(new Cadence.Apps.Ctms.Domain.DoaDelegationPeriod
            {
                DelegationRecordId = doaId,
                SiteCode = prior.SiteCode,
                StaffUserId = prior.StaffUserId,
                TaskCode = prior.TaskCode,
                StartDate = prior.StartDate,
                Status = Cadence.Apps.Ctms.Domain.DoaDelegationStatus.Active,
                IsActive = true,
                ValidPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CreateClosedPeriod(t0, t1),
                RecordedPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CreateOpenPeriod(t1),
                CreatedBy = "pi-1",
                ReasonForChange = "Carry forward prior segment",
            });
            ctms2.DoaDelegationPeriods.Add(new Cadence.Apps.Ctms.Domain.DoaDelegationPeriod
            {
                DelegationRecordId = doaId,
                SiteCode = prior.SiteCode,
                StaffUserId = prior.StaffUserId,
                TaskCode = prior.TaskCode,
                StartDate = prior.StartDate,
                EndDate = t1,
                Status = Cadence.Apps.Ctms.Domain.DoaDelegationStatus.Revoked,
                IsActive = false,
                ValidPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CreateOpenPeriod(t1),
                RecordedPeriod = Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.CreateOpenPeriod(t1),
                CreatedBy = "pi-1",
                ReasonForChange = "Revoked delegation",
            });
            await ctms2.SaveChangesAsync(Ct);

            var asOfMidAtLatest = await Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.QueryAsOfAsync(
                ctms2.DoaDelegationPeriods.AsNoTracking().Where(p => p.DelegationRecordId == doaId),
                ctms2,
                validAt: tMid,
                knownAt: t1.AddMinutes(5),
                Ct);
            Assert.Single(asOfMidAtLatest);
            Assert.Equal(Cadence.Apps.Ctms.Domain.DoaDelegationStatus.Active, asOfMidAtLatest[0].Status);

            var asOfAfterRevoke = await Cadence.Database.Temporal.StandaloneBitemporalModelExtensions.QueryAsOfAsync(
                ctms2.DoaDelegationPeriods.AsNoTracking().Where(p => p.DelegationRecordId == doaId),
                ctms2,
                validAt: t1.AddMinutes(5),
                knownAt: t1.AddMinutes(5),
                Ct);
            Assert.Single(asOfAfterRevoke);
            Assert.Equal(Cadence.Apps.Ctms.Domain.DoaDelegationStatus.Revoked, asOfAfterRevoke[0].Status);
        }

        // Verify KA001 insert-only trigger blocks UPDATE and DELETE on ticket_revisions under cadence_app
        var ticketOptionsBuilder = new DbContextOptionsBuilder<Cadence.Apps.Tickets.Infrastructure.Persistence.TicketsDbContext>();
        ticketOptionsBuilder
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(appConnString, "tickets"))
            .UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true)
            .AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance,
                Cadence.Database.Concurrency.StandaloneConcurrencyTokenInterceptor.Instance);
        var ticketOptions = ticketOptionsBuilder.Options;

        string ticketId = Guid.NewGuid().ToString();
        await using var ticketsDb = new Cadence.Apps.Tickets.Infrastructure.Persistence.TicketsDbContext(ticketOptions);
        var ticket = new Cadence.Apps.Tickets.Domain.Ticket
        {
            Id = ticketId,
            Reference = "TCK-KA001-01",
            Title = "Monitor Calibration Issue",
            Description = "Initial report",
            Category = "SITE_ISSUE",
            Priority = "HIGH",
            Reporter = "cra-1",
            CreatedAt = t0,
            CreatedBy = "cra-1",
            ReasonForChange = "Ticket opened",
            VersionIndex = 1,
        };
        ticketsDb.Tickets.Add(ticket);
        ticketsDb.TicketRevisions.Add(Cadence.Apps.Tickets.Domain.TicketRevision.FromTicket(ticket, t0, "cra-1"));
        await ticketsDb.SaveChangesAsync(Ct);

        await using var rawTicketsConn = new NpgsqlConnection(CadenceDatabaseExtensions.WithModuleSchema(appConnString, "tickets"));
        await rawTicketsConn.OpenAsync(Ct);
        await using var kaTx = await rawTicketsConn.BeginTransactionAsync(Ct);
        await using (var setTenant = new NpgsqlCommand("SELECT pg_catalog.set_config('cadence.tenant_id', @tenant, true);", rawTicketsConn, kaTx))
        {
            setTenant.Parameters.AddWithValue("tenant", tenantId.ToString("D"));
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var updRev = new NpgsqlCommand(
            """UPDATE tickets.ticket_revisions SET "Title" = 'Tampered' WHERE "TicketId" = @tid;""",
            rawTicketsConn,
            kaTx);
        updRev.Parameters.AddWithValue("tid", ticketId);
        var kaEx = await Assert.ThrowsAsync<PostgresException>(() => updRev.ExecuteNonQueryAsync(Ct));
        Assert.Equal("KA001", kaEx.SqlState);
        await kaTx.RollbackAsync(Ct);
    }

    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    public async Task TenantCodeResolverCache_MidScopeRebinding_AndPoolResetOnError_EnforceFiveSecondTtlWithoutCachingNullOrLeakingState()
    {
        database.SkipIfUnavailable();

        await MigrateAndVerifySchemaAsync<PaymentsDbContext>(database, PaymentsDbContextFactory.Schema, Ct);

        const string dynamicInactiveCode = "tenant-dynamic-inactive";
        Guid dynamicInactiveId = DeterministicTenantCodeResolver.ComputeDeterministicGuid(dynamicInactiveCode);
        Guid alphaId = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha");
        Guid betaId = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-beta");

        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO "kernel"."tenants" (id, code, name, is_active)
                VALUES (@id, @code, 'Dynamic Inactive Tenant', false)
                ON CONFLICT (code) DO UPDATE SET is_active = false;

                SELECT pg_catalog.set_config('cadence.tenant_id', @alphaText, false);
                INSERT INTO "payments"."payments_participant_stipend_rules"
                    ("Id", "StudyId", "VisitCode", "VisitName", "Amount", "Currency", "RequiresVerification", "Taxable", "IsMilestone", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "VersionIndex", "tenant_id")
                VALUES
                    ('cfg-rebind-alpha', 'STUDY-REBIND', 'V1-A', 'Alpha Rebind Visit', 110.00, 'USD', true, false, false, true, NOW(), 'seed', NOW(), 1, @alphaUuid)
                ON CONFLICT ("Id") DO NOTHING;

                SELECT pg_catalog.set_config('cadence.tenant_id', @betaText, false);
                INSERT INTO "payments"."payments_participant_stipend_rules"
                    ("Id", "StudyId", "VisitCode", "VisitName", "Amount", "Currency", "RequiresVerification", "Taxable", "IsMilestone", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "VersionIndex", "tenant_id")
                VALUES
                    ('cfg-rebind-beta', 'STUDY-REBIND', 'V1-B', 'Beta Rebind Visit', 220.00, 'USD', true, false, false, true, NOW(), 'seed', NOW(), 1, @betaUuid)
                ON CONFLICT ("Id") DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("id", dynamicInactiveId);
            cmd.Parameters.AddWithValue("code", dynamicInactiveCode);
            cmd.Parameters.AddWithValue("alphaText", alphaId.ToString("D"));
            cmd.Parameters.AddWithValue("alphaUuid", alphaId);
            cmd.Parameters.AddWithValue("betaText", betaId.ToString("D"));
            cmd.Parameters.AddWithValue("betaUuid", betaId);
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        string cadenceAppConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);
        string paymentsModuleCs = CadenceDatabaseExtensions.WithModuleSchema(cadenceAppConnString, PaymentsDbContextFactory.Schema);
        string rebindPaymentsCs = new NpgsqlConnectionStringBuilder(paymentsModuleCs)
        {
            MaxPoolSize = 4,
            MinPoolSize = 0,
        }.ConnectionString;

        using var wireRecorder = new WireSqlRecorder();
        await using var sharedDataSource = wireRecorder.CreateDataSource(rebindPaymentsCs);
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        await using var resolver = new PostgresTenantCodeResolver(sharedDataSource, fakeClock, ownsDataSource: false);

        // 1. Cold lookup of active tenant hits PostgreSQL once; warm lookups within 5s execute zero SQL queries.
        (await resolver.ResolveTenantIdAsync("tenant-alpha", Ct)).Should().Be(alphaId);
        wireRecorder.CountContaining("tenant_id_by_code").Should().Be(1);

        fakeClock.Advance(TimeSpan.FromSeconds(4));
        (await resolver.ResolveTenantIdAsync("tenant-alpha", Ct)).Should().Be(alphaId);
        AssertSyncResolve(resolver, "tenant-alpha", alphaId);
        wireRecorder.CountContaining("tenant_id_by_code").Should().Be(1, "active tenant mappings must be cached for 5 seconds");

        // Advancing clock past 5s TTL re-queries PostgreSQL.
        fakeClock.Advance(TimeSpan.FromSeconds(2));
        (await resolver.ResolveTenantIdAsync("tenant-alpha", Ct)).Should().Be(alphaId);
        wireRecorder.CountContaining("tenant_id_by_code").Should().Be(2);

        // 2. Inactive and unknown tenant codes are NEVER cached; reactivating an inactive tenant resolves immediately.
        wireRecorder.Clear();
        (await resolver.ResolveTenantIdAsync(dynamicInactiveCode, Ct)).Should().BeNull();
        (await resolver.ResolveTenantIdAsync(dynamicInactiveCode, Ct)).Should().BeNull();
        (await resolver.ResolveTenantIdAsync("tenant-unknown-code", Ct)).Should().BeNull();
        wireRecorder.CountContaining("tenant_id_by_code").Should().Be(3, "null/inactive lookups must never be cached");

        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var activateCmd = adminConn.CreateCommand();
            activateCmd.CommandText = """UPDATE "kernel"."tenants" SET is_active = true WHERE code = @code""";
            activateCmd.Parameters.AddWithValue("code", dynamicInactiveCode);
            await activateCmd.ExecuteNonQueryAsync(Ct);
        }

        // Without advancing FakeClock, reactivated tenant resolves immediately because null was not cached.
        (await resolver.ResolveTenantIdAsync(dynamicInactiveCode, Ct)).Should().Be(dynamicInactiveId);

        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var deactivateCmd = adminConn.CreateCommand();
            deactivateCmd.CommandText = """UPDATE "kernel"."tenants" SET is_active = false WHERE code = @code""";
            deactivateCmd.Parameters.AddWithValue("code", dynamicInactiveCode);
            await deactivateCmd.ExecuteNonQueryAsync(Ct);
        }

        fakeClock.Advance(TimeSpan.FromSeconds(6));

        // 3. Mid-scope ITenantScope.Bind rebinding on one leased DbContext connection + fail-closed invalid bind + pool reset on error.
        var leaseTracker = new LeaseTrackingInterceptor();
        var services = new ServiceCollection();
        services.AddSingleton(TestFieldProtection.CreateEncryptor());
        services.AddSingleton<ITenantCodeResolver>(resolver);
        services.AddSingleton<CurrentTenantAccessor>();
        services.AddSingleton<ICurrentTenantAccessor>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<ITenantScope>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<StandaloneDbContextLeaseManager>();
        services.AddDbContext<PaymentsDbContext>((sp, options) =>
        {
            options.UseNpgsql(sharedDataSource);
            options.UseCadenceStandaloneTenancy(
                sp.GetRequiredService<ICurrentTenantAccessor>(),
                isRelational: true,
                leaseManagerResolver: () => sp.GetService<StandaloneDbContextLeaseManager>());
            options.AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance,
                leaseTracker);
        });

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

            using (tenantScope.Bind(alphaId))
            {
                var alphaRules = await db.ParticipantStipendRules.AsNoTracking()
                    .Where(r => r.StudyId == "STUDY-REBIND")
                    .Select(r => r.Id)
                    .ToListAsync(Ct);
                alphaRules.Should().Equal("cfg-rebind-alpha");
                leaseTracker.ConnectionsOpened.Should().Be(1);

                // Rebind mid-scope to Tenant Beta on the exact same leased DbContext connection.
                using (tenantScope.Bind(betaId))
                {
                    var betaRules = await db.ParticipantStipendRules.AsNoTracking()
                        .Where(r => r.StudyId == "STUDY-REBIND")
                        .Select(r => r.Id)
                        .ToListAsync(Ct);
                    betaRules.Should().Equal("cfg-rebind-beta");
                    leaseTracker.ConnectionsOpened.Should().Be(1, "mid-scope rebinding must reuse the existing leased connection");
                }

                // Restoring outer scope rebinds back to Tenant Alpha.
                var restoredAlphaRules = await db.ParticipantStipendRules.AsNoTracking()
                    .Where(r => r.StudyId == "STUDY-REBIND")
                    .Select(r => r.Id)
                    .ToListAsync(Ct);
                restoredAlphaRules.Should().Equal("cfg-rebind-alpha");
                leaseTracker.ConnectionsOpened.Should().Be(1);

                // Binding an inactive tenant mid-scope fails closed with InvalidOperationException and clears cadence.tenant_id.
                using (tenantScope.Bind(dynamicInactiveCode))
                {
                    Func<Task> invalidRead = () => db.ParticipantStipendRules.AsNoTracking().ToListAsync(Ct);
                    await invalidRead.Should().ThrowAsync<InvalidOperationException>();

                    await using var checkCmd = db.Database.GetDbConnection().CreateCommand();
                    checkCmd.CommandText = "SELECT COALESCE(pg_catalog.current_setting('cadence.tenant_id', true), '')";
                    string afterInvalid = (string)(await checkCmd.ExecuteScalarAsync(Ct))!;
                    afterInvalid.Should().BeEmpty("failing closed on an invalid mid-scope tenant must reset cadence.tenant_id");
                }
            }
        }

        // Simulate an unhandled mid-scope exception on a dedicated MaxPoolSize = 1 pool after leasing and binding Tenant Alpha, then verify pool reset on disposal.
        string singleConnPaymentsCs = new NpgsqlConnectionStringBuilder(paymentsModuleCs)
        {
            MaxPoolSize = 1,
            MinPoolSize = 0,
        }.ConnectionString;
        await using var singlePoolDataSource = NpgsqlDataSource.Create(singleConnPaymentsCs);
        var singlePoolServices = new ServiceCollection();
        singlePoolServices.AddSingleton(TestFieldProtection.CreateEncryptor());
        singlePoolServices.AddSingleton<ITenantCodeResolver>(resolver);
        singlePoolServices.AddSingleton<CurrentTenantAccessor>();
        singlePoolServices.AddSingleton<ICurrentTenantAccessor>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        singlePoolServices.AddSingleton<ITenantScope>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        singlePoolServices.AddScoped<StandaloneDbContextLeaseManager>();
        singlePoolServices.AddDbContext<PaymentsDbContext>((sp, options) =>
        {
            options.UseNpgsql(singlePoolDataSource);
            options.UseCadenceStandaloneTenancy(
                sp.GetRequiredService<ICurrentTenantAccessor>(),
                isRelational: true,
                leaseManagerResolver: () => sp.GetService<StandaloneDbContextLeaseManager>());
            options.AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance);
        });
        await using var singlePoolProvider = singlePoolServices.BuildServiceProvider();

        Func<Task> faultedScope = async () =>
        {
            await using var errorScope = singlePoolProvider.CreateAsyncScope();
            using var _ = errorScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(alphaId);
            var errorDb = errorScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var rows = await errorDb.ParticipantStipendRules.AsNoTracking()
                .Where(r => r.StudyId == "STUDY-REBIND")
                .ToListAsync(Ct);
            rows.Should().ContainSingle();
            throw new InvalidOperationException("Simulated mid-request failure while connection is leased");
        };
        await faultedScope.Should().ThrowAsync<InvalidOperationException>();

        // MaxPoolSize = 1 guarantees this raw connection reuses the exact pooled physical session.
        await using var rawPooled = await singlePoolDataSource.OpenConnectionAsync(Ct);
        await using var verifyResetCmd = rawPooled.CreateCommand();
        verifyResetCmd.CommandText = "SELECT COALESCE(pg_catalog.current_setting('cadence.tenant_id', true), '')";
        string pooledTenant = (string)(await verifyResetCmd.ExecuteScalarAsync(Ct))!;
        pooledTenant.Should().BeEmpty("disposing a faulted scoped DbContext must still reset cadence.tenant_id before returning the connection to NpgsqlDataSource");
    }

    [Fact]
    [GxPRequirement("PRD-KNB-001")]
    public async Task KnowledgeContextualHelp_ExecutesAtMostOneTenantLookupOnColdCache_ZeroOnWarmCache_AndOneDbContextLease()
    {
        database.SkipIfUnavailable();

        await MigrateAndVerifySchemaAsync<KnowledgeDbContext>(database, KnowledgeDbContextFactory.Schema, Ct);
        string cadenceAppConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);

        using var wireRecorder = new WireSqlRecorder();
        var leaseTracker = new LeaseTrackingInterceptor();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 27, 13, 0, 0, TimeSpan.Zero));

        await using var knowledgeHost = new InstrumentedCadenceHost<Cadence.Apps.Knowledge.Program, KnowledgeDbContext>(
            cadenceAppConnString,
            KnowledgeDbContextFactory.Schema,
            wireRecorder,
            leaseTracker,
            clock);

        using var client = knowledgeHost.CreateSignedClient(new GatewayIdentity(
            UserId: "cra-knowledge-1",
            Roles: ["CRA"],
            SiteIds: ["SITE-01"],
            TenantId: "tenant-alpha",
            StudyIds: ["*"],
            ProductFamilies: TestSecurityContext.AllProductFamilies));

        wireRecorder.Clear();
        leaseTracker.Reset();

        // Request 1 (Cold cache + initial KnowledgeCatalogSeeder run for tenant-alpha):
        var coldResp = await client.GetAsync("/api/v1/knowledge/contextual-help?route=/studies/STUDY-1/overview&persona=CRA", Ct);
        coldResp.StatusCode.Should().Be(HttpStatusCode.OK, await coldResp.Content.ReadAsStringAsync(Ct));
        await leaseTracker.WaitForConnectionsClosedAsync(1, Ct);
        wireRecorder.CountContaining("tenant_id_by_code").Should().BeLessThanOrEqualTo(1, "cold-cache contextual-help + seeder must resolve tenant_id_by_code at most once");
        leaseTracker.ConnectionsOpened.Should().Be(1, "contextual-help must hold a single KnowledgeDbContext connection lease");
        wireRecorder.CountContaining("set_config('cadence.tenant_id'").Should().Be(2, "cold-cache contextual-help + seeder executes 1 bind on open + 1 reset on close");

        // Request 2 (Warm 5-second cache):
        wireRecorder.Clear();
        leaseTracker.Reset();

        var warmResp = await client.GetAsync("/api/v1/knowledge/contextual-help?route=/studies/STUDY-1/overview&persona=CRA", Ct);
        warmResp.StatusCode.Should().Be(HttpStatusCode.OK, await warmResp.Content.ReadAsStringAsync(Ct));
        await leaseTracker.WaitForConnectionsClosedAsync(1, Ct);

        wireRecorder.CountContaining("tenant_id_by_code").Should().Be(0, "warm-cache contextual-help must execute zero kernel.tenant_id_by_code queries");
        leaseTracker.ConnectionsOpened.Should().Be(1, "contextual-help must reuse a single KnowledgeDbContext connection lease");
        wireRecorder.CountContaining("set_config('cadence.tenant_id'").Should().Be(2, "one leased scope executes 1 bind on open + 1 reset on close (2 + Q)");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task CtmsMilestoneAndSignedGreenlightCertify_AndPaymentsFortySiteBatchCompile_ReuseSingleLeaseAndPreserveAuditHashes()
    {
        database.SkipIfUnavailable();

        await MigrateAndVerifySchemaAsync<CtmsDbContext>(database, CtmsDbContextFactory.Schema, Ct);
        await MigrateAndVerifySchemaAsync<PaymentsDbContext>(database, PaymentsDbContextFactory.Schema, Ct);

        string cadenceAppConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);
        var now = new DateTimeOffset(2026, 11, 27, 14, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);

        // --- Part 1: CTMS PUT /site-milestones/{id} and signed POST /startup/sites/{siteId}/greenlight/certify ---
        using var ctmsWire = new WireSqlRecorder();
        var ctmsLeases = new LeaseTrackingInterceptor();
        await using var ctmsHost = new InstrumentedCadenceHost<Cadence.Apps.Ctms.Program, CtmsDbContext>(
            cadenceAppConnString,
            CtmsDbContextFactory.Schema,
            ctmsWire,
            ctmsLeases,
            clock);

        using var ctmsClient = ctmsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "cra-lead-1",
            Roles: ["Clinical Operations Lead", "CRA", "sysadmin"],
            SiteIds: ["SITE-GL-01"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-GL-01"],
            ProductFamilies: TestSecurityContext.AllProductFamilies));

        // Create initial milestone via HTTP (warms the 5s tenant cache for tenant-alpha).
        using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/site-milestones")
        {
            Content = JsonContent.Create(new SiteMilestoneCreateDto(
                Site: "SITE-GL-01",
                Study: "STUDY-GL-01",
                MilestoneType: "SIV",
                PlannedDate: now.AddDays(7),
                ActualDate: null,
                Status: "ACTIVE",
                ReasonForChange: "Initial SIV schedule")),
        })
        {
            createReq.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, "Initial SIV schedule");
            var createResp = await ctmsClient.SendAsync(createReq, Ct);
            createResp.StatusCode.Should().Be(HttpStatusCode.Created, await createResp.Content.ReadAsStringAsync(Ct));
            var createdDto = (await createResp.Content.ReadFromJsonAsync<SiteMilestoneResponseDto>(cancellationToken: Ct))!;
            await ctmsLeases.WaitForConnectionsClosedAsync(1, Ct);

            // Measure PUT /api/v1/ctms/site-milestones/{milestone_id} on warm cache.
            ctmsWire.Clear();
            ctmsLeases.Reset();
            clock.Advance(TimeSpan.FromSeconds(1));

            using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/ctms/site-milestones/{createdDto.Id}")
            {
                Content = JsonContent.Create(new SiteMilestoneUpdateDto(
                    Status: "COMPLETED",
                    PlannedDate: now.AddDays(7),
                    ActualDate: clock.UtcNow,
                    ReasonForChange: "SIV completed on schedule")),
            };
            updateReq.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, "SIV completed on schedule");
            var updateResp = await ctmsClient.SendAsync(updateReq, Ct);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK, await updateResp.Content.ReadAsStringAsync(Ct));
            var updatedDto = (await updateResp.Content.ReadFromJsonAsync<SiteMilestoneResponseDto>(cancellationToken: Ct))!;
            await ctmsLeases.WaitForConnectionsClosedAsync(1, Ct);

            updatedDto.VersionIndex.Should().Be(2, "PUT site-milestone must increment VersionIndex once");
            ctmsWire.CountContaining("tenant_id_by_code").Should().Be(0, "warm-cache milestone update must issue zero tenant lookups");
            ctmsLeases.ConnectionsOpened.Should().Be(1, "milestone update must execute on a single CtmsDbContext connection lease");
            ctmsWire.CountContaining("set_config('cadence.tenant_id'").Should().Be(2, "milestone update executes 1 bind on open + 1 reset on close (2 + Q + 2W)");
        }

        // Seed a 6-facet compliant greenlight gate for SITE-GL-01 in tenant-alpha.
        await using (var seedScope = ctmsHost.Services.CreateAsyncScope())
        {
            using var _ = seedScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-alpha");
            using var actor = seedScope.ServiceProvider.GetRequiredService<IAuditActorScope>().Begin("svc:ctms-seed", "Seed compliant gate");
            var ctmsDb = seedScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            ctmsDb.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate("GATE-GL-01", CtmsGreenlightStatus.Pending)
            {
                StudyId = "STUDY-GL-01",
                SiteId = "SITE-GL-01",
                SiteName = "Metro Clinical Site 01",
                CountryCode = "US",
                ReadinessPercentage = 100,
                FacetRegulatoryIrb = true,
                FacetCtaBudget = true,
                FacetEisfBaseline = true,
                FacetDoaLog = true,
                FacetStaffTraining = true,
                FacetIpRelease = true,
                CreatedAt = clock.UtcNow.UtcDateTime,
                CreatedBy = "svc:ctms-seed",
                ReasonForChange = "Seed compliant gate",
                VersionIndex = 1,
            });
            await ctmsDb.SaveChangesAsync(Ct);
        }

        // Measure signed POST /api/v1/ctms/startup/sites/{siteId}/greenlight/certify (including PostgresSigTokenUseStore).
        ctmsWire.Clear();
        ctmsLeases.Reset();
        clock.Advance(TimeSpan.FromSeconds(1));

        const string certifyPath = "/api/v1/ctms/startup/sites/SITE-GL-01/greenlight/certify";
        string sigToken = TestSecurityContext.CreateSigToken(
            userId: "cra-lead-1",
            action: certifyPath,
            semanticAction: "ctms.greenlight.certify",
            roles: ["Clinical Operations Lead", "CRA", "sysadmin"],
            issuedAt: clock.UtcNow);

        using (var certifyReq = new HttpRequestMessage(HttpMethod.Post, $"{certifyPath}?study_id=STUDY-GL-01&country_code=US"))
        {
            certifyReq.Headers.TryAddWithoutValidation(GatewayHeaders.SigToken, sigToken);
            certifyReq.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, "All 6 site startup facets verified");
            var certifyResp = await ctmsClient.SendAsync(certifyReq, Ct);
            certifyResp.StatusCode.Should().Be(HttpStatusCode.OK, await certifyResp.Content.ReadAsStringAsync(Ct));
        }

        await ctmsLeases.WaitForConnectionsClosedAsync(1, Ct);
        ctmsWire.CountContaining("tenant_id_by_code").Should().Be(0, "warm-cache signed greenlight certification must issue zero tenant lookups");
        ctmsLeases.ConnectionsOpened.Should().Be(1, "PostgresSigTokenUseStore and CertifySiteGreenlightAsync must share the single request CtmsDbContext connection lease");
        ctmsWire.CountContaining("set_config('cadence.tenant_id'").Should().Be(2, "signed greenlight certification executes 1 bind on open + 1 reset on close");

        // Replaying the consumed step-up token must still be rejected (ADR-2515).
        using (var replayReq = new HttpRequestMessage(HttpMethod.Post, $"{certifyPath}?study_id=STUDY-GL-01&country_code=US"))
        {
            replayReq.Headers.TryAddWithoutValidation(GatewayHeaders.SigToken, sigToken);
            replayReq.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, "Replay attempt");
            var replayResp = await ctmsClient.SendAsync(replayReq, Ct);
            replayResp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        // Verify SHA-256 Merkle chain integrity across every CtmsDbContext AuditLogEntry written in tenant-alpha.
        await VerifyAuditChainHashesAsync<CtmsDbContext>(ctmsHost.Services, "tenant-alpha", Ct);

        // --- Part 2: Payments POST /api/v1/payments/invoices/batch-compile across 40 sites ---
        using var paymentsWire = new WireSqlRecorder();
        var paymentsLeases = new LeaseTrackingInterceptor();
        var paymentsClock = new FakeClock(now);
        await using var paymentsHost = new InstrumentedCadenceHost<Cadence.Apps.Payments.Program, PaymentsDbContext>(
            cadenceAppConnString,
            PaymentsDbContextFactory.Schema,
            paymentsWire,
            paymentsLeases,
            paymentsClock);

        const string batchStudyId = "STUDY-BATCH-40";
        var siteIds = Enumerable.Range(1, 40).Select(i => $"SITE-B{i:D2}").ToArray();

        await using (var seedScope = paymentsHost.Services.CreateAsyncScope())
        {
            using var _ = seedScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-alpha");
            using var actor = seedScope.ServiceProvider.GetRequiredService<IAuditActorScope>().Begin("svc:payments-seed", "Seed 40 site payables");
            var paymentsDb = seedScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            foreach (string siteId in siteIds)
            {
                paymentsDb.Payables.Add(new Payable
                {
                    Id = $"PAY-{siteId}",
                    StudyId = batchStudyId,
                    SiteId = siteId,
                    ContractId = $"CTR-{siteId}",
                    SubjectId = $"SUBJ-{siteId}",
                    VisitCode = "V1",
                    ProcedureCode = "PROC-01",
                    ProcedureName = "Baseline Assessment",
                    TriggerType = "VISIT_COMPLETED",
                    TriggerEventId = $"EVT-{siteId}",
                    BaseAmount = 500m,
                    OverheadAmount = 100m,
                    GrossAmount = 600m,
                    WithholdingRate = 10m,
                    WithholdingAmount = 60m,
                    NetAmount = 540m,
                    Currency = "USD",
                    Status = "VERIFIED",
                    IsSdvGated = false,
                    CreatedAt = paymentsClock.UtcNow,
                    CreatedBy = "svc:payments-seed",
                    UpdatedAt = paymentsClock.UtcNow,
                    ReasonForChange = "Seed verified payable",
                    VersionIndex = 1,
                });
            }

            await paymentsDb.SaveChangesAsync(Ct);
        }

        // Advance clock > 5s so batch-compile starts on a cold resolver cache and proves <= 1 tenant lookup across all 40 sites.
        paymentsClock.Advance(TimeSpan.FromSeconds(10));
        paymentsWire.Clear();
        paymentsLeases.Reset();

        using var financeClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-batch-officer",
            Roles: ["Finance Officer", "sysadmin"],
            SiteIds: siteIds,
            TenantId: "tenant-alpha",
            StudyIds: [batchStudyId],
            ProductFamilies: TestSecurityContext.AllProductFamilies));

        using var batchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/invoices/batch-compile")
        {
            Content = JsonContent.Create(new Cadence.Apps.Payments.Endpoints.BatchBillingCycleCompilationRequest(
                StudyId: batchStudyId,
                BillingCycle: "2026-11",
                AutoApprove: true,
                SiteIds: [.. siteIds])),
        };
        batchReq.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, "Monthly 40-site billing batch compilation");

        var batchResp = await financeClient.SendAsync(batchReq, Ct);
        batchResp.StatusCode.Should().Be(HttpStatusCode.Created, await batchResp.Content.ReadAsStringAsync(Ct));

        using var batchDoc = JsonDocument.Parse(await batchResp.Content.ReadAsStringAsync(Ct));
        batchDoc.RootElement.GetProperty("total_sites_processed").GetInt32().Should().Be(40);
        batchDoc.RootElement.GetProperty("successful_sites_count").GetInt32().Should().Be(40);
        batchDoc.RootElement.GetProperty("total_invoices_generated").GetInt32().Should().Be(40);

        await paymentsLeases.WaitForConnectionsClosedAsync(1, Ct);
        paymentsWire.CountContaining("tenant_id_by_code").Should().BeLessThanOrEqualTo(1, "40-site batch compile must reuse one resolved tenant UUID");
        paymentsLeases.ConnectionsOpened.Should().Be(1, "40-site batch compile must reuse one PaymentsDbContext connection lease instead of 329 pool leases");
        paymentsWire.CountContaining("set_config('cadence.tenant_id'").Should().Be(2, "40-site batch compile executes 1 bind on open + 1 reset on close across all 40 sites");

        await using var verifyPaymentsScope = paymentsHost.Services.CreateAsyncScope();
        var tenantScope = verifyPaymentsScope.ServiceProvider.GetRequiredService<ITenantScope>();
        var verifyDb = verifyPaymentsScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        using (tenantScope.Bind("tenant-alpha"))
        {
            (await verifyDb.SelfBillingInvoices.AsNoTracking().CountAsync(i => i.StudyId == batchStudyId, Ct)).Should().Be(40);
            (await verifyDb.PaymentAuditLogs.AsNoTracking().CountAsync(Ct)).Should().Be(41);
        }

        using (tenantScope.Bind("tenant-beta"))
        {
            (await verifyDb.SelfBillingInvoices.AsNoTracking().CountAsync(i => i.StudyId == batchStudyId, Ct)).Should().Be(0);
        }
    }

    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task EtmfAndFileshareWorkers_WithTwoHundredActiveTenants_IssueZeroPerTenantSelectsWhenIdle_AndProcessOnlyQueuedTenantsUnderForcedRls()
    {
        database.SkipIfUnavailable();

        await MigrateAndVerifySchemaAsync<EtmfDbContext>(database, EtmfDbContextFactory.Schema, Ct);
        await MigrateAndVerifySchemaAsync<FileshareDbContext>(database, FileshareDbContextFactory.Schema, Ct);

        // Provision 200 active scale tenants in kernel.tenants.
        var scaleTenants = Enumerable.Range(1, 200)
            .Select(i =>
            {
                string code = $"tenant-scale-{i:D3}";
                return (Code: code, Id: DeterministicTenantCodeResolver.ComputeDeterministicGuid(code));
            })
            .ToArray();

        await using (var adminConn = new NpgsqlConnection(database.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            foreach (var (code, id) in scaleTenants)
            {
                await using var ins = adminConn.CreateCommand();
                ins.CommandText = """
                    INSERT INTO "kernel"."tenants" (id, code, name, is_active)
                    VALUES (@id, @code, @code, true)
                    ON CONFLICT (code) DO UPDATE SET is_active = true;
                    """;
                ins.Parameters.AddWithValue("id", id);
                ins.Parameters.AddWithValue("code", code);
                await ins.ExecuteNonQueryAsync(Ct);
            }
        }

        string cadenceAppConnString = BuildCadenceAppConnectionString(database.AdminDatabaseConnectionString!);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.Zero));

        // Build worker DI containers running strictly as cadence_app (NOSUPERUSER NOBYPASSRLS).
        using var etmfWire = new WireSqlRecorder();
        await using var etmfServices = BuildStandaloneWorkerServiceProvider<EtmfDbContext>(
            cadenceAppConnString,
            EtmfDbContextFactory.Schema,
            etmfWire,
            clock,
            opts => new EtmfDbContext(opts));

        using var fileshareWire = new WireSqlRecorder();
        var transcodeMock = Substitute.For<IMediaTranscoderProcessor>();
        transcodeMock.TranscodeToHlsAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .Returns(call => $"hls/{call.Arg<FileRecord>().Id}/master.m3u8");
        var convertMock = Substitute.For<IDocumentConverterProcessor>();
        convertMock.ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .Returns(call => $"pdf/{call.Arg<FileRecord>().Id}.pdf");

        await using var fileshareServices = BuildStandaloneWorkerServiceProvider<FileshareDbContext>(
            cadenceAppConnString,
            FileshareDbContextFactory.Schema,
            fileshareWire,
            clock,
            opts => new FileshareDbContext(opts),
            extra =>
            {
                extra.AddScoped(_ => transcodeMock);
                extra.AddScoped(_ => convertMock);
            });

        // 1. With 200 active tenants and ZERO queued/pending rows, idle workers execute ZERO per-tenant SELECTs and ZERO set_config binds.
        etmfWire.Clear();
        var exportQueue = new EtmfExportQueue();
        var exportWorker = new EtmfExportWorker(etmfServices, exportQueue, NullLogger<EtmfExportWorker>.Instance);
        using (var idleCts = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            await exportWorker.StartAsync(idleCts.Token);
            for (int waitAttempt = 0; waitAttempt < 60 && etmfWire.CountContaining("queued_export_tenant_ids") == 0; waitAttempt++)
            {
                await Task.Delay(50, Ct);
            }

            await Task.Delay(150, Ct);
            await idleCts.CancelAsync();
            await exportWorker.StopAsync(Ct);
        }

        etmfWire.CountContaining("queued_export_tenant_ids").Should().Be(1, "startup recovery runs one narrow directory lookup");
        etmfWire.CountContaining("tenant_ids()").Should().Be(0, "EtmfExportWorker must not enumerate all 200 active tenants");
        etmfWire.Commands.Where(c => c.Contains("tmf_export_jobs", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty(
            "idle EtmfExportWorker must issue zero per-tenant SELECT statements against tmf_export_jobs");

        fileshareWire.Clear();
        var workerOptions = Options.Create(new FileshareWorkerOptions { BatchSize = 10, MaxConcurrentJobs = 2 });
        var mediaWorker = new MediaTranscoderWorker(
            fileshareServices.GetRequiredService<IServiceScopeFactory>(),
            workerOptions,
            NullLogger<MediaTranscoderWorker>.Instance);
        var docWorker = new DocumentConversionWorker(
            fileshareServices.GetRequiredService<IServiceScopeFactory>(),
            workerOptions,
            NullLogger<DocumentConversionWorker>.Instance);

        await mediaWorker.ProcessPendingTranscodesAsync(Ct);
        await docWorker.ProcessPendingConversionsAsync(Ct);

        fileshareWire.CountContaining("pending_transcode_tenant_ids").Should().Be(1);
        fileshareWire.CountContaining("pending_conversion_tenant_ids").Should().Be(1);
        fileshareWire.CountContaining("tenant_ids()").Should().Be(0, "Fileshare workers must not enumerate all 200 active tenants");
        fileshareWire.Commands.Where(c => c.Contains("FROM \"file_records\"", StringComparison.OrdinalIgnoreCase)
            || c.Contains("FROM file_records", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty(
            "idle Fileshare workers must issue zero per-tenant SELECT statements against file_records");

        // 2. Seed queued/pending work in ONLY 2 of the 200 active tenants (plus non-matching rows in a 3rd tenant)
        // and verify cadence_app still cannot bypass FORCE ROW LEVEL SECURITY by setting cadence.pending_work_directory directly.
        Guid tenantA = scaleTenants[6].Id;   // tenant-scale-007
        Guid tenantB = scaleTenants[141].Id; // tenant-scale-142
        Guid tenantNoise = scaleTenants[98].Id; // tenant-scale-099 (completed / deleted noise rows)

        foreach ((Guid tid, string suffix) in new[] { (tenantA, "007"), (tenantB, "142") })
        {
            await using var scope = etmfServices.CreateAsyncScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tid);
            using var actor = scope.ServiceProvider.GetRequiredService<IAuditActorScope>().Begin("svc:etmf-seed", "Seed queued export");
            var etmfDb = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            etmfDb.TmfDocuments.Add(TmfDocument.CreateApproved(
                id: $"DOC-{suffix}",
                studyId: $"STUDY-{suffix}",
                filename: $"Protocol_{suffix}.pdf",
                createdBy: "svc:etmf-seed",
                createdAt: clock.UtcNow.UtcDateTime,
                reasonForChange: "Seed approved doc",
                siteId: "SITE-1",
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Protocol",
                content: "Approved content",
                mimeType: "application/pdf",
                protocolVersionIndex: 1));
            etmfDb.TmfExportJobs.Add(new TmfExportJob($"JOB-{suffix}")
            {
                StudyId = $"STUDY-{suffix}",
                RequestedBy = "cra.user",
                Status = "QUEUED",
                TotalDocuments = 1,
                ProcessedDocuments = 0,
                CreatedAt = clock.UtcNow.UtcDateTime,
                ReasonForChange = "Queued binder export",
            });
            await etmfDb.SaveChangesAsync(Ct);
        }

        await using (var noiseEtmfScope = etmfServices.CreateAsyncScope())
        {
            using var _ = noiseEtmfScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tenantNoise);
            using var actor = noiseEtmfScope.ServiceProvider.GetRequiredService<IAuditActorScope>().Begin("svc:etmf-seed", "Seed completed export");
            var etmfDb = noiseEtmfScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            etmfDb.TmfExportJobs.Add(new TmfExportJob("JOB-NOISE-DONE")
            {
                StudyId = "STUDY-NOISE",
                RequestedBy = "cra.user",
                Status = "COMPLETED",
                TotalDocuments = 1,
                ProcessedDocuments = 1,
                CreatedAt = clock.UtcNow.UtcDateTime,
                ReasonForChange = "Already completed export",
            });
            await etmfDb.SaveChangesAsync(Ct);
        }

        foreach ((Guid tid, string suffix) in new[] { (tenantA, "007"), (tenantB, "142") })
        {
            await using var scope = fileshareServices.CreateAsyncScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tid);
            using var actor = scope.ServiceProvider.GetRequiredService<IAuditActorScope>().Begin("svc:fileshare-seed", "Seed pending files");
            var fsDb = scope.ServiceProvider.GetRequiredService<FileshareDbContext>();
            fsDb.FileRecords.AddRange(
                new FileRecord($"VID-{suffix}")
                {
                    Filename = $"video_{suffix}.mp4",
                    ObjectKey = $"videos/{suffix}.mp4",
                    UploadedAt = clock.UtcNow,
                    TranscodeStatus = "PENDING",
                    ConversionStatus = "NONE",
                    ReasonForChange = "Seed pending video",
                },
                new FileRecord($"DOCX-{suffix}")
                {
                    Filename = $"doc_{suffix}.docx",
                    ObjectKey = $"docs/{suffix}.docx",
                    UploadedAt = clock.UtcNow,
                    TranscodeStatus = "NONE",
                    ConversionStatus = "PENDING",
                    ReasonForChange = "Seed pending document",
                });
            await fsDb.SaveChangesAsync(Ct);
        }

        await using (var noiseFsScope = fileshareServices.CreateAsyncScope())
        {
            using var _ = noiseFsScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tenantNoise);
            using var actor = noiseFsScope.ServiceProvider.GetRequiredService<IAuditActorScope>().Begin("svc:fileshare-seed", "Seed deleted pending file");
            var fsDb = noiseFsScope.ServiceProvider.GetRequiredService<FileshareDbContext>();
            fsDb.FileRecords.Add(new FileRecord("DELETED-NOISE")
            {
                Filename = "deleted.mp4",
                ObjectKey = "videos/deleted.mp4",
                UploadedAt = clock.UtcNow,
                TranscodeStatus = "PENDING",
                ConversionStatus = "PENDING",
                IsDeleted = true,
                ReasonForChange = "Soft-deleted file must be excluded",
            });
            await fsDb.SaveChangesAsync(Ct);
        }

        // Security check: cadence_app manually setting cadence.pending_work_directory = 'on' STILL sees 0 rows on underlying tables!
        await using (var rlsProbeConn = new NpgsqlConnection(cadenceAppConnString))
        {
            await rlsProbeConn.OpenAsync(Ct);
            await using var probeCmd = rlsProbeConn.CreateCommand();
            probeCmd.CommandText = """
                SELECT pg_catalog.set_config('cadence.pending_work_directory', 'on', false);
                SELECT
                    (SELECT COUNT(*) FROM "etmf"."tmf_export_jobs") +
                    (SELECT COUNT(*) FROM "fileshare"."file_records");
                """;
            await using var reader = await probeCmd.ExecuteReaderAsync(Ct);
            await reader.NextResultAsync(Ct);
            (await reader.ReadAsync(Ct)).Should().BeTrue();
            reader.GetInt64(0).Should().Be(0, "cadence_app is not a member of the table owner and must never bypass FORCE ROW LEVEL SECURITY via cadence.pending_work_directory");
        }

        // Run workers and verify ONLY the 2 pending tenants are queried and processed to COMPLETED.
        etmfWire.Clear();
        var recoveryWorker = new EtmfExportWorker(etmfServices, new EtmfExportQueue(), NullLogger<EtmfExportWorker>.Instance);
        using (var recoveryCts = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            await recoveryWorker.StartAsync(recoveryCts.Token);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(25, Ct);
                await using var checkScope = etmfServices.CreateAsyncScope();
                using var _ = checkScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tenantB);
                var db = checkScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
                if ((await db.TmfExportJobs.AsNoTracking().SingleAsync(j => j.Id == "JOB-142", Ct)).Status == "COMPLETED")
                {
                    break;
                }
            }

            await recoveryCts.CancelAsync();
            await recoveryWorker.StopAsync(Ct);
        }

        etmfWire.CountContaining("tenant_ids()").Should().Be(0);

        fileshareWire.Clear();
        await mediaWorker.ProcessPendingTranscodesAsync(Ct);
        await docWorker.ProcessPendingConversionsAsync(Ct);

        fileshareWire.CountContaining("tenant_ids()").Should().Be(0);

        foreach ((Guid tid, string suffix) in new[] { (tenantA, "007"), (tenantB, "142") })
        {
            await using var verifyEtmf = etmfServices.CreateAsyncScope();
            using var _ = verifyEtmf.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tid);
            var etmfDb = verifyEtmf.ServiceProvider.GetRequiredService<EtmfDbContext>();
            (await etmfDb.TmfExportJobs.AsNoTracking().SingleAsync(j => j.Id == $"JOB-{suffix}", Ct)).Status.Should().Be("COMPLETED");

            await using var verifyFs = fileshareServices.CreateAsyncScope();
            using var __ = verifyFs.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tid);
            var fsDb = verifyFs.ServiceProvider.GetRequiredService<FileshareDbContext>();
            (await fsDb.FileRecords.AsNoTracking().SingleAsync(f => f.Id == $"VID-{suffix}", Ct)).TranscodeStatus.Should().Be("COMPLETED");
            (await fsDb.FileRecords.AsNoTracking().SingleAsync(f => f.Id == $"DOCX-{suffix}", Ct)).ConversionStatus.Should().Be("COMPLETED");
        }
    }

    private static ServiceProvider BuildStandaloneWorkerServiceProvider<TContext>(
        string cadenceAppConnString,
        string schema,
        WireSqlRecorder wireRecorder,
        IClock clock,
        Func<DbContextOptions<TContext>, TContext> createContext,
        Action<IServiceCollection>? configureExtra = null)
        where TContext : DbContext
    {
        string moduleConnString = CadenceDatabaseExtensions.WithModuleSchema(cadenceAppConnString, schema);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<NpgsqlDataSource>(
            typeof(TContext),
            (_, _) => wireRecorder.CreateDataSource(moduleConnString));
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ITenantCodeResolver>(sp =>
            new PostgresTenantCodeResolver(
                sp.GetRequiredKeyedService<NpgsqlDataSource>(typeof(TContext)),
                sp.GetRequiredService<IClock>(),
                ownsDataSource: false));
        services.AddSingleton<CurrentTenantAccessor>();
        services.AddSingleton<ICurrentTenantAccessor>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<ITenantScope>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<IAuditActorScope, AmbientAuditActorScope>();
        services.AddScoped<StandaloneDbContextLeaseManager>();
        services.AddScoped(sp =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<TContext>();
            optionsBuilder.UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(typeof(TContext)));
            optionsBuilder.UseCadenceStandaloneTenancy(
                sp.GetRequiredService<ICurrentTenantAccessor>(),
                isRelational: true,
                leaseManagerResolver: () => sp.GetService<StandaloneDbContextLeaseManager>());
            optionsBuilder.AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance,
                new GxPAuditInterceptor(
                    () => AmbientAuditActorScope.Current?.Actor ?? "svc:worker-test",
                    sp.GetRequiredService<IClock>()));
            return createContext(optionsBuilder.Options);
        });
        configureExtra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static void AssertSyncResolve(PostgresTenantCodeResolver resolver, string tenantCode, Guid? expected) =>
        resolver.ResolveTenantId(tenantCode).Should().Be(expected);

    private static async Task VerifyAuditChainHashesAsync<TContext>(
        IServiceProvider rootServices,
        string tenantCode,
        CancellationToken ct)
        where TContext : DbContext
    {
        await using var scope = rootServices.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind(tenantCode);
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var entries = await db.Set<AuditLogEntry>().AsNoTracking()
            .OrderBy(e => e.EntityName)
            .ThenBy(e => e.Timestamp)
            .ThenBy(e => e.VersionIndex)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        entries.Should().NotBeEmpty();

        foreach (var chain in entries.GroupBy(e => e.EntityName))
        {
            string expectedPredecessor = string.Empty;
            foreach (var entry in chain)
            {
                entry.PredecessorHash.Should().Be(expectedPredecessor, $"Merkle chain for {chain.Key} must remain unbroken");
                string recomputed = AuditLogEntry.ComputeBlockHash(
                    entry.EntityName,
                    entry.EntityId,
                    entry.Action,
                    AuditLogEntry.NormalizeTimestamp(entry.Timestamp),
                    entry.UserId,
                    entry.VersionIndex,
                    entry.ChangesJson,
                    entry.PredecessorHash);
                entry.BlockHash.Should().Be(recomputed, $"SHA-256 BlockHash for {entry.EntityName}:{entry.EntityId} must match canonical Part 11 computation");
                expectedPredecessor = entry.BlockHash;
            }
        }
    }

    private sealed class InstrumentedCadenceHost<TProgram, TContext>(
        string cadenceAppConnectionString,
        string schema,
        WireSqlRecorder wireRecorder,
        LeaseTrackingInterceptor leaseTracker,
        IClock clock)
        : WebApplicationFactory<TProgram>
        where TProgram : class
        where TContext : DbContext
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestFieldProtectionKeys();
            builder.UseSetting("ConnectionStrings:cadence", cadenceAppConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureTestServices(services =>
            {
                string moduleConnString = CadenceDatabaseExtensions.WithModuleSchema(cadenceAppConnectionString, schema);
                services.RemoveAllKeyed<NpgsqlDataSource>(typeof(TContext));
                services.AddKeyedSingleton<NpgsqlDataSource>(
                    typeof(TContext),
                    (_, _) => wireRecorder.CreateDataSource(moduleConnString));

                services.RemoveAll<IClock>();
                services.AddSingleton(clock);

                services.ConfigureDbContext<TContext>(options => options.AddInterceptors(leaseTracker));
            });
            base.ConfigureWebHost(builder);
        }
    }

    private sealed class LeaseTrackingInterceptor : DbConnectionInterceptor
    {
        private int _connectionsOpened;
        private int _connectionsClosed;

        public int ConnectionsOpened => Volatile.Read(ref _connectionsOpened);

        public int ConnectionsClosed => Volatile.Read(ref _connectionsClosed);

        public void Reset()
        {
            Interlocked.Exchange(ref _connectionsOpened, 0);
            Interlocked.Exchange(ref _connectionsClosed, 0);
        }

        public async Task WaitForConnectionsClosedAsync(int expectedClosed, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 200 && ConnectionsClosed < expectedClosed; attempt++)
            {
                await Task.Delay(10, ct);
            }
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            Interlocked.Increment(ref _connectionsOpened);
            base.ConnectionOpened(connection, eventData);
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectionsOpened);
            return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        }

        public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
        {
            Interlocked.Increment(ref _connectionsClosed);
            base.ConnectionClosed(connection, eventData);
        }

        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
        {
            Interlocked.Increment(ref _connectionsClosed);
            return base.ConnectionClosedAsync(connection, eventData);
        }
    }

    private sealed class WireSqlRecorder : IDisposable
    {
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly ILoggerFactory _loggerFactory;

        public WireSqlRecorder()
        {
            _loggerFactory = LoggerFactory.Create(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(new CommandLoggerProvider(_commands));
            });
        }

        public IReadOnlyCollection<string> Commands => _commands.ToArray();

        public NpgsqlDataSource CreateDataSource(string connectionString)
        {
            var dsBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dsBuilder.UseLoggerFactory(_loggerFactory);
            return dsBuilder.Build();
        }

        public int CountContaining(string substring) =>
            _commands.Count(sql => sql.Contains(substring, StringComparison.OrdinalIgnoreCase));

        public void Clear()
        {
            while (_commands.TryDequeue(out _))
            {
                // Drain recorded SQL queue.
            }
        }

        public void Dispose() => _loggerFactory.Dispose();

        private sealed class CommandLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) =>
                string.Equals(categoryName, "Npgsql.Command", StringComparison.Ordinal)
                    ? new CommandLogger(sink)
                    : NullLogger.Instance;

            public void Dispose()
            {
                // Nothing to dispose on in-memory queue sink.
            }
        }

        private sealed class CommandLogger(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                // EventId 2001 = CommandExecuted in Npgsql.Command (emitted once per executed wire command).
                if (eventId.Id == 2001)
                {
                    sink.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
