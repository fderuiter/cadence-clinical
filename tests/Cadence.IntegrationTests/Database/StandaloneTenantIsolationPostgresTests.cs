using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

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
        // in PaymentsCaller cannot see or mutate another tenant's wallets or site configs, and inactive/missing tenants get 403.
        await using var paymentsHost = new CadenceAppPaymentsHost(cadenceAppConnString);

        using var alphaSponsorClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-alpha",
            Roles: ["Finance Officer", "sysadmin"],
            SiteIds: [],
            TenantId: "tenant-alpha",
            StudyIds: ["*"]));

        using var betaSponsorClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
            UserId: "fin-beta",
            Roles: ["Finance Officer", "sysadmin"],
            SiteIds: [],
            TenantId: "tenant-beta",
            StudyIds: ["*"]));

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
            StudyIds: ["*"]));
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
            Password = "cadence_app",
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
}
