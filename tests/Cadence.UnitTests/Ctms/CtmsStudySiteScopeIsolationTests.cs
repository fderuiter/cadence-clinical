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
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class CtmsStudySiteScopeIsolationTests : IAsyncLifetime
{
    private const string VisitBetaId = "VISIT-BETA-001";
    private const string VisitAlphaId = "VISIT-ALPHA-001";

    private readonly CtmsInMemoryWebFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await SeedIsolationFixtureAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task AllTenCrossStudyAndCrossSiteBypasses_AreBlockedAcrossEightCtmsRoutes()
    {
        var factory = _factory;
        string visitBetaId = VisitBetaId;

        using var alphaDm = factory.CreateSignedClient("Data Manager", "dm-alpha-1", ["STUDY-ALPHA"], []);
        using var alphaPiSite1 = factory.CreateSignedClient("Principal Investigator", "pi-alpha-site1", ["STUDY-ALPHA"], ["SITE-1"]);
        using var alphaPiShared = factory.CreateSignedClient("Principal Investigator", "pi-alpha-shared", ["STUDY-ALPHA"], ["SITE-SHARED"]);
        using var alphaCraUnassignedSite = factory.CreateSignedClient("CRA", "cra-alpha-nosite", ["STUDY-ALPHA"], []);
        using var alphaCraSite1 = factory.CreateSignedClient("CRA", "cra-alpha-1", ["STUDY-ALPHA"], ["SITE-1"]);

        using var delCrossReq = JsonPost(
            "/api/v1/ctms/doa/delegate",
            new
            {
                site_id = "SITE-BETA-DELEG",
                staff_user_id = "staff-intruder",
                task_codes = new[] { "CRF_DATA_ENTRY" },
                start_date = "2026-11-22",
                reason_for_change = "Cross-study delegation attempt",
            });

        using var signCrossReq = JsonPost(
            "/api/v1/ctms/doa/sign-off",
            new
            {
                record_id = "DEL-BETA-SHARED",
                reason_for_change = "Cross-study sign-off attempt",
            });
        signCrossReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                "pi-alpha-shared",
                DoaEndpoints.SignOffDelegationAction,
                DoaEndpoints.SignOffDelegationSemanticAction,
                [Cadence.Security.Rbac.RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR]));

        using var revCrossReq = JsonPost(
            "/api/v1/ctms/doa/revoke",
            new
            {
                record_id = "DEL-BETA-ONLY",
                reason_for_change = "Cross-study revoke attempt",
            });

        // DOA cross-study delegation-only site, shared-site isolation, and SITE-1 vs SITE-101 audit isolation
        var resDoaLogDelegOnly = await alphaDm.GetAsync("/api/v1/ctms/doa/sites/SITE-BETA-DELEG/log", Ct);
        var resDoaLogShared = await alphaPiShared.GetAsync("/api/v1/ctms/doa/sites/SITE-SHARED/log", Ct);
        var resDoaAuditLeak = await alphaPiSite1.GetAsync("/api/v1/ctms/doa/sites/SITE-1/log", Ct);
        var resDoaPdf = await alphaDm.GetAsync("/api/v1/ctms/doa/sites/SITE-BETA-DELEG/export-pdf", Ct);
        var resDoaDelegate = await alphaDm.SendAsync(delCrossReq, Ct);
        var resDoaSignOff = await alphaPiShared.SendAsync(signCrossReq, Ct);
        var resDoaRevoke = await alphaDm.SendAsync(revCrossReq, Ct);

        // Letter unassigned-site render, foreign-visit compile, and null-PdfObjectKey PDF read
        var resLetterRender = await alphaCraUnassignedSite.PostAsJsonAsync(
            "/api/v1/ctms/letters/render?templateName=confirmation_letter",
            new { craId = "cra-alpha-nosite", createdAt = "2026-11-22", studyId = "STUDY-ALPHA", siteId = "SITE-ALPHA-UNASSIGNED-99", visitType = "IMV" },
            Ct);
        var resLetterCompile = await alphaCraSite1.PostAsJsonAsync(
            "/api/v1/ctms/letters/compile?templateName=follow_up_letter",
            new { craId = "cra-alpha-1", createdAt = "2026-11-22", studyId = "STUDY-ALPHA", siteId = "SITE-1", visitId = visitBetaId, visitType = "IMV" },
            Ct);
        var resLetterPdf = await alphaCraSite1.GetAsync("/api/v1/ctms/letters/LTR-BETA-NOKEY/pdf", Ct);

        // Assert DOA cross-study delegation-only site blocked fail-closed with 403 FORBIDDEN_SCOPE
        await AssertForbiddenScopeAsync(resDoaLogDelegOnly);
        await AssertForbiddenScopeAsync(resDoaPdf);
        await AssertForbiddenScopeAsync(resDoaDelegate);
        await AssertForbiddenScopeAsync(resDoaSignOff);
        await AssertForbiddenScopeAsync(resDoaRevoke);

        // Assert shared-site log returns STUDY-ALPHA records only and excludes STUDY-BETA records & PI marker
        resDoaLogShared.StatusCode.Should().Be(HttpStatusCode.OK);
        string sharedBody = await resDoaLogShared.Content.ReadAsStringAsync(Ct);
        sharedBody.Should().Contain("DEL-ALPHA-SHARED")
            .And.Contain("Dr-Alpha-PI-Marker")
            .And.NotContain("DEL-BETA-SHARED")
            .And.NotContain("Dr-Beta-PI-Marker")
            .And.NotContain("staff-beta-shared-marker");

        // Assert SITE-1 log excludes SITE-101 substring audit entries
        resDoaAuditLeak.StatusCode.Should().Be(HttpStatusCode.OK);
        string site1Body = await resDoaAuditLeak.Content.ReadAsStringAsync(Ct);
        site1Body.Should().Contain("staff-alpha-site1-marker")
            .And.NotContain("staff-beta-101-marker")
            .And.NotContain("SITE-101");

        // Assert letter routes block unassigned-site render, foreign-visit compile, and null-PdfObjectKey PDF read
        resLetterRender.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resLetterCompile.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        await AssertForbiddenScopeAsync(resLetterPdf);

        // Also verify handler-level ClinicalScopeGuard.RequireStudyAndOptionalSite on /letters/render for cross-site CRA (403 FORBIDDEN_SCOPE)
        var resLetterRenderCrossSite = await alphaCraSite1.PostAsJsonAsync(
            "/api/v1/ctms/letters/render?templateName=confirmation_letter",
            new { craId = "cra-alpha-1", createdAt = "2026-11-22", studyId = "STUDY-ALPHA", siteId = "SITE-ALPHA-UNASSIGNED-99", visitType = "IMV" },
            Ct);
        await AssertForbiddenScopeAsync(resLetterRenderCrossSite);

        // And verify direct RenderLetterAsync handler call rejects site-scoped role with empty AssignedSites (403 FORBIDDEN_SCOPE)
        var unassignedCraHttp = new DefaultHttpContext
        {
            User = TestSecurityContext.CreateScopedPrincipal("cra-alpha-nosite", ["STUDY-ALPHA"], [], "CRA"),
        };
        var directRenderRes = await LetterTemplateEndpoints.RenderLetterAsync(
            "confirmation_letter",
            new SiteVisitLetterModel("cra-alpha-nosite", "2026-11-22", "STUDY-ALPHA", "SITE-ALPHA-UNASSIGNED-99", "IMV"),
            Cadence.Templating.FluidTemplateRenderer.Instance,
            unassignedCraHttp);
        var directProblem = directRenderRes.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        directProblem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        directProblem.ProblemDetails.Extensions["code"].Should().Be(CtmsScopeGuard.ForbiddenCode);

        // Verify foreign visit never received an attached letter and beta delegations remained untouched
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
        var betaLetters = await db.CtmsGeneratedLetters.AsNoTracking()
            .Where(l => l.VisitId == visitBetaId && l.Id != "LTR-BETA-NOKEY")
            .ToListAsync(Ct);
        betaLetters.Should().BeEmpty();

        var betaShared = await db.CtmsDelegations.AsNoTracking().SingleAsync(d => d.Id == "DEL-BETA-SHARED", Ct);
        betaShared.SignedOff.Should().BeFalse();

        var betaOnly = await db.CtmsDelegations.AsNoTracking().SingleAsync(d => d.Id == "DEL-BETA-ONLY", Ct);
        betaOnly.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SharedSiteInvestigator_CanDelegateSignOffAndExportPdf_IsolatedToAssignedStudy()
    {
        using var alphaPiShared = _factory.CreateSignedClient("Principal Investigator", "pi-alpha-shared", ["STUDY-ALPHA"], ["SITE-SHARED"]);

        var delegateRes = await alphaPiShared.PostAsJsonAsync(
            "/api/v1/ctms/doa/delegate",
            new
            {
                site_id = "SITE-SHARED",
                staff_user_id = "staff-alpha-new",
                task_codes = new[] { "INFORMED_CONSENT" },
                start_date = "2026-11-22",
                reason_for_change = "Delegate consent duty in STUDY-ALPHA",
            },
            Ct);
        delegateRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await delegateRes.Content.ReadFromJsonAsync<DelegationResponseDto>(Ct);
        created.Should().NotBeNull();

        using var signReq = JsonPost(
            "/api/v1/ctms/doa/sign-off",
            new
            {
                record_id = created!.RecordId,
                reason_for_change = "PI sign-off for STUDY-ALPHA delegation",
            });
        signReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                "pi-alpha-shared",
                DoaEndpoints.SignOffDelegationAction,
                DoaEndpoints.SignOffDelegationSemanticAction,
                [Cadence.Security.Rbac.RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR]));
        var signRes = await alphaPiShared.SendAsync(signReq, Ct);
        signRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var pdfRes = await alphaPiShared.GetAsync("/api/v1/ctms/doa/sites/SITE-SHARED/export-pdf", Ct);
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        string pdfText = Encoding.UTF8.GetString(await pdfRes.Content.ReadAsByteArrayAsync(Ct));
        pdfText.Should().Contain("DEL-ALPHA-SHARED")
            .And.Contain(created.RecordId)
            .And.Contain("Dr-Alpha-PI-Marker")
            .And.NotContain("DEL-BETA-SHARED")
            .And.NotContain("Dr-Beta-PI-Marker")
            .And.NotContain("staff-beta-shared-marker");

        using var signedPdfReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/doa/sites/SITE-SHARED/export-signed-pdf");
        signedPdfReq.Headers.Add("X-Change-Reason", "Official signed DoA export for STUDY-ALPHA");
        signedPdfReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                "pi-alpha-shared",
                DoaEndpoints.ExportSignedPdfAction("SITE-SHARED"),
                DoaEndpoints.ExportSignedPdfSemanticAction,
                [Cadence.Security.Rbac.RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR]));
        var signedPdfRes = await alphaPiShared.SendAsync(signedPdfReq, Ct);
        signedPdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        string signedPdfText = Encoding.UTF8.GetString(await signedPdfRes.Content.ReadAsByteArrayAsync(Ct));
        signedPdfText.Should().Contain("DEL-ALPHA-SHARED")
            .And.NotContain("DEL-BETA-SHARED");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
        var saved = await db.CtmsDelegations.AsNoTracking().SingleAsync(d => d.Id == created.RecordId, Ct);
        saved.StudyId.Should().Be("STUDY-ALPHA");
        saved.SiteId.Should().Be("SITE-SHARED");
        saved.SignedOff.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizedCallerOnNonexistentSite_Gets404_WhileUnauthorizedProbe_Gets403()
    {
        using var alphaDm = _factory.CreateSignedClient("Data Manager", "dm-alpha-1", ["STUDY-ALPHA"], []);
        using var alphaPiSite1 = _factory.CreateSignedClient("Principal Investigator", "pi-alpha-site1", ["STUDY-ALPHA"], ["SITE-1"]);

        // Authorized study-wide caller targeting nonexistent site gets 404
        var dmMissingLog = await alphaDm.GetAsync("/api/v1/ctms/doa/sites/SITE-DOES-NOT-EXIST/log", Ct);
        dmMissingLog.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Unauthorized site probe (PI assigned only to SITE-1) gets 403 before SiteExistsAsync
        var piUnassignedProbe = await alphaPiSite1.GetAsync("/api/v1/ctms/doa/sites/SITE-DOES-NOT-EXIST/log", Ct);
        piUnassignedProbe.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Unauthorized explicit study_id probe gets 403 before SiteExistsAsync
        var dmUnauthorizedStudyProbe = await alphaDm.GetAsync("/api/v1/ctms/doa/sites/SITE-DOES-NOT-EXIST/log?study_id=STUDY-BETA", Ct);
        await AssertForbiddenScopeAsync(dmUnauthorizedStudyProbe);
    }

    [Fact]
    public async Task ValidVisitCompileAndPdfDownload_SucceedsForAuthorizedCra_AndPreservesMonitoringVisitLetterEndpoints()
    {
        using var alphaCraSite1 = _factory.CreateSignedClient("CRA", "cra-alpha-1", ["STUDY-ALPHA"], ["SITE-1"]);

        var compileRes = await alphaCraSite1.PostAsJsonAsync(
            "/api/v1/ctms/letters/compile?templateName=follow_up_letter",
            new
            {
                craId = "cra-alpha-1",
                createdAt = "2026-11-22",
                studyId = "STUDY-ALPHA",
                siteId = "SITE-1",
                visitId = VisitAlphaId,
                visitType = "IMV",
            },
            Ct);
        compileRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var compiled = await compileRes.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string letterId = compiled.GetProperty("letterId").GetString()!;

        var pdfRes = await alphaCraSite1.GetAsync($"/api/v1/ctms/letters/{letterId}/pdf", Ct);
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfRes.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var visitLettersRes = await alphaCraSite1.GetAsync($"/api/v1/ctms/monitoring-visits/{VisitAlphaId}/letters", Ct);
        visitLettersRes.StatusCode.Should().Be(HttpStatusCode.OK);
        string visitLettersJson = await visitLettersRes.Content.ReadAsStringAsync(Ct);
        visitLettersJson.Should().Contain(letterId);
    }

    private static HttpRequestMessage JsonPost(string path, object payload) =>
        new(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    private static async Task AssertForbiddenScopeAsync(HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, raw);
        var json = JsonSerializer.Deserialize<JsonElement>(raw);
        json.GetProperty("code").GetString().Should().Be(CtmsScopeGuard.ForbiddenCode);
    }

    private async Task SeedIsolationFixtureAsync()
    {
        var now = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();

        // 1. Delegation-only site in STUDY-BETA (no greenlight gate, milestone, or monitoring visit)
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

        // 2. Shared clinical site (SITE-SHARED) participating in both STUDY-ALPHA and STUDY-BETA
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
            new CtmsAuditLog("AUD-ALPHA-SHARED")
            {
                Timestamp = now,
                UserId = "pi-alpha-shared",
                UserRole = "investigator",
                Action = "DOA_DELEGATE",
                Details = "Delegated tasks [CRF_DATA_ENTRY] to staff staff-alpha-shared at site SITE-SHARED in study STUDY-ALPHA.",
                StudyId = "STUDY-ALPHA",
                SiteId = "SITE-SHARED",
                CreatedAt = now.UtcDateTime,
                CreatedBy = "pi-alpha-shared",
                ReasonForChange = "Seed alpha audit",
                VersionIndex = 1,
            },
            new CtmsAuditLog("AUD-BETA-SHARED")
            {
                Timestamp = now,
                UserId = "pi-beta-shared",
                UserRole = "investigator",
                Action = "DOA_DELEGATE",
                Details = "Delegated tasks [IP_ACCOUNTABILITY] to staff staff-beta-shared-marker at site SITE-SHARED in study STUDY-BETA.",
                StudyId = "STUDY-BETA",
                SiteId = "SITE-SHARED",
                CreatedAt = now.UtcDateTime,
                CreatedBy = "pi-beta-shared",
                ReasonForChange = "Seed beta audit",
                VersionIndex = 1,
            });

        // 3. SITE-1 (STUDY-ALPHA) vs SITE-101 (STUDY-BETA) to verify exact SiteId audit filtering without substring leaks
        db.CtmsSiteGreenlightGates.AddRange(
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

        // 4. Monitoring visits and null-PdfObjectKey letter in STUDY-BETA
        db.CtmsMonitoringVisits.AddRange(
            new CtmsMonitoringVisit(VisitAlphaId)
            {
                StudyId = "STUDY-ALPHA",
                SiteId = "SITE-1",
                CraId = "cra-alpha-1",
                VisitType = "IMV",
                ScheduledDate = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "cra-alpha-1",
                ReasonForChange = "Seed alpha visit",
                VersionIndex = 1,
            },
            new CtmsMonitoringVisit(VisitBetaId)
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
            StudyId = "STUDY-BETA",
            SiteId = "SITE-101",
            VisitId = VisitBetaId,
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

    public sealed class CtmsInMemoryWebFactory : WebApplicationFactory<Cadence.Apps.Ctms.Program>
    {
        private readonly string _databaseName = $"ctms-scope-{Guid.NewGuid():N}";

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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();

            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IHostedService))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(sp => new ReplayCache(sp.GetRequiredService<IClock>()));

                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<DbContextOptions<CtmsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CtmsDbContext>>();
                services.RemoveAll<CurrentTenantAccessor>();
                services.AddSingleton<CurrentTenantAccessor>(sp =>
                {
                    var accessor = new CurrentTenantAccessor(
                        sp.GetRequiredService<ITenantCodeResolver>(),
                        sp.GetService<IHttpContextAccessor>());
                    accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
                    return accessor;
                });

                services.AddDbContext<CtmsDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(_databaseName)
                        .UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                        .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance);
                    options.UseInternalServiceProvider(inMemoryServices);
                });
            });
        }
    }
}
