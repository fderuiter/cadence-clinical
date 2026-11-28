using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Designer.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Security;

/// <summary>
/// Boots the real <c>safety</c>, <c>ctms</c>, <c>etmf</c>, <c>interop</c>, <c>imaging</c>, <c>knowledge</c>,
/// <c>econsent</c>, and <c>designer</c> hosts against PostgreSQL as <c>cadence_app</c> (<c>NOSUPERUSER NOBYPASSRLS</c>)
/// to verify fail-closed tenant, study, and site scope authorization, country matrix site filtering,
/// eSource provenance study/tenant isolation, eConsent tenant stamping, and Designer batch job isolation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StandaloneServiceScopeAuthorizationTests(StandaloneScopePostgresFixture fixture)
    : IClassFixture<StandaloneScopePostgresFixture>
{
    private static readonly DateTimeOffset FixedTime = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task UnassignedStudyAndMissingTenantCallers_ReceiveForbidden_AcrossAllSevenStandaloneClinicalServices()
    {
        fixture.SkipIfUnavailable();

        var probes = new (
            string Name,
            Func<GatewayIdentity, HttpClient> ClientFactory,
            string Role,
            string Method,
            string Path,
            string? JsonBody,
            bool NeedsConsentSigToken)[]
        {
            (
                "safety",
                fixture.Safety.CreateSignedClient,
                "Sponsor Medical Monitor",
                HttpMethods.Get,
                "/api/v1/safety/dsur?study_id=STUDY-SCOPE-101",
                null,
                false),
            (
                "ctms",
                fixture.Ctms.CreateSignedClient,
                "CRA",
                HttpMethods.Get,
                "/api/v1/ctms/startup/matrix?study_id=STUDY-SCOPE-101&country_code=US",
                null,
                false),
            (
                "etmf",
                fixture.Etmf.CreateSignedClient,
                "CRA",
                HttpMethods.Get,
                "/api/v1/etmf/documents?study_id=STUDY-SCOPE-101&site_id=SITE-01",
                null,
                false),
            (
                "interop",
                fixture.Interop.CreateSignedClient,
                "crc",
                HttpMethods.Get,
                "/api/v1/interop/esource/candidates?study_id=STUDY-SCOPE-101&site_id=SITE-01&skip=0&limit=20",
                null,
                false),
            (
                "imaging",
                fixture.Imaging.CreateSignedClient,
                "crc",
                HttpMethods.Post,
                "/api/v1/imaging/studies?studyId=STUDY-SCOPE-101&siteId=SITE-01&subjectId=SUBJ-1",
                """{"dummy":"payload"}""",
                false),
            (
                "knowledge",
                fixture.Knowledge.CreateSignedClient,
                "crc",
                HttpMethods.Get,
                "/api/v1/training/assignments?study_id=STUDY-SCOPE-101&site_id=SITE-01",
                null,
                false),
            (
                "econsent",
                fixture.Econsent.CreateSignedClient,
                "crc",
                HttpMethods.Post,
                "/api/v1/econsent/templates/tpl-scope-1/capture-consent",
                """{"subject_pseudonym":"SUBJ-1","study_id":"STUDY-SCOPE-101","site_id":"SITE-01","digest_sha256":"a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289","signature_data":"SIG-1","liveness_token":"tok"}""",
                true),
        };

        foreach (var probe in probes)
        {
            // 1. Missing X-Tenant-Id -> 403 Forbidden
            var missingTenant = BuildIdentity($"no-tenant-{probe.Name}", probe.Role, null, ["*"], ["SITE-01"], probe.Path, probe.NeedsConsentSigToken);
            using (var client = probe.ClientFactory(missingTenant))
            using (var req = BuildRequest(probe.Method, probe.Path, probe.JsonBody))
            using (var resp = await client.SendAsync(req, Ct))
            {
                resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{probe.Name} must refuse requests without a signed tenant");
            }

            // 2. Empty study assignment (StudyIds = []) -> 403 Forbidden
            var unassignedStudy = BuildIdentity($"unassigned-study-{probe.Name}", probe.Role, "tenant-alpha", [], ["SITE-01"], probe.Path, probe.NeedsConsentSigToken);
            using (var client = probe.ClientFactory(unassignedStudy))
            using (var req = BuildRequest(probe.Method, probe.Path, probe.JsonBody))
            using (var resp = await client.SendAsync(req, Ct))
            {
                resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{probe.Name} must refuse callers with zero assigned studies");
            }

            // 3. Out-of-scope study assignment (StudyIds = ["STUDY-OTHER"]) -> 403 Forbidden
            var wrongStudy = BuildIdentity($"wrong-study-{probe.Name}", probe.Role, "tenant-alpha", ["STUDY-OTHER"], ["SITE-01"], probe.Path, probe.NeedsConsentSigToken);
            using var wrongStudyClient = probe.ClientFactory(wrongStudy);
            using var wrongStudyReq = BuildRequest(probe.Method, probe.Path, probe.JsonBody);
            using var wrongStudyResp = await wrongStudyClient.SendAsync(wrongStudyReq, Ct);
            wrongStudyResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{probe.Name} must refuse callers assigned only to another study");
        }
    }

    [Theory]
    [InlineData("crc")]
    [InlineData("investigator")]
    [InlineData("principal_investigator")]
    [InlineData("cra")]
    [InlineData("monitor")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task SiteScopedRoles_WithUnassignedOrCrossSiteScope_ReceiveForbidden_AcrossStandaloneServices(string siteScopedRole)
    {
        fixture.SkipIfUnavailable();

        var commonEndpoints = new (string Name, Func<GatewayIdentity, HttpClient> ClientFactory, string Method, string Path, string? Body)[]
        {
            ("ctms", fixture.Ctms.CreateSignedClient, HttpMethods.Get, "/api/v1/ctms/doa/sites/SITE-02/log?study_id=STUDY-101", null),
            ("etmf", fixture.Etmf.CreateSignedClient, HttpMethods.Get, "/api/v1/etmf/documents?study_id=STUDY-101&site_id=SITE-02", null),
            ("interop", fixture.Interop.CreateSignedClient, HttpMethods.Get, "/api/v1/interop/esource/candidates?study_id=STUDY-101&site_id=SITE-02&skip=0&limit=20", null),
            ("imaging", fixture.Imaging.CreateSignedClient, HttpMethods.Get, "/api/v1/imaging/studies/UID-DOES-NOT-EXIST", null),
            ("knowledge", fixture.Knowledge.CreateSignedClient, HttpMethods.Get, "/api/v1/training/assignments?study_id=STUDY-101&site_id=SITE-02", null),
        };

        // 1. Unassigned site scope (SiteIds = []) fails closed with 403 for ALL 5 site-scoped roles.
        foreach (var endpoint in commonEndpoints)
        {
            var noSiteIdentity = new GatewayIdentity(
                UserId: $"nosite-{siteScopedRole}-{endpoint.Name}",
                Roles: [siteScopedRole],
                SiteIds: [],
                TenantId: "tenant-alpha",
                StudyIds: ["STUDY-101"]);

            using var client = endpoint.ClientFactory(noSiteIdentity);
            using var req = BuildRequest(endpoint.Method, endpoint.Path, endpoint.Body);
            using var resp = await client.SendAsync(req, Ct);
            resp.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                $"{endpoint.Name} must refuse site-scoped role '{siteScopedRole}' when SiteIds is empty");
        }

        // 2. Cross-site access (SiteIds = ["SITE-01"] requesting SITE-02) fails closed with 403 across CTMS, eTMF, Interop, Knowledge,
        // and for roles holding upload/capture permissions on Imaging and eConsent.
        foreach (var endpoint in commonEndpoints.Where(e => e.Name != "imaging"))
        {
            var crossSiteIdentity = new GatewayIdentity(
                UserId: $"crosssite-{siteScopedRole}-{endpoint.Name}",
                Roles: [siteScopedRole],
                SiteIds: ["SITE-01"],
                TenantId: "tenant-alpha",
                StudyIds: ["STUDY-101"]);

            using var client = endpoint.ClientFactory(crossSiteIdentity);
            using var req = BuildRequest(endpoint.Method, endpoint.Path, endpoint.Body);
            using var resp = await client.SendAsync(req, Ct);
            resp.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                $"{endpoint.Name} must refuse site-scoped role '{siteScopedRole}' assigned to SITE-01 when requesting SITE-02");
        }

        if (siteScopedRole is "crc" or "investigator" or "principal_investigator")
        {
            var imagingCrossSite = new GatewayIdentity(
                UserId: $"img-cross-{siteScopedRole}",
                Roles: [siteScopedRole],
                SiteIds: ["SITE-01"],
                TenantId: "tenant-alpha",
                StudyIds: ["STUDY-101"]);
            using (var imgClient = fixture.Imaging.CreateSignedClient(imagingCrossSite))
            using (var imgReq = BuildRequest(
                HttpMethods.Post,
                "/api/v1/imaging/studies?studyId=STUDY-101&siteId=SITE-02&subjectId=SUBJ-2",
                """{"dummy":"payload"}"""))
            using (var imgResp = await imgClient.SendAsync(imgReq, Ct))
            {
                imgResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }

            string consentPath = "/api/v1/econsent/templates/tpl-cross-site/capture-consent";
            string userId = $"ec-cross-{siteScopedRole}";
            var econsentCrossSite = new GatewayIdentity(
                UserId: userId,
                Roles: [siteScopedRole],
                SiteIds: ["SITE-01"],
                TenantId: "tenant-alpha",
                StudyIds: ["STUDY-101"],
                SigToken: TestSecurityContext.CreateSigToken(userId, consentPath, "econsent.consent.capture", [siteScopedRole]));
            using var ecClient = fixture.Econsent.CreateSignedClient(econsentCrossSite);
            using var ecReq = BuildRequest(
                HttpMethods.Post,
                consentPath,
                """{"subject_pseudonym":"SUBJ-2","study_id":"STUDY-101","site_id":"SITE-02","digest_sha256":"a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289","signature_data":"SIG-2","liveness_token":"tok"}""");
            using var ecResp = await ecClient.SendAsync(ecReq, Ct);
            ecResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task GetCountryMatrixAsync_ReturnsOnlyAuthorizedSites_AndHidesUnauthorizedAndCrossTenantGreenlightTokens()
    {
        fixture.SkipIfUnavailable();

        const string studyId = "STUDY-GREENLIGHT-MATRIX";
        Guid tenantAlpha = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha");
        Guid tenantBeta = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-beta");

        await SeedCtmsGateAsync(tenantAlpha, "gate-a-s1", studyId, "SITE-01", "US", "GL-SECRET-ALPHA-SITE-01");
        await SeedCtmsGateAsync(tenantAlpha, "gate-a-s2", studyId, "SITE-02", "US", "GL-SECRET-ALPHA-SITE-02");
        await SeedCtmsGateAsync(tenantBeta, "gate-b-s1", studyId, "SITE-01", "US", "GL-SECRET-BETA-SITE-01");

        foreach (string role in new[] { "crc", "investigator", "principal_investigator", "cra", "monitor" })
        {
            using var site01Client = fixture.Ctms.CreateSignedClient(new GatewayIdentity(
                UserId: $"matrix-{role}-s1",
                Roles: [role],
                SiteIds: ["SITE-01"],
                TenantId: "tenant-alpha",
                StudyIds: [studyId]));

            using var resp = await site01Client.GetAsync($"/api/v1/ctms/startup/matrix?study_id={studyId}&country_code=US", Ct);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            string body = await resp.Content.ReadAsStringAsync(Ct);

            body.Should().Contain("SITE-01").And.Contain("GL-SECRET-ALPHA-SITE-01");
            body.Should().NotContain("SITE-02", $"role '{role}' assigned only to SITE-01 must not see SITE-02");
            body.Should().NotContain("GL-SECRET-ALPHA-SITE-02", $"role '{role}' assigned only to SITE-01 must never receive SITE-02's GreenlightToken");
            body.Should().NotContain("GL-SECRET-BETA-SITE-01", "PostgreSQL RLS must isolate tenant-beta's GreenlightToken");
        }

        // Wildcard Sponsor Data Manager in tenant-alpha sees both tenant-alpha sites and zero tenant-beta tokens.
        using var sponsorClient = fixture.Ctms.CreateSignedClient(new GatewayIdentity(
            UserId: "matrix-dm-alpha",
            Roles: ["Sponsor Data Manager"],
            SiteIds: [],
            TenantId: "tenant-alpha",
            StudyIds: ["*"]));
        using var sponsorResp = await sponsorClient.GetAsync($"/api/v1/ctms/startup/matrix?study_id={studyId}&country_code=US", Ct);
        sponsorResp.StatusCode.Should().Be(HttpStatusCode.OK);
        string sponsorBody = await sponsorResp.Content.ReadAsStringAsync(Ct);
        sponsorBody.Should().Contain("GL-SECRET-ALPHA-SITE-01").And.Contain("GL-SECRET-ALPHA-SITE-02");
        sponsorBody.Should().NotContain("GL-SECRET-BETA-SITE-01");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task GetProvenanceAuditAsync_ReturnsOnlyTargetCandidateAuditLogs_WithoutCrossStudyPrefixOrCrossTenantLeakage()
    {
        fixture.SkipIfUnavailable();

        Guid tenantAlpha = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha");
        Guid tenantBeta = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-beta");

        await SeedInteropCandidateAndAuditAsync(
            tenantAlpha,
            candidateId: "cand-prov-1",
            studyId: "STUDY-PROV-A",
            siteId: "SITE-01",
            auditDetails: "Accepted candidate cand-prov-1 (SYSBP) into eCRF [ALPHA-STUDY-A-MARKER]");

        // Same tenant, different study, same TargetVariable ("SYSBP"): must NOT appear!
        await SeedInteropCandidateAndAuditAsync(
            tenantAlpha,
            candidateId: "cand-prov-2",
            studyId: "STUDY-PROV-B",
            siteId: "SITE-01",
            auditDetails: "Accepted candidate cand-prov-2 (SYSBP) into eCRF [ALPHA-STUDY-B-MARKER]");

        // Prefix collision ("cand-prov-10" starts with "cand-prov-1") in another study: must NOT appear!
        await SeedInteropCandidateAndAuditAsync(
            tenantAlpha,
            candidateId: "cand-prov-10",
            studyId: "STUDY-PROV-B",
            siteId: "SITE-01",
            auditDetails: "Accepted candidate cand-prov-10 (SYSBP) into eCRF [PREFIX-COLLISION-MARKER]");

        // Different tenant, even mentioning "candidate cand-prov-1 " verbatim: blocked by PostgreSQL RLS!
        await SeedInteropCandidateAndAuditAsync(
            tenantBeta,
            candidateId: "cand-prov-beta",
            studyId: "STUDY-PROV-A",
            siteId: "SITE-01",
            auditDetails: "Accepted candidate cand-prov-1 (SYSBP) into eCRF [BETA-TENANT-MARKER]");

        using var authorizedCrc = fixture.Interop.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-prov-a",
            Roles: ["crc"],
            SiteIds: ["SITE-01"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-PROV-A"]));

        using var okResp = await authorizedCrc.GetAsync("/api/v1/interop/esource/provenance/cand-prov-1", Ct);
        okResp.StatusCode.Should().Be(HttpStatusCode.OK);
        string provenanceJson = await okResp.Content.ReadAsStringAsync(Ct);
        provenanceJson.Should().Contain("ALPHA-STUDY-A-MARKER");
        provenanceJson.Should().NotContain("ALPHA-STUDY-B-MARKER");
        provenanceJson.Should().NotContain("PREFIX-COLLISION-MARKER");
        provenanceJson.Should().NotContain("BETA-TENANT-MARKER");

        // Cross-site CRC in same study receives 404 NotFound (no existence or provenance leak).
        using var wrongSiteCrc = fixture.Interop.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-prov-s2",
            Roles: ["crc"],
            SiteIds: ["SITE-02"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-PROV-A"]));
        using var wrongSiteResp = await wrongSiteCrc.GetAsync("/api/v1/interop/esource/provenance/cand-prov-1", Ct);
        wrongSiteResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task IngestFhirBundleAsync_BlocksCrossSiteSubjectProbeAndConflictingSite_WhilePreservingSameSiteDeduplication()
    {
        fixture.SkipIfUnavailable();

        Guid tenantAlpha = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha");
        await using (var scope = fixture.Interop.Services.CreateAsyncScope())
        {
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            using var _ = tenantScope.Bind(tenantAlpha);
            var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();

            db.EhrReconciliationCandidateRecords.Add(new EhrReconciliationCandidateRecord
            {
                Id = "cand-site02-subj02-bp",
                StudyId = "STUDY-01",
                SiteId = "SITE-02",
                SubjectId = "SUBJ-02",
                SubjectPseudonym = "PSEUDO-SITE02-CANARY",
                EhrSystemId = "SITE02-HOSPITAL-EHR-CANARY",
                FhirResourceType = "Observation",
                FhirResourceId = "obs-bp-1",
                SourceTimestamp = FixedTime,
                TargetDomain = "VS",
                TargetVariable = "SYSBP",
                CdashTestcd = "SYSBP",
                CdashTest = "Systolic Blood Pressure",
                CandidateValueJson = "142",
                CandidateUnit = "mmHg",
                ElectronicSignatureJson = """{"signed_by":"pi-site-02","site":"SITE-02-SIGNATURE-CANARY"}""",
                CreatedAt = FixedTime,
            });

            await db.SaveChangesAsync(Ct);
        }

        const string site1ProbeBundle = """
            {
              "resourceType": "Bundle",
              "type": "collection",
              "entry": [
                {
                  "resource": {
                    "resourceType": "Observation",
                    "id": "obs-bp-1",
                    "status": "final",
                    "code": { "coding": [ { "system": "http://loinc.org", "code": "8480-6", "display": "Systolic blood pressure" } ] },
                    "effectiveDateTime": "2026-10-15T10:00:00Z",
                    "valueQuantity": { "value": 120, "unit": "mmHg" }
                  }
                }
              ]
            }
            """;

        using var crcSite1 = fixture.Interop.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-ingest-s1",
            Roles: ["crc"],
            SiteIds: ["SITE-01"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-01"]));

        using var probeResp = await crcSite1.PostAsJsonAsync(
            "/api/v1/interop/esource/ingest",
            new
            {
                studyId = "STUDY-01",
                siteId = "SITE-01",
                subjectId = "SUBJ-02",
                subjectPseudonym = "PROBE",
                ehrSystemId = "EPIC-METRO",
                rawFhirBundleJson = site1ProbeBundle,
            },
            Ct);

        probeResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        string probeBody = await probeResp.Content.ReadAsStringAsync(Ct);
        probeBody.Should().NotContain("PSEUDO-SITE02-CANARY")
            .And.NotContain("SITE02-HOSPITAL-EHR-CANARY")
            .And.NotContain("SITE-02-SIGNATURE-CANARY")
            .And.NotContain("SITE-02");

        // Multi-site caller authorized for both SITE-01 and SITE-02 sending conflicting SiteId receives 409 without candidate details.
        using var crcMultiSite = fixture.Interop.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-ingest-multi",
            Roles: ["crc"],
            SiteIds: ["SITE-01", "SITE-02"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-01"]));

        using var conflictResp = await crcMultiSite.PostAsJsonAsync(
            "/api/v1/interop/esource/ingest",
            new
            {
                studyId = "STUDY-01",
                siteId = "SITE-01",
                subjectId = "SUBJ-02",
                subjectPseudonym = "PROBE",
                ehrSystemId = "EPIC-METRO",
                rawFhirBundleJson = site1ProbeBundle,
            },
            Ct);

        conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        string conflictBody = await conflictResp.Content.ReadAsStringAsync(Ct);
        conflictBody.Should().NotContain("PSEUDO-SITE02-CANARY")
            .And.NotContain("SITE02-HOSPITAL-EHR-CANARY")
            .And.NotContain("SITE-02-SIGNATURE-CANARY")
            .And.NotContain("SITE-02");

        // Distinct subject SUBJ-01 at SITE-01 sharing local resource ID obs-bp-1 ingests independently.
        using var site1OwnSubjectResp = await crcSite1.PostAsJsonAsync(
            "/api/v1/interop/esource/ingest",
            new
            {
                studyId = "STUDY-01",
                siteId = "SITE-01",
                subjectId = "SUBJ-01",
                subjectPseudonym = "PSEUDO-SITE01-SUBJ01",
                ehrSystemId = "EPIC-METRO",
                rawFhirBundleJson = site1ProbeBundle,
            },
            Ct);
        site1OwnSubjectResp.StatusCode.Should().Be(HttpStatusCode.OK, await site1OwnSubjectResp.Content.ReadAsStringAsync(Ct));

        // Same-site retry at SITE-02 continues to return 200 OK with the existing SITE-02 candidate.
        using var crcSite2 = fixture.Interop.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-ingest-s2",
            Roles: ["crc"],
            SiteIds: ["SITE-02"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-01"]));

        using var sameSiteRetryResp = await crcSite2.PostAsJsonAsync(
            "/api/v1/interop/esource/ingest",
            new
            {
                studyId = "  STUDY-01  ",
                siteId = "  SITE-02  ",
                subjectId = "  SUBJ-02  ",
                subjectPseudonym = "PSEUDO-SITE02-CANARY",
                ehrSystemId = "SITE02-HOSPITAL-EHR-CANARY",
                rawFhirBundleJson = site1ProbeBundle,
            },
            Ct);
        sameSiteRetryResp.StatusCode.Should().Be(HttpStatusCode.OK, await sameSiteRetryResp.Content.ReadAsStringAsync(Ct));
        string sameSiteBody = await sameSiteRetryResp.Content.ReadAsStringAsync(Ct);
        sameSiteBody.Should().Contain("cand-site02-subj02-bp");
    }

    [Fact]
    [GxPRequirement("PRD-CRF-013")]
    public async Task EconsentCaptureAndDesignerBatchValidate_StampVerifiedTenant_WithZeroDefaultOrRandomTenants()
    {
        fixture.SkipIfUnavailable();

        // 1. eConsent capture stamps verified tenant code on ConsentAuditLog and resolved UUID on PostgreSQL tenant_id.
        string actionPath = "/api/v1/econsent/templates/tpl-rls-1/capture-consent";
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-EC-1", 0.99m, true, secret: TestSecurityContext.BiometricLivenessKeyBytes, now: SystemClock.Instance.UtcNow);
        var crcAlpha = new GatewayIdentity(
            UserId: "crc-ec-alpha",
            Roles: ["crc"],
            SiteIds: ["SITE-01"],
            TenantId: "tenant-alpha",
            StudyIds: ["STUDY-EC-1"],
            SigToken: TestSecurityContext.CreateSigToken("crc-ec-alpha", actionPath, "econsent.consent.capture", ["crc"]));

        using (var ecAlphaClient = fixture.Econsent.CreateSignedClient(crcAlpha))
        {
            var captureResp = await ecAlphaClient.PostAsJsonAsync(
                actionPath,
                new
                {
                    subject_pseudonym = "SUBJ-EC-1",
                    study_id = "STUDY-EC-1",
                    site_id = "SITE-01",
                    digest_sha256 = "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
                    signature_data = "SIG-PART11-ALPHA",
                    liveness_token = livenessToken,
                },
                Ct);

            captureResp.StatusCode.Should().Be(HttpStatusCode.OK, await captureResp.Content.ReadAsStringAsync(Ct));
        }

        Guid expectedAlphaUuid = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-alpha");
        await using (var adminConn = new NpgsqlConnection(fixture.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = """
                SELECT "tenant_id"
                FROM "econsent"."consent_audit_logs"
                WHERE "ActorId" = 'crc-ec-alpha'
                """;
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).Should().BeTrue();
            reader.GetGuid(0).Should().Be(expectedAlphaUuid).And.NotBe(Guid.Empty);
        }

        // 2. Designer batch-validate stamps deterministic verified TenantId and isolates jobs across tenants.
        string studyRouteId = Guid.NewGuid().ToString("D");
        using var designerAlpha = fixture.Designer.CreateSignedClient(new GatewayIdentity(
            UserId: "designer-alpha",
            Roles: ["Study Designer"],
            SiteIds: [],
            TenantId: "tenant-alpha",
            StudyIds: [studyRouteId]));

        using var designerBeta = fixture.Designer.CreateSignedClient(new GatewayIdentity(
            UserId: "designer-beta",
            Roles: ["Study Designer"],
            SiteIds: [],
            TenantId: "tenant-beta",
            StudyIds: [studyRouteId]));

        var missingPackResp = await designerAlpha.PostAsJsonAsync(
            $"/api/v1/studies/{studyRouteId}/rules/batch-validate",
            new BatchValidateApiRequest(IsDryRun: true),
            Ct);
        missingPackResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var explicitPack = new RulePack(
            RuleAuthority.Study,
            "Study Isolation Pack",
            [
                new Rule(
                    "R_ISO_001",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Protocol Edit Check Spec", "4.1", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(
                        ComparisonOperator.Greater,
                        new VariableExpression("SYSBP"),
                        new LiteralExpression(RuleValue.FromNumber(140m))),
                    "High systolic blood pressure.",
                    null,
                    EffectivePeriod.Always),
            ]);

        var batchPostResp = await designerAlpha.PostAsJsonAsync(
            $"/api/v1/studies/{studyRouteId}/rules/batch-validate",
            new BatchValidateApiRequest(RulePacks: [explicitPack], IsDryRun: true),
            Ct);
        batchPostResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await batchPostResp.Content.ReadFromJsonAsync<BatchValidationReport>(cancellationToken: Ct);
        report.Should().NotBeNull();
        report!.JobId.Should().NotBeNullOrWhiteSpace();

        using var alphaJobResp = await designerAlpha.GetAsync(
            $"/api/v1/studies/{studyRouteId}/rules/batch-validate/{report.JobId}",
            Ct);
        alphaJobResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var betaJobResp = await designerBeta.GetAsync(
            $"/api/v1/studies/{studyRouteId}/rules/batch-validate/{report.JobId}",
            Ct);
        betaJobResp.StatusCode.Should().Be(HttpStatusCode.NotFound, "batch validation jobs must be isolated to the submitting tenant");
    }

    private async Task SeedCtmsGateAsync(
        Guid tenantUuid,
        string gateId,
        string studyId,
        string siteId,
        string countryCode,
        string greenlightToken)
    {
        await using var scope = fixture.Ctms.Services.CreateAsyncScope();
        using var actorBinding = scope.ServiceProvider.GetRequiredService<Cadence.Database.Audit.IAuditActorScope>().Begin("svc:test-seeder", "Test fixture seeding");
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        using var _ = tenantScope.Bind(tenantUuid);
        var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();

        var gate = new CtmsSiteGreenlightGate(gateId)
        {
            StudyId = studyId,
            SiteId = siteId,
            SiteName = $"Clinic {siteId}",
            CountryCode = countryCode,
            ReadinessPercentage = 100,
            FacetRegulatoryIrb = true,
            FacetCtaBudget = true,
            FacetEisfBaseline = true,
            FacetDoaLog = true,
            FacetStaffTraining = true,
            FacetIpRelease = true,
            CreatedAt = FixedTime.UtcDateTime,
            CreatedBy = "seed",
            ReasonForChange = "Initial greenlight seed",
        };
        gate.Approve("sponsor-certifier", FixedTime, greenlightToken);
        db.CtmsSiteGreenlightGates.Add(gate);
        await db.SaveChangesAsync(Ct).ConfigureAwait(false);
    }

    private async Task SeedInteropCandidateAndAuditAsync(
        Guid tenantUuid,
        string candidateId,
        string studyId,
        string siteId,
        string auditDetails)
    {
        await using var scope = fixture.Interop.Services.CreateAsyncScope();
        using var actorBinding = scope.ServiceProvider.GetRequiredService<Cadence.Database.Audit.IAuditActorScope>().Begin("svc:test-seeder", "Test fixture seeding");
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        using var _ = tenantScope.Bind(tenantUuid);
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();

        db.EhrReconciliationCandidateRecords.Add(new EhrReconciliationCandidateRecord
        {
            Id = candidateId,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = "SUBJ-PROV-1",
            SubjectPseudonym = "SUBJ-PROV-1",
            EhrSystemId = "EPIC",
            FhirResourceType = "Observation",
            FhirResourceId = $"obs-{candidateId}",
            SourceTimestamp = FixedTime,
            TargetDomain = "VS",
            TargetVariable = "SYSBP",
            CandidateValueJson = "120",
            CreatedAt = FixedTime,
        });

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = FixedTime,
            UserId = "seed-crc",
            UserRole = "crc",
            Action = "ESOURCE_CANDIDATE_ACCEPTED",
            Details = auditDetails,
            ChangeReason = "Seed audit trail",
        });

        await db.SaveChangesAsync(Ct).ConfigureAwait(false);
    }

    private static GatewayIdentity BuildIdentity(
        string userId,
        string role,
        string? tenantId,
        IReadOnlyList<string> studyIds,
        IReadOnlyList<string> siteIds,
        string path,
        bool needsConsentSigToken) =>
        new(
            UserId: userId,
            Roles: [role],
            SiteIds: siteIds,
            TenantId: tenantId,
            StudyIds: studyIds,
            SigToken: needsConsentSigToken
                ? TestSecurityContext.CreateSigToken(userId, path, "econsent.consent.capture", [role])
                : null);

    private static HttpRequestMessage BuildRequest(string method, string path, string? jsonBody)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (jsonBody is not null)
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return req;
    }
}

/// <summary>
/// Shared PostgreSQL fixture that migrates the standalone schemas onto one <see cref="ThrowawayPostgresDatabase"/>
/// and exposes <see cref="WebApplicationFactory{TEntryPoint}"/> hosts connected strictly as <c>cadence_app</c>.
/// </summary>
public sealed class StandaloneScopePostgresFixture : IAsyncLifetime
{
    private readonly ThrowawayPostgresDatabase _database = new();

    public WebApplicationFactory<Cadence.Apps.Safety.Program> Safety { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Ctms.Program> Ctms { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Etmf.Program> Etmf { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Interop.Program> Interop { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Imaging.Program> Imaging { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Knowledge.Program> Knowledge { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Econsent.Program> Econsent { get; private set; } = null!;
    public WebApplicationFactory<Cadence.Apps.Designer.Program> Designer { get; private set; } = null!;

    public string AdminDatabaseConnectionString =>
        _database.AdminDatabaseConnectionString ?? throw new InvalidOperationException("PostgreSQL is not initialized.");

    public void SkipIfUnavailable() => _database.SkipIfUnavailable();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync().ConfigureAwait(false);
        if (_database.ConnectionString is null || _database.AdminDatabaseConnectionString is null)
        {
            return;
        }

        string adminConn = _database.AdminDatabaseConnectionString;
        string appConn = new NpgsqlConnectionStringBuilder(adminConn)
        {
            Username = "cadence_app",
            Password = "cadence_app", // pragma: allowlist secret
        }.ConnectionString;

        await MigrateSchemaAsync<SafetyDbContext>(adminConn, SafetyDbContextFactory.Schema).ConfigureAwait(false);
        await MigrateSchemaAsync<CtmsDbContext>(adminConn, CtmsDbContextFactory.Schema).ConfigureAwait(false);
        await MigrateSchemaAsync<EtmfDbContext>(adminConn, EtmfDbContextFactory.Schema).ConfigureAwait(false);
        await MigrateSchemaAsync<InteropDbContext>(adminConn, InteropDbContextFactory.Schema).ConfigureAwait(false);
        await MigrateSchemaAsync<ImagingDbContext>(adminConn, ImagingDbContextFactory.Schema).ConfigureAwait(false);
        await MigrateSchemaAsync<KnowledgeDbContext>(adminConn, KnowledgeDbContextFactory.Schema).ConfigureAwait(false);
        await MigrateSchemaAsync<EconsentDbContext>(adminConn, EconsentDbContextFactory.Schema).ConfigureAwait(false);

        Safety = CreateAppFactory<Cadence.Apps.Safety.Program>(appConn);
        Ctms = CreateAppFactory<Cadence.Apps.Ctms.Program>(appConn);
        Etmf = CreateAppFactory<Cadence.Apps.Etmf.Program>(appConn);
        Interop = CreateAppFactory<Cadence.Apps.Interop.Program>(appConn);
        Imaging = CreateAppFactory<Cadence.Apps.Imaging.Program>(appConn);
        Knowledge = CreateAppFactory<Cadence.Apps.Knowledge.Program>(appConn);
        Econsent = CreateAppFactory<Cadence.Apps.Econsent.Program>(appConn);
        Designer = CreateAppFactory<Cadence.Apps.Designer.Program>(appConn);
    }

    public async ValueTask DisposeAsync()
    {
        if (Designer is not null) await Designer.DisposeAsync().ConfigureAwait(false);
        if (Econsent is not null) await Econsent.DisposeAsync().ConfigureAwait(false);
        if (Knowledge is not null) await Knowledge.DisposeAsync().ConfigureAwait(false);
        if (Imaging is not null) await Imaging.DisposeAsync().ConfigureAwait(false);
        if (Interop is not null) await Interop.DisposeAsync().ConfigureAwait(false);
        if (Etmf is not null) await Etmf.DisposeAsync().ConfigureAwait(false);
        if (Ctms is not null) await Ctms.DisposeAsync().ConfigureAwait(false);
        if (Safety is not null) await Safety.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task MigrateSchemaAsync<TContext>(string adminConnString, string schema)
        where TContext : DbContext
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(
            new Microsoft.AspNetCore.Builder.WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = adminConnString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);

        await using var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(CancellationToken.None).ConfigureAwait(false);

        await using var adminConn = new NpgsqlConnection(adminConnString);
        await adminConn.OpenAsync().ConfigureAwait(false);
        await using var grantCmd = adminConn.CreateCommand();
        grantCmd.CommandText = StandaloneTenantIsolationSql.GrantAppRoleSql(schema, "cadence_app");
        await grantCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static WebApplicationFactory<TProgram> CreateAppFactory<TProgram>(string cadenceAppConnectionString)
        where TProgram : class =>
        new WebApplicationFactory<TProgram>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestStorage()
                .UseTestDemographicsKey()
                .UseTestRandomizationKey()
                .UseTestCgtBarcodeKey()
                .UseTestFieldProtectionKeys()
                .UseTestSceSigningKey()
                .UseTestImagingDeidentificationKeys()
                .UseTestBiometricLivenessKey()
                .UseSetting("ConnectionStrings:cadence", cadenceAppConnectionString)
                .UseSetting("Database:MigrateOnStartup", "false")
                .ConfigureServices(services =>
                {
                    services.PostConfigure<ProblemDetailsOptions>(options =>
                        options.CustomizeProblemDetails = ctx =>
                            ctx.ProblemDetails.Detail ??= ctx.Exception?.ToString());
                });
        });
}
