using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Multi-tenant PostgreSQL verification for Execution participant, consent, and site-compliance isolation,
/// demo-tenant seeder scoping (<c>cadence-local</c>), coordinator vs. <c>sponsor_dm</c> write authority, and
/// fail-closed rejection when tenant context is missing or unverified.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionTenantIsolationPostgresTests(ExecutionPostgresApiFactory factory)
    : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Multi-tenant execution isolation verification with explicit clinical reason.";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact(DisplayName = "FreshTrialTenant_SeesZeroDemoCadence101Subjects_EnforcesCoordinatorAndSponsorDmAuthority_AndIsolatesSubjectsConsentsAndCompliance")]
    [GxPRequirement("PRD-SUB-001")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task FreshTrialTenantSeesZeroDemoCadence101SubjectsEnforcesCoordinatorAndSponsorDmAuthorityAndIsolatesSubjectsConsentsAndCompliance()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // Warm host and run ExecutionDbSeeder strictly against the development demo tenant (cadence-local).
        using (var warm = factory.CreateSignedClient())
        {
            _ = await warm.GetAsync(new Uri("/healthz", UriKind.Relative), ct);
        }

        using (var seedScope = factory.Services.CreateSeedingScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var tenantScope = seedScope.ServiceProvider.GetRequiredService<ITenantScope>();
            await ExecutionDbSeeder.SeedAsync(db, tenantScope, ct);
        }

        // 1. Demo tenant (cadence-local) sees seeded CADENCE-101 subjects (SUBJ-101-001 & SUBJ-101-002 at SITE-101,
        //    plus SUBJ-102-001 at SITE-102 for study-wide readers) with deterministic enrollment_ref NodeRefs matching demo-seed.json.
        using (var demoClient = factory.CreateSignedClient(new GatewayIdentity(
            UserId: "demo-crc",
            Roles: ["Site Investigator", "Sponsor Admin"],
            SiteIds: ["SITE-101"],
            TenantId: ExecutionDbSeeder.DemoTenantCode,
            StudyIds: ["CADENCE-101"])))
        {
            var demoList = await demoClient.GetFromJsonAsync<SubjectListResponse>(
                "/api/v1/execution/subjects?study_id=CADENCE-101",
                JsonOptions,
                ct);
            demoList.Should().NotBeNull();
            demoList!.Total.Should().Be(2);
            demoList.Items.Select(i => i.SubjectId_.Value).Should().Equal("SUBJ-101-001", "SUBJ-101-002");
            demoList.Items.Should().OnlyContain(i => i.EnrollmentRef != null && i.EnrollmentRef.StartsWith("enrollment:", StringComparison.Ordinal));

            var subj101Detail = await demoClient.GetFromJsonAsync<SubjectDetailResponse>(
                $"/api/v1/execution/subjects/{demoList.Items[0].EnrollmentRef}",
                JsonOptions,
                ct);
            subj101Detail.Should().NotBeNull();
            subj101Detail!.SubjectId.Value.Should().Be("SUBJ-101-001");
            subj101Detail.EnrollmentRef.Should().Be(demoList.Items[0].EnrollmentRef);

            using var demoCompliance = await demoClient.GetAsync(
                new Uri("/api/v1/execution/sites/SITE-101/compliance-status?study_id=CADENCE-101&milestone=SITE_ACTIVATION", UriKind.Relative),
                ct);
            demoCompliance.StatusCode.Should().Be(HttpStatusCode.OK);
            var demoCompJson = JsonDocument.Parse(await demoCompliance.Content.ReadAsStringAsync(ct)).RootElement;
            demoCompJson.GetProperty("is_complete").GetBoolean().Should().BeTrue();
        }

        using (var demoSponsorClient = factory.CreateSignedClient(new GatewayIdentity(
            UserId: "demo-sponsor-admin",
            Roles: ["Sponsor Admin"],
            SiteIds: [],
            TenantId: ExecutionDbSeeder.DemoTenantCode,
            StudyIds: ["CADENCE-101"])))
        {
            var allDemoSubjects = await demoSponsorClient.GetFromJsonAsync<SubjectListResponse>(
                "/api/v1/execution/subjects?study_id=CADENCE-101",
                JsonOptions,
                ct);
            allDemoSubjects.Should().NotBeNull();
            allDemoSubjects!.Total.Should().Be(3);
            allDemoSubjects.Items.Select(i => i.SubjectId_.Value)
                .Should().Equal("SUBJ-101-001", "SUBJ-101-002", "SUBJ-102-001");
            allDemoSubjects.Items.Select(i => i.EnrollmentRef).Distinct().Should().HaveCount(3);
        }

        // 2. Requests without a verified tenant (null or unknown tenant) are rejected with HTTP 403.
        foreach (string? invalidTenant in new string?[] { null, "tenant-does-not-exist" })
        {
            using var untrustedClient = factory.CreateSignedClient(new GatewayIdentity(
                UserId: "unverified-caller",
                Roles: ["Site Investigator"],
                SiteIds: ["SITE-ALPHA-01"],
                TenantId: invalidTenant,
                StudyIds: ["CADENCE-101"],
                ChangeReason: Reason));

            using var listDenied = await untrustedClient.GetAsync(
                new Uri("/api/v1/execution/subjects?study_id=CADENCE-101", UriKind.Relative),
                ct);
            listDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var createDenied = await untrustedClient.PostAsJsonAsync(
                "/api/v1/execution/subjects",
                new SubjectCreateRequest("SUBJ-101-001", "CADENCE-101", "SITE-ALPHA-01", null),
                JsonOptions,
                ct);
            createDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 3. Fresh trial tenant (tenant-alpha) creating study CADENCE-101 observes ZERO demo subjects and unseeded compliance.
        var alphaFounder = new GatewayIdentity(
            UserId: "founder-alpha",
            Roles: ["Sponsor Admin", "Sponsor Designer", "Site Investigator"],
            SiteIds: ["SITE-ALPHA-01"],
            TenantId: "tenant-alpha",
            StudyIds: ["CADENCE-101"],
            ChangeReason: Reason);
        using var alphaClient = factory.CreateSignedClient(alphaFounder);

        var initialAlphaSubjects = await alphaClient.GetFromJsonAsync<SubjectListResponse>(
            "/api/v1/execution/subjects?study_id=CADENCE-101",
            JsonOptions,
            ct);
        initialAlphaSubjects.Should().NotBeNull();
        initialAlphaSubjects!.Total.Should().Be(0, "demo tenant CADENCE-101 rows must never leak into trial workspaces");
        initialAlphaSubjects.Items.Should().BeEmpty();

        using (var alphaInitialCompliance = await alphaClient.GetAsync(
            new Uri("/api/v1/execution/sites/SITE-101/compliance-status?study_id=CADENCE-101&milestone=SITE_ACTIVATION", UriKind.Relative),
            ct))
        {
            // Founder is site-scoped to SITE-ALPHA-01, so querying SITE-101 is forbidden with 403.
            alphaInitialCompliance.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var alphaOwnSiteCompliance = await alphaClient.GetAsync(
            new Uri("/api/v1/execution/sites/SITE-ALPHA-01/compliance-status?study_id=CADENCE-101&milestone=SITE_ACTIVATION", UriKind.Relative),
            ct))
        {
            alphaOwnSiteCompliance.StatusCode.Should().Be(HttpStatusCode.OK);
            var ownCompJson = JsonDocument.Parse(await alphaOwnSiteCompliance.Content.ReadAsStringAsync(ct)).RootElement;
            ownCompJson.GetProperty("is_complete").GetBoolean().Should().BeFalse();
        }

        // 4. Attempting participant creation as sponsor_dm without a coordinator role returns HTTP 403.
        using (var sponsorDmClient = factory.CreateSignedClient(new GatewayIdentity(
            UserId: "dm-alpha",
            Roles: ["sponsor_dm"],
            SiteIds: [],
            TenantId: "tenant-alpha",
            StudyIds: ["CADENCE-101"],
            ChangeReason: Reason)))
        {
            using var dmCreateResp = await sponsorDmClient.PostAsJsonAsync(
                "/api/v1/execution/subjects",
                new SubjectCreateRequest("SUBJ-101-001", "CADENCE-101", "SITE-ALPHA-01", null),
                JsonOptions,
                ct);
            dmCreateResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 5. Founder (Site Investigator scoped to SITE-ALPHA-01) is blocked at unassigned sites (403) and succeeds at SITE-ALPHA-01 (201)
        //    with SUBJ-101-001 and enrollment_index = 0, independent of cadence-local!
        using (var wrongSiteResp = await alphaClient.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-101-001", "CADENCE-101", "SITE-UNASSIGNED", null),
            JsonOptions,
            ct))
        {
            wrongSiteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var alphaCreateResp = await alphaClient.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-101-001", "CADENCE-101", "SITE-ALPHA-01", null),
            JsonOptions,
            ct);
        string alphaCreateBody = await alphaCreateResp.Content.ReadAsStringAsync(ct);
        alphaCreateResp.StatusCode.Should().Be(HttpStatusCode.Created, alphaCreateBody);
        var createdAlphaSubject = JsonSerializer.Deserialize<SubjectResponse>(alphaCreateBody, JsonOptions)!;
        createdAlphaSubject.SubjectId_.Value.Should().Be("SUBJ-101-001");
        createdAlphaSubject.StudyId.Value.Should().Be("CADENCE-101");
        createdAlphaSubject.SiteId!.Value.Value.Should().Be("SITE-ALPHA-01");
        createdAlphaSubject.Status.Should().Be("SCREENING");

        var alphaListAfter = await alphaClient.GetFromJsonAsync<SubjectListResponse>(
            "/api/v1/execution/subjects?study_id=CADENCE-101",
            JsonOptions,
            ct);
        alphaListAfter!.Items.Single().EnrollmentIndex.Should().Be(0);

        // Record consent and site compliance in tenant-alpha.
        using (var consentResp = await alphaClient.PostAsJsonAsync(
            "/api/v1/execution/subjects/SUBJ-101-001/consent",
            new SubjectConsentRequest(
                new ProtocolVersionPayload("CADENCE-101", "1.0", 1),
                IcfSigned: true,
                IcfSignedDate: new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
                RequiresReconsent: false,
                IsPaperOverride: false),
            JsonOptions,
            ct))
        {
            consentResp.StatusCode.Should().Be(HttpStatusCode.OK, await consentResp.Content.ReadAsStringAsync(ct));
        }

        using (var etmfServiceClient = factory.CreateSignedClient(new GatewayIdentity(
            UserId: "etmf-service-alpha",
            Roles: ["etmf_service"],
            SiteIds: [],
            TenantId: "tenant-alpha",
            StudyIds: ["CADENCE-101"],
            ChangeReason: Reason)))
        {
            using var webhookResp = await etmfServiceClient.PostAsJsonAsync(
                "/api/v1/execution/webhooks/etmf",
                new EtmfCompletenessWebhookPayload("CADENCE-101", "SITE-ALPHA-01", "SITE_ACTIVATION", IsComplete: true, MissingArtifacts: []),
                JsonOptions,
                ct);
            webhookResp.StatusCode.Should().Be(HttpStatusCode.OK, await webhookResp.Content.ReadAsStringAsync(ct));
        }

        // 6. A second trial tenant (tenant-beta) sees zero subjects, zero compliance from tenant-alpha, and can independently
        //    register SUBJ-101-001 in CADENCE-101 at enrollment_index = 0.
        using var betaClient = factory.CreateSignedClient(new GatewayIdentity(
            UserId: "founder-beta",
            Roles: ["Sponsor Admin", "Sponsor Designer", "Site Investigator"],
            SiteIds: ["SITE-ALPHA-01"],
            TenantId: "tenant-beta",
            StudyIds: ["CADENCE-101"],
            ChangeReason: Reason));

        var betaBefore = await betaClient.GetFromJsonAsync<SubjectListResponse>(
            "/api/v1/execution/subjects?study_id=CADENCE-101",
            JsonOptions,
            ct);
        betaBefore!.Total.Should().Be(0);

        using (var betaDetailBefore = await betaClient.GetAsync(
            new Uri("/api/v1/execution/subjects/SUBJ-101-001", UriKind.Relative),
            ct))
        {
            betaDetailBefore.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var betaCompliance = await betaClient.GetAsync(
            new Uri("/api/v1/execution/sites/SITE-ALPHA-01/compliance-status?study_id=CADENCE-101&milestone=SITE_ACTIVATION", UriKind.Relative),
            ct))
        {
            betaCompliance.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonDocument.Parse(await betaCompliance.Content.ReadAsStringAsync(ct))
                .RootElement.GetProperty("is_complete").GetBoolean()
                .Should().BeFalse("site compliance rows in tenant-alpha must never leak to tenant-beta");
        }

        using var betaCreateResp = await betaClient.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-101-001", "CADENCE-101", "SITE-ALPHA-01", null),
            JsonOptions,
            ct);
        string betaCreateBody = await betaCreateResp.Content.ReadAsStringAsync(ct);
        betaCreateResp.StatusCode.Should().Be(HttpStatusCode.Created, betaCreateBody);
        var createdBetaSubject = JsonSerializer.Deserialize<SubjectResponse>(betaCreateBody, JsonOptions)!;
        createdBetaSubject.Id.Should().NotBe(createdAlphaSubject.Id);

        var betaListAfter = await betaClient.GetFromJsonAsync<SubjectListResponse>(
            "/api/v1/execution/subjects?study_id=CADENCE-101",
            JsonOptions,
            ct);
        betaListAfter!.Items.Single().EnrollmentIndex.Should().Be(0);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }
}
