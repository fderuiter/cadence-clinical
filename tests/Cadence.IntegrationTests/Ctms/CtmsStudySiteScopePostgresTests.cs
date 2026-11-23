using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Ctms;

[Trait("Category", "Integration")]
public sealed class CtmsStudySiteScopePostgresTests(CtmsPostgresApiFactory factory) : IClassFixture<CtmsPostgresApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PostgresHost_EnforcesAllTenStudyAndSiteScopeIsolationChecks()
    {
        factory.SkipIfUnavailable();

        var now = new DateTimeOffset(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);
        string visitBetaId = $"VISIT-BETA-{Guid.NewGuid():N}"[..20];

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();

            db.CtmsDelegations.Add(new CtmsDelegation("DEL-BETA-ONLY")
            {
                StudyId = "STUDY-BETA",
                SiteId = "SITE-BETA-DELEG",
                StaffUserId = "staff-beta-only",
                TaskCodes = "[\"CRF_DATA_ENTRY\"]",
                StartDate = "2026-11-01",
                IsActive = true,
                SignedOff = false,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "pi-beta",
                ReasonForChange = "Initial beta delegation",
                VersionIndex = 1,
            });

            db.CtmsSiteGreenlightGates.AddRange(
                new CtmsSiteGreenlightGate("GATE-ALPHA-SHARED")
                {
                    StudyId = "STUDY-ALPHA",
                    SiteId = "SITE-SHARED",
                    SiteName = "Shared Site Alpha",
                    CountryCode = "US",
                    GreenlightCertifiedBy = "Dr-Alpha-PI-Marker",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "admin",
                    ReasonForChange = "Seed alpha gate",
                    VersionIndex = 1,
                },
                new CtmsSiteGreenlightGate("GATE-BETA-SHARED")
                {
                    StudyId = "STUDY-BETA",
                    SiteId = "SITE-SHARED",
                    SiteName = "Shared Site Beta",
                    CountryCode = "US",
                    GreenlightCertifiedBy = "Dr-Beta-PI-Marker",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "admin",
                    ReasonForChange = "Seed beta gate",
                    VersionIndex = 1,
                },
                new CtmsSiteGreenlightGate("GATE-SITE-1")
                {
                    StudyId = "STUDY-ALPHA",
                    SiteId = "SITE-1",
                    SiteName = "Alpha Site 1",
                    CountryCode = "US",
                    GreenlightCertifiedBy = "Dr. Alpha One",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "admin",
                    ReasonForChange = "Seed site 1",
                    VersionIndex = 1,
                },
                new CtmsSiteGreenlightGate("GATE-SITE-101")
                {
                    StudyId = "STUDY-BETA",
                    SiteId = "SITE-101",
                    SiteName = "Beta Site 101",
                    CountryCode = "US",
                    GreenlightCertifiedBy = "Dr. Beta 101",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "admin",
                    ReasonForChange = "Seed site 101",
                    VersionIndex = 1,
                });

            db.CtmsDelegations.AddRange(
                new CtmsDelegation("DEL-ALPHA-SHARED")
                {
                    StudyId = "STUDY-ALPHA",
                    SiteId = "SITE-SHARED",
                    StaffUserId = "staff-alpha-shared",
                    TaskCodes = "[\"CRF_DATA_ENTRY\"]",
                    StartDate = "2026-11-01",
                    IsActive = true,
                    SignedOff = false,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "pi-alpha-shared",
                    ReasonForChange = "Seed alpha shared delegation",
                    VersionIndex = 1,
                },
                new CtmsDelegation("DEL-BETA-SHARED")
                {
                    StudyId = "STUDY-BETA",
                    SiteId = "SITE-SHARED",
                    StaffUserId = "staff-beta-shared-marker",
                    TaskCodes = "[\"IP_ACCOUNTABILITY\"]",
                    StartDate = "2026-11-02",
                    IsActive = true,
                    SignedOff = false,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "pi-beta-shared",
                    ReasonForChange = "Seed beta shared delegation",
                    VersionIndex = 1,
                });

            db.CtmsAuditLogs.AddRange(
                new CtmsAuditLog("AUD-SITE-1")
                {
                    Timestamp = now,
                    UserId = "pi-alpha-site1",
                    UserRole = "investigator",
                    Action = "DOA_DELEGATE",
                    Details = "Delegated tasks [CRF_DATA_ENTRY] to staff staff-alpha-site1-marker at site SITE-1 in study STUDY-ALPHA.",
                    StudyId = "STUDY-ALPHA",
                    SiteId = "SITE-1",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "pi-alpha-site1",
                    ReasonForChange = "Seed site 1 audit",
                    VersionIndex = 1,
                },
                new CtmsAuditLog("AUD-SITE-101")
                {
                    Timestamp = now,
                    UserId = "pi-beta-101",
                    UserRole = "investigator",
                    Action = "DOA_DELEGATE",
                    Details = "Delegated tasks [CRF_DATA_ENTRY] to staff staff-beta-101-marker at site SITE-101 in study STUDY-BETA.",
                    StudyId = "STUDY-BETA",
                    SiteId = "SITE-101",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = "pi-beta-101",
                    ReasonForChange = "Seed site 101 audit",
                    VersionIndex = 1,
                });

            db.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit(visitBetaId)
            {
                StudyId = "STUDY-BETA",
                SiteId = "SITE-101",
                CraId = "cra-beta-1",
                VisitType = "IMV",
                ScheduledDate = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "cra-beta-1",
                ReasonForChange = "Seed beta visit",
                VersionIndex = 1,
            });

            db.CtmsGeneratedLetters.Add(new CtmsGeneratedLetter("LTR-BETA-NOKEY", CtmsGeneratedLetterStatus.Draft)
            {
                StudyId = string.Empty,
                SiteId = string.Empty,
                VisitId = visitBetaId,
                LetterType = "confirmation_letter",
                RenderedContent = "CONFIDENTIAL BETA LETTER",
                PdfObjectKey = null,
                IsSigned = false,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "cra-beta-1",
                ReasonForChange = "Draft letter without PdfObjectKey",
                VersionIndex = 1,
            });

            await db.SaveChangesAsync(Ct);
        }

        using var alphaDm = factory.CreateSignedClient("Data Manager", "dm-alpha-1", ["STUDY-ALPHA"], []);
        using var alphaPiSite1 = factory.CreateSignedClient("Principal Investigator", "pi-alpha-site1", ["STUDY-ALPHA"], ["SITE-1"]);
        using var alphaPiShared = factory.CreateSignedClient("Principal Investigator", "pi-alpha-shared", ["STUDY-ALPHA"], ["SITE-SHARED"]);
        using var alphaCraUnassignedSite = factory.CreateSignedClient("CRA", "cra-alpha-nosite", ["STUDY-ALPHA"], []);
        using var alphaCraSite1 = factory.CreateSignedClient("CRA", "cra-alpha-1", ["STUDY-ALPHA"], ["SITE-1"]);

        using var delCrossReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/doa/delegate")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    site_id = "SITE-BETA-DELEG",
                    staff_user_id = "staff-intruder",
                    task_codes = new[] { "CRF_DATA_ENTRY" },
                    start_date = "2026-11-22",
                    reason_for_change = "Cross-study delegation attempt",
                }),
                Encoding.UTF8,
                "application/json"),
        };
        using var signCrossReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/doa/sign-off")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { record_id = "DEL-BETA-SHARED", reason_for_change = "Cross-study sign-off attempt" }),
                Encoding.UTF8,
                "application/json"),
        };
        using var revCrossReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/doa/revoke")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { record_id = "DEL-BETA-ONLY", reason_for_change = "Cross-study revoke attempt" }),
                Encoding.UTF8,
                "application/json"),
        };

        var resDoaLogDelegOnly = await alphaDm.GetAsync("/api/v1/ctms/doa/sites/SITE-BETA-DELEG/log", Ct);
        var resDoaLogShared = await alphaPiShared.GetAsync("/api/v1/ctms/doa/sites/SITE-SHARED/log", Ct);
        var resDoaAuditLeak = await alphaPiSite1.GetAsync("/api/v1/ctms/doa/sites/SITE-1/log", Ct);
        var resDoaPdf = await alphaDm.GetAsync("/api/v1/ctms/doa/sites/SITE-BETA-DELEG/export-pdf", Ct);
        var resDoaDelegate = await alphaDm.SendAsync(delCrossReq, Ct);
        var resDoaSignOff = await alphaPiShared.SendAsync(signCrossReq, Ct);
        var resDoaRevoke = await alphaDm.SendAsync(revCrossReq, Ct);

        var resLetterRender = await alphaCraUnassignedSite.PostAsJsonAsync(
            "/api/v1/ctms/letters/render?templateName=confirmation_letter",
            new { craId = "cra-alpha-nosite", createdAt = "2026-11-22", studyId = "STUDY-ALPHA", siteId = "SITE-ALPHA-UNASSIGNED-99", visitType = "IMV" },
            Ct);
        var resLetterCompile = await alphaCraSite1.PostAsJsonAsync(
            "/api/v1/ctms/letters/compile?templateName=follow_up_letter",
            new { craId = "cra-alpha-1", createdAt = "2026-11-22", studyId = "STUDY-ALPHA", siteId = "SITE-1", visitId = visitBetaId, visitType = "IMV" },
            Ct);
        var resLetterPdf = await alphaCraSite1.GetAsync("/api/v1/ctms/letters/LTR-BETA-NOKEY/pdf", Ct);

        resDoaLogDelegOnly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resDoaPdf.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resDoaDelegate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resDoaSignOff.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resDoaRevoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        resDoaLogShared.StatusCode.Should().Be(HttpStatusCode.OK);
        string sharedBody = await resDoaLogShared.Content.ReadAsStringAsync(Ct);
        sharedBody.Should().Contain("DEL-ALPHA-SHARED")
            .And.NotContain("DEL-BETA-SHARED")
            .And.NotContain("Dr-Beta-PI-Marker");

        resDoaAuditLeak.StatusCode.Should().Be(HttpStatusCode.OK);
        string site1Body = await resDoaAuditLeak.Content.ReadAsStringAsync(Ct);
        site1Body.Should().Contain("staff-alpha-site1-marker")
            .And.NotContain("staff-beta-101-marker");

        resLetterRender.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resLetterCompile.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        resLetterPdf.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MigrationBackfill_PopulatesSingleStudyDelegationsLettersAndAudits_WithoutTouchingAuditDetails()
    {
        await using var throwaway = new ThrowawayPostgresDatabase();
        await throwaway.InitializeAsync();
        throwaway.SkipIfUnavailable();

        string adminConn = throwaway.AdminDatabaseConnectionString!;
        await using (var schemaConn = new NpgsqlConnection(adminConn))
        {
            await schemaConn.OpenAsync(Ct);
            await using var schemaCmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{CtmsDbContextFactory.Schema}\";", schemaConn);
            await schemaCmd.ExecuteNonQueryAsync(Ct);
        }

        string schemaScopedAdminConn = new NpgsqlConnectionStringBuilder(adminConn)
        {
            SearchPath = CtmsDbContextFactory.Schema,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseNpgsql(schemaScopedAdminConn, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CtmsDbContextFactory.Schema))
            .Options;

        await using var db = new CtmsDbContext(options);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261121053553_EnforceTenantRowLevelSecurity", Ct);

        Guid tenantId = Guid.NewGuid();
        string originalAudit1Details = "Delegated tasks [CRF] to staff s1 at site SITE-MIG-1.";
        string originalAudit101Details = "Delegated tasks [CRF] to staff s101 at site SITE-MIG-101.";

        await using (var conn = new NpgsqlConnection(adminConn))
        {
            await conn.OpenAsync(Ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT set_config('cadence.tenant_id', '{tenantId}', false);

                INSERT INTO "ctms"."ctms_site_greenlight_gates"
                    ("Id", "StudyId", "SiteId", "SiteName", "CountryCode", "OverallStatus", "ReadinessPercentage",
                     "FacetRegulatoryIrb", "FacetCtaBudget", "FacetEisfBaseline", "FacetDoaLog", "FacetStaffTraining", "FacetIpRelease",
                     "IsOverridden", "CreatedAt", "CreatedBy", "ReasonForChange", "VersionIndex", "tenant_id")
                VALUES
                    ('G-MIG-1', 'STUDY-MIG-A', 'SITE-MIG-1', 'Site 1', 'US', 'Pending', 0, false, false, false, false, false, false, false, NOW(), 'seed', 'seed', 1, '{tenantId}'),
                    ('G-MIG-101', 'STUDY-MIG-B', 'SITE-MIG-101', 'Site 101', 'US', 'Pending', 0, false, false, false, false, false, false, false, NOW(), 'seed', 'seed', 1, '{tenantId}');

                INSERT INTO "ctms"."ctms_monitoring_visits"
                    ("Id", "StudyId", "SiteId", "CraId", "VisitType", "ScheduledDate", "Status", "CreatedAt", "CreatedBy", "ReasonForChange", "VersionIndex", "tenant_id")
                VALUES
                    ('V-MIG-1', 'STUDY-MIG-A', 'SITE-MIG-1', 'cra-1', 'IMV', NOW(), 'Scheduled', NOW(), 'seed', 'seed', 1, '{tenantId}');

                INSERT INTO "ctms"."ctms_delegations"
                    ("Id", "SiteId", "StaffUserId", "TaskCodes", "StartDate", "IsActive", "SignedOff", "CreatedAt", "CreatedBy", "ReasonForChange", "VersionIndex", "tenant_id")
                VALUES
                    ('D-MIG-1', 'SITE-MIG-1', 's1', '["CRF"]', '2026-11-01', true, false, NOW(), 'seed', 'seed', 1, '{tenantId}');

                INSERT INTO "ctms"."ctms_generated_letters"
                    ("Id", "VisitId", "LetterType", "RenderedContent", "PdfObjectKey", "Status", "IsSigned", "CreatedAt", "CreatedBy", "ReasonForChange", "VersionIndex", "tenant_id")
                VALUES
                    ('L-MIG-VISIT', 'V-MIG-1', 'confirmation_letter', 'body', NULL, 'Draft', false, NOW(), 'seed', 'seed', 1, '{tenantId}'),
                    ('L-MIG-KEY', 'V-UNLINKED', 'follow_up_letter', 'body', 'letters/STUDY-MIG-B/SITE-MIG-101/L-MIG-KEY.pdf', 'Finalized', true, NOW(), 'seed', 'seed', 1, '{tenantId}');

                INSERT INTO "ctms"."ctms_audit_logs"
                    ("Id", "Timestamp", "UserId", "UserRole", "Action", "Details", "CreatedAt", "CreatedBy", "ReasonForChange", "VersionIndex", "tenant_id")
                VALUES
                    ('A-MIG-1', NOW(), 'u1', 'PI', 'DOA_DELEGATE', '{originalAudit1Details}', NOW(), 'u1', 'seed', 1, '{tenantId}'),
                    ('A-MIG-101', NOW(), 'u2', 'PI', 'DOA_DELEGATE', '{originalAudit101Details}', NOW(), 'u2', 'seed', 1, '{tenantId}');
                """;
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(cancellationToken: Ct);

        await using var verifyConn = new NpgsqlConnection(adminConn);
        await verifyConn.OpenAsync(Ct);
        await using var setTenant = new NpgsqlCommand($"SELECT set_config('cadence.tenant_id', '{tenantId}', false);", verifyConn);
        await setTenant.ExecuteNonQueryAsync(Ct);

        await using var delCmd = new NpgsqlCommand("""SELECT "StudyId" FROM "ctms"."ctms_delegations" WHERE "Id" = 'D-MIG-1';""", verifyConn);
        ((string?)await delCmd.ExecuteScalarAsync(Ct)).Should().Be("STUDY-MIG-A");

        await using var letVisitCmd = new NpgsqlCommand("""SELECT "StudyId", "SiteId" FROM "ctms"."ctms_generated_letters" WHERE "Id" = 'L-MIG-VISIT';""", verifyConn);
        await using (var r = await letVisitCmd.ExecuteReaderAsync(Ct))
        {
            (await r.ReadAsync(Ct)).Should().BeTrue();
            r.GetString(0).Should().Be("STUDY-MIG-A");
            r.GetString(1).Should().Be("SITE-MIG-1");
        }

        await using var letKeyCmd = new NpgsqlCommand("""SELECT "StudyId", "SiteId" FROM "ctms"."ctms_generated_letters" WHERE "Id" = 'L-MIG-KEY';""", verifyConn);
        await using (var r = await letKeyCmd.ExecuteReaderAsync(Ct))
        {
            (await r.ReadAsync(Ct)).Should().BeTrue();
            r.GetString(0).Should().Be("STUDY-MIG-B");
            r.GetString(1).Should().Be("SITE-MIG-101");
        }

        await using var aud1Cmd = new NpgsqlCommand("""SELECT "StudyId", "SiteId", "Details" FROM "ctms"."ctms_audit_logs" WHERE "Id" = 'A-MIG-1';""", verifyConn);
        await using (var r = await aud1Cmd.ExecuteReaderAsync(Ct))
        {
            (await r.ReadAsync(Ct)).Should().BeTrue();
            r.GetString(0).Should().Be("STUDY-MIG-A");
            r.GetString(1).Should().Be("SITE-MIG-1");
            r.GetString(2).Should().Be(originalAudit1Details);
        }

        await using var aud101Cmd = new NpgsqlCommand("""SELECT "StudyId", "SiteId", "Details" FROM "ctms"."ctms_audit_logs" WHERE "Id" = 'A-MIG-101';""", verifyConn);
        await using (var r = await aud101Cmd.ExecuteReaderAsync(Ct))
        {
            (await r.ReadAsync(Ct)).Should().BeTrue();
            r.GetString(0).Should().Be("STUDY-MIG-B");
            r.GetString(1).Should().Be("SITE-MIG-101");
            r.GetString(2).Should().Be(originalAudit101Details);
        }

        // Confirm FORCE ROW LEVEL SECURITY remains active after migration
        await using var rlsCmd = new NpgsqlCommand(
            """SELECT relforcerowsecurity FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'ctms' AND c.relname = 'ctms_delegations';""",
            verifyConn);
        ((bool)(await rlsCmd.ExecuteScalarAsync(Ct))!).Should().BeTrue();
    }
}

public sealed class CtmsPostgresApiFactory : WebApplicationFactory<Cadence.Apps.Ctms.Program>, IAsyncLifetime
{
    private readonly ThrowawayPostgresDatabase _database = new();

    public void SkipIfUnavailable() => _database.SkipIfUnavailable();

    public HttpClient CreateSignedClient(
        string role,
        string userId,
        IReadOnlyList<string> studyIds,
        IReadOnlyList<string> siteIds)
    {
        var identity = TestSecurityContext.Identity(userId, role) with
        {
            StudyIds = studyIds,
            SiteIds = siteIds,
        };
        return SignedClientExtensions.CreateSignedClient(this, identity);
    }

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync().ConfigureAwait(false);
        if (_database.ConnectionString is null || _database.AdminDatabaseConnectionString is null)
        {
            return;
        }

        string adminConnString = _database.AdminDatabaseConnectionString;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = adminConnString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        builder.AddCadencePostgresDbContext<CtmsDbContext>(CtmsDbContextFactory.Schema);

        await using (var app = builder.Build())
        {
            var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<CtmsDbContext>>().Single();
            await migrator.StartingAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using var adminConn = new NpgsqlConnection(adminConnString);
        await adminConn.OpenAsync().ConfigureAwait(false);
        await using var grantCmd = adminConn.CreateCommand();
        grantCmd.CommandText = StandaloneTenantIsolationSql.GrantAppRoleSql(CtmsDbContextFactory.Schema, "cadence_app");
        await grantCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret();
        if (_database.AdminDatabaseConnectionString is { } adminConn)
        {
            string appConn = new NpgsqlConnectionStringBuilder(adminConn)
            {
                Username = "cadence_app",
                Password = "cadence_app", // pragma: allowlist secret
            }.ConnectionString;
            builder.UseSetting("ConnectionStrings:cadence", appConn);
            builder.UseSetting("Database:MigrateOnStartup", "false");
        }
        else
        {
            builder.UseTestPostgresConnectionString();
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CurrentTenantAccessor>();
            services.AddSingleton<CurrentTenantAccessor>(sp =>
            {
                var accessor = new CurrentTenantAccessor(
                    sp.GetRequiredService<ITenantCodeResolver>(),
                    sp.GetService<IHttpContextAccessor>());
                accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
                return accessor;
            });
        });

        base.ConfigureWebHost(builder);
    }
}
