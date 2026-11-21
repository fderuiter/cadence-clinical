using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// End-to-end PostgreSQL verification of self-service trial workspace bootstrap, idempotency per OIDC subject,
/// Keycloak claim writeback + refreshed gateway identity resolution under Row-Level Security, commercial vs.
/// clinical Grant isolation, and expired-trial read/export preservation (<c>403 K.Subscription.Expired</c>).
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelTrialOnboardingPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    [Fact(DisplayName = "SelfRegisteredCallerWithoutTenant_BootstrapsTrialIdempotently_SyncsKeycloak_AndIsolatesUnderPostgresRls")]
    public async Task SelfRegisteredCallerWithoutTenantBootstrapsTrialIdempotentlySyncsKeycloakAndIsolatesUnderPostgresRls()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        var keycloak = new RecordingKeycloakOnboardingClient();

        using var host = CreateHost(clock, keycloak, environment: "Production");

        string subjectOne = Guid.CreateVersion7(clock.UtcNow).ToString();
        string subjectTwo = Guid.CreateVersion7(clock.UtcNow.AddSeconds(1)).ToString();

        // 1. Before onboarding, a newly self-registered OIDC user has no tenant_id claim:
        //    tenant-scoped kernel routes refuse with 403, while POST /api/v1/kernel/onboarding/trial is mapped outside KernelTenantFilter.
        using var unprovisionedClient = host.CreateSignedClient(new GatewayIdentity(subjectOne, [], [], TenantId: null));
        using var preCheck = await unprovisionedClient.GetAsync(new Uri("/api/v1/kernel/entitlements", UriKind.Relative), ct);
        preCheck.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var onboardResponse = await unprovisionedClient.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Meridian Vascular Research", residency = "EU", tier = "Individual" },
            ct);
        string onboardBody = await onboardResponse.Content.ReadAsStringAsync(ct);
        onboardResponse.StatusCode.Should().Be(HttpStatusCode.OK, onboardBody);

        var firstPayload = JsonDocument.Parse(onboardBody).RootElement;
        firstPayload.GetProperty("newly_provisioned").GetBoolean().Should().BeTrue();
        string tenantCode = firstPayload.GetProperty("tenant_code").GetString()!;
        string tenantRef = firstPayload.GetProperty("tenant_ref").GetString()!;
        string personRef = firstPayload.GetProperty("person_ref").GetString()!;
        tenantCode.Should().StartWith("trial-");
        firstPayload.GetProperty("assigned_roles").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Sponsor Admin", "Sponsor Designer", "Site Investigator");
        string sandboxSiteCode = firstPayload.GetProperty("sandbox_site_code").GetString()!;
        sandboxSiteCode.Should().StartWith("SITE-");
        firstPayload.GetProperty("sandbox_site_country").GetString().Should().Be("NL");
        firstPayload.GetProperty("assigned_site_ids").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(sandboxSiteCode);
        firstPayload.GetProperty("entitlements").GetProperty("tier").GetString().Should().Be("Individual");
        firstPayload.GetProperty("entitlements").GetProperty("status").GetString().Should().Be("Trialing");
        firstPayload.GetProperty("entitlements").GetProperty("product_families").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Blueprint", "Capture");

        // 2. Repeated POST /api/v1/kernel/onboarding/trial for the same OIDC sub is strictly idempotent.
        using var replayResponse = await unprovisionedClient.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Meridian Vascular Research", residency = "EU", tier = "Individual" },
            ct);
        string replayBody = await replayResponse.Content.ReadAsStringAsync(ct);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK, replayBody);
        var replayPayload = JsonDocument.Parse(replayBody).RootElement;
        replayPayload.GetProperty("newly_provisioned").GetBoolean().Should().BeFalse();
        replayPayload.GetProperty("tenant_code").GetString().Should().Be(tenantCode);
        replayPayload.GetProperty("tenant_ref").GetString().Should().Be(tenantRef);
        replayPayload.GetProperty("person_ref").GetString().Should().Be(personRef);

        keycloak.Calls.Should().Contain(c =>
            c.SubjectId == subjectOne
            && c.TenantCode == tenantCode
            && c.Roles.SequenceEqual(new[] { "Sponsor Admin", "Sponsor Designer", "Site Investigator" })
            && c.SiteCodes.SequenceEqual(new[] { sandboxSiteCode })
            && c.StudyCodes.Length == 0);

        // Verify PostgreSQL state: 1 tenant, 2 orgs (Sponsor + SiteInstitution), 1 sandbox site, 1 pseudonymous person (zero name/email columns), 1 membership, 1 subscription.
        var tenantId = NodeRef.Parse(tenantRef).Id;
        var personId = NodeRef.Parse(personRef).Id;
        await using (var conn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await conn.OpenAsync(ct);
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.tenants WHERE code = @code", ("code", tenantCode))).Should().Be(1L);
            (await ScalarStringAsync(conn, "SELECT created_by FROM kernel.tenants WHERE id = @id", ("id", tenantId)))
                .Should().Be("svc:kernel-trial-onboarding");
            (await ScalarStringAsync(conn, "SELECT residency FROM kernel.tenants WHERE id = @id", ("id", tenantId)))
                .Should().Be("EU");
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.organizations WHERE tenant_id = @tid", ("tid", tenantId))).Should().Be(2L);
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.sites WHERE tenant_id = @tid", ("tid", tenantId))).Should().Be(1L);
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.people WHERE id = @pid", ("pid", personId))).Should().Be(1L);
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.tenant_memberships WHERE tenant_id = @tid AND person_id = @pid", ("tid", tenantId), ("pid", personId))).Should().Be(1L);
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.subscriptions WHERE tenant_id = @tid", ("tid", tenantId))).Should().Be(1L);
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.product_entitlements WHERE tenant_id = @tid", ("tid", tenantId))).Should().Be(2L);

            // Pseudonymous identity check (K-ID-4): kernel.people has no name or email columns.
            await using var colCmd = new NpgsqlCommand(
                "SELECT column_name FROM information_schema.columns WHERE table_schema = 'kernel' AND table_name = 'people'",
                conn);
            var columns = new List<string>();
            await using (var reader = await colCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    columns.Add(reader.GetString(0));
                }
            }

            columns.Should().NotContain(c => c.Contains("name", StringComparison.OrdinalIgnoreCase) || c.Contains("email", StringComparison.OrdinalIgnoreCase));
        }

        // 3. After token refresh, GatewayIdentity carries TenantId = tenantCode and assigned roles -> DirectoryTenantResolver + RLS admit requests!
        using var refreshedOne = host.CreateSignedClient(
            new GatewayIdentity(subjectOne, ["Sponsor Admin", "Sponsor Designer"], ["*"], TenantId: tenantCode));

        using var entResp = await refreshedOne.GetAsync(new Uri("/api/v1/kernel/entitlements", UriKind.Relative), ct);
        entResp.StatusCode.Should().Be(HttpStatusCode.OK, await entResp.Content.ReadAsStringAsync(ct));

        // Register a study inside Tenant 1 as the newly onboarded Sponsor Admin
        var sponsorId = NodeRef.Parse(firstPayload.GetProperty("organization_ref").GetString()!).Id;
        var studyOneId = StudyId.New(clock.UtcNow);
        await SendAsPersonAsync(
            host.Services,
            subjectOne,
            new RegisterStudy(
                TenantId.FromGuid(tenantId),
                studyOneId,
                OrganizationId.FromGuid(sponsorId),
                "MERIDIAN-101",
                "Meridian Vascular Feasibility Study",
                "Phase II",
                "idem-meridian-study-101",
                "Create first trial study"),
            ct);

        var searchOne = await GetJsonAsync(refreshedOne, "/api/v1/kernel/search?q=MERIDIAN-101", ct);
        searchOne.EnumerateArray().Should().ContainSingle(hit => hit.GetProperty("code").GetString() == "MERIDIAN-101");

        // 4. Onboard a second independent OIDC user -> receives separate tenant, isolated by PostgreSQL RLS.
        using var unprovisionedTwo = host.CreateSignedClient(new GatewayIdentity(subjectTwo, [], [], TenantId: null));
        using var onboardTwoResp = await unprovisionedTwo.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Boreal Oncology Group", residency = "US", tier = "Select" },
            ct);
        onboardTwoResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPayload = JsonDocument.Parse(await onboardTwoResp.Content.ReadAsStringAsync(ct)).RootElement;
        string tenantCodeTwo = secondPayload.GetProperty("tenant_code").GetString()!;
        tenantCodeTwo.Should().NotBe(tenantCode);

        using var refreshedTwo = host.CreateSignedClient(
            new GatewayIdentity(subjectTwo, ["Sponsor Admin", "Sponsor Designer"], ["*"], TenantId: tenantCodeTwo));
        var searchTwo = await GetJsonAsync(refreshedTwo, "/api/v1/kernel/search?q=MERIDIAN-101", ct);
        searchTwo.GetArrayLength().Should().Be(0, "PostgreSQL RLS isolates self-registered trial tenants");
    }

    [Fact(DisplayName = "CompleteTierWithoutGrant_MasksStudy_AndExpiredTrialBlocksMutationsWith403WhilePreservingReadsAndBaselineSignatures")]
    public async Task CompleteTierWithoutGrantMasksStudyAndExpiredTrialBlocksMutationsWith403WhilePreservingReadsAndBaselineSignatures()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        var keycloak = new RecordingKeycloakOnboardingClient();

        using var host = CreateHost(clock, keycloak, environment: "Production");

        string founderSub = Guid.CreateVersion7(clock.UtcNow).ToString();
        string ungrantedColleagueSub = Guid.CreateVersion7(clock.UtcNow.AddSeconds(2)).ToString();

        using var unprovisioned = host.CreateSignedClient(new GatewayIdentity(founderSub, [], [], TenantId: null));
        using var onboardResp = await unprovisioned.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Helios Cardiology Partners", tier = "Individual" },
            ct);
        onboardResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var onboardJson = JsonDocument.Parse(await onboardResp.Content.ReadAsStringAsync(ct)).RootElement;

        string tenantCode = onboardJson.GetProperty("tenant_code").GetString()!;
        var tenantId = TenantId.FromGuid(NodeRef.Parse(onboardJson.GetProperty("tenant_ref").GetString()!).Id);
        var sponsorId = OrganizationId.FromGuid(NodeRef.Parse(onboardJson.GetProperty("organization_ref").GetString()!).Id);

        using var founderClient = host.CreateSignedClient(
            new GatewayIdentity(founderSub, ["Sponsor Admin", "Sponsor Designer"], ["*"], TenantId: tenantCode));

        // Register a study while trial is active
        var studyId = StudyId.New(clock.UtcNow);
        var studyRef = NodeRef.For(studyId).ToString();
        var registered = await SendAsPersonAsync(
            host.Services,
            founderSub,
            new RegisterStudy(
                tenantId,
                studyId,
                sponsorId,
                "HELIOS-201",
                "Helios Heart Rhythm Study",
                "Phase II",
                "idem-helios-201",
                "Initial trial study registration"),
            ct);
        registered.IsSuccess.Should().BeTrue();

        // Multi-step commercial tier transitions in place: Individual -> Select -> Complete -> Select -> Complete
        foreach (var (stepKey, targetTier, expectedFamilyCount) in new (string Key, string Tier, int Count)[]
        {
            ("idem-upgrade-select-1", "Select", 5),
            ("idem-upgrade-complete-1", "Complete", 8),
            ("idem-narrow-select-2", "Select", 5),
            ("idem-upgrade-complete-2", "Complete", 8),
        })
        {
            using var upgradeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/entitlements/tier")
            {
                Content = JsonContent.Create(new { tier = targetTier }),
            };
            upgradeReq.Headers.Add("Idempotency-Key", stepKey);
            upgradeReq.Headers.Add("X-Change-Reason", $"Switch commercial tier to {targetTier}");
            using var upgradeResp = await founderClient.SendAsync(upgradeReq, ct);
            string upgradeBody = await upgradeResp.Content.ReadAsStringAsync(ct);
            upgradeResp.StatusCode.Should().Be(HttpStatusCode.OK, upgradeBody);
            var upgraded = JsonDocument.Parse(upgradeBody).RootElement;
            upgraded.GetProperty("tier").GetString().Should().Be(targetTier);
            upgraded.GetProperty("status").GetString().Should().Be("Active");
            upgraded.GetProperty("product_families").GetArrayLength().Should().Be(expectedFamilyCount);
        }

        // 1. Even in a Complete-tier tenant, a signed-in caller WITHOUT a clinical study Grant cannot see or mutate the study!
        using (var ungrantedClient = host.CreateSignedClient(
            new GatewayIdentity(ungrantedColleagueSub, ["Auditor"], ["*"], TenantId: tenantCode)))
        {
            var colleagueEntitlements = await GetJsonAsync(ungrantedClient, "/api/v1/kernel/entitlements", ct);
            colleagueEntitlements.GetProperty("tier").GetString().Should().Be("Complete");

            var nodeJson = await GetJsonAsync(ungrantedClient, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(studyRef)}", ct);
            nodeJson.GetProperty("mask").GetString().Should().Be(
                "NotPermitted",
                "Complete commercial entitlement never confers clinical study visibility without a kernel Grant");

            var deniedMutation = await SendAsPersonAsync(
                host.Services,
                ungrantedColleagueSub,
                new RegisterStudy(
                    tenantId,
                    StudyId.New(clock.UtcNow),
                    sponsorId,
                    "HELIOS-UNAUTH",
                    "Unauthorized Study",
                    "Phase I",
                    "idem-unauth-study",
                    "Attempt without grant"),
                ct);
            deniedMutation.IsSuccess.Should().BeFalse();
            deniedMutation.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        // 2. Now expire the tenant's subscription and verify:
        //    - New study mutations fail with 403 K.Subscription.Expired
        //    - Regulated reads/exports (nodes, activity/audit feed, search, entitlements) succeed
        //    - Baseline regulated capabilities (electronic signature Attest) remain active and never gated!
        var subscriptionId = SubscriptionId.FromGuid(
            NodeRef.Parse(onboardJson.GetProperty("entitlements").GetProperty("subscription_ref").GetString()!).Id);
        var expired = await SendAsPersonAsync(
            host.Services,
            founderSub,
            new ExpireSubscription(tenantId, subscriptionId, "idem-expire-helios", "Simulate trial window expiration"),
            ct);
        expired.IsSuccess.Should().BeTrue();

        var expiredEntitlements = await GetJsonAsync(founderClient, "/api/v1/kernel/entitlements", ct);
        expiredEntitlements.GetProperty("status").GetString().Should().Be("Expired");
        expiredEntitlements.GetProperty("is_read_only").GetBoolean().Should().BeTrue();

        // Regulated study record read + audit activity stream remain accessible (read & export preserved)
        var visibleStudyNode = await GetJsonAsync(founderClient, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(studyRef)}", ct);
        visibleStudyNode.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);
        visibleStudyNode.GetProperty("code").GetString().Should().Be("HELIOS-201");

        var activityPage = await GetJsonAsync(founderClient, $"/api/v1/kernel/activity?scope={Uri.EscapeDataString(studyRef)}", ct);
        activityPage.ValueKind.Should().Be(JsonValueKind.Object);

        // New clinical study mutation is blocked with 403 K.Subscription.Expired
        var blockedStudy = await SendAsPersonAsync(
            host.Services,
            founderSub,
            new RegisterStudy(
                tenantId,
                StudyId.New(clock.UtcNow),
                sponsorId,
                "HELIOS-202",
                "Blocked Study During Expired Trial",
                "Phase II",
                "idem-helios-blocked-202",
                "Attempt new study while expired"),
            ct);
        blockedStudy.IsSuccess.Should().BeFalse();
        blockedStudy.Error!.Code.Should().Be(KernelErrors.SubscriptionExpired.Code);
        blockedStudy.Error!.Code.Should().Be("K.Subscription.Expired");
        blockedStudy.Error.HttpStatus.Should().Be(403);

        // HTTP mutation endpoint (POST /api/v1/kernel/observations) also returns 403 ProblemDetails with code = K.Subscription.Expired
        using (var obsReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/observations")
        {
            Content = JsonContent.Create(new
            {
                observation_id = ObservationId.New(clock.UtcNow).Value,
                enrollment_ref = NodeRef.For(EnrollmentId.New(clock.UtcNow)).ToString(),
                concept = $"{ConceptId.New(clock.UtcNow)}@1",
                value = new { type = "boolean", value = true },
                observed_at = clock.UtcNow,
            }),
        })
        {
            obsReq.Headers.Add("Idempotency-Key", "idem-obs-expired-http");
            obsReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Attempt observation entry after trial expiry"));
            using var obsResp = await founderClient.SendAsync(obsReq, ct);
            string obsBody = await obsResp.Content.ReadAsStringAsync(ct);
            obsResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, obsBody);
            JsonDocument.Parse(obsBody).RootElement.GetProperty("code").GetString().Should().Be("K.Subscription.Expired");
        }

        // Baseline GxP electronic signature (Attest) is NEVER gated behind paid add-ons or blocked by trial expiration
        var attestationResult = await SendAsPersonAsync(
            host.Services,
            founderSub,
            new Attest(
                tenantId,
                AttestationId.New(clock.UtcNow),
                AttestationMeaning.Reviewed,
                new AttestationTarget(NodeKinds.Assertion, AssertionId.New(clock.UtcNow).Value, ContentHash.Compute("{\"study\":\"HELIOS-201\"}")),
                clock.UtcNow,
                "oidc+reauth",
                "idem-baseline-attest-while-expired",
                "Sign study review record for regulatory export"),
            ct);
        attestationResult.IsSuccess.Should().BeTrue(attestationResult.Error?.Message);
    }

    [Fact(DisplayName = "OnboardedFounder_CreatesStudyWithOneStepSandboxSiteLink_RegistersCustomSite_AndIsolatesSitesAcrossTenants")]
    public async Task OnboardedFounderCreatesStudyWithOneStepSandboxSiteLinkRegistersCustomSiteAndIsolatesSitesAcrossTenants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        var keycloak = new RecordingKeycloakOnboardingClient();

        using var host = CreateHost(clock, keycloak, environment: "Production");

        string founderSub = Guid.CreateVersion7(clock.UtcNow).ToString();
        string otherSub = Guid.CreateVersion7(clock.UtcNow.AddSeconds(5)).ToString();

        using var unprovisioned = host.CreateSignedClient(new GatewayIdentity(founderSub, [], [], TenantId: null));
        using var onboardResp = await unprovisioned.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Vanguard Neuro Clinical", residency = "US", tier = "Individual" },
            ct);
        onboardResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var onboardJson = JsonDocument.Parse(await onboardResp.Content.ReadAsStringAsync(ct)).RootElement;

        string tenantCode = onboardJson.GetProperty("tenant_code").GetString()!;
        string sponsorRef = onboardJson.GetProperty("organization_ref").GetString()!;
        string sandboxSiteCode = onboardJson.GetProperty("sandbox_site_code").GetString()!;
        onboardJson.GetProperty("sandbox_site_country").GetString().Should().Be("US");

        using var founderClient = host.CreateSignedClient(
            new GatewayIdentity(
                founderSub,
                ["Sponsor Admin", "Sponsor Designer", "Site Investigator"],
                [sandboxSiteCode],
                TenantId: tenantCode,
                StudyIds: []));

        // 1. Starter sandbox site is listed in GET /api/v1/kernel/sites
        var initialSites = await GetJsonAsync(founderClient, "/api/v1/kernel/sites", ct);
        initialSites.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("code").GetString().Should().Be(sandboxSiteCode);

        // 2. One-step study creation links the default sandbox site and syncs explicit study_ids to Keycloak
        using var createStudyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/studies")
        {
            Content = JsonContent.Create(new
            {
                study_code = "CADENCE-101",
                title = "Vanguard First-in-Human Trial",
                phase = "Phase I",
                sponsor_ref = sponsorRef,
                link_sandbox_site = true,
            }),
        };
        createStudyReq.Headers.Add("Idempotency-Key", "idem-vanguard-study-1");
        createStudyReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Create study and link starter sandbox site"));
        using var createStudyResp = await founderClient.SendAsync(createStudyReq, ct);
        string createStudyBody = await createStudyResp.Content.ReadAsStringAsync(ct);
        createStudyResp.StatusCode.Should().Be(HttpStatusCode.Created, createStudyBody);

        var createdStudy = JsonDocument.Parse(createStudyBody).RootElement;
        createdStudy.GetProperty("code").GetString().Should().Be("CADENCE-101");
        createdStudy.GetProperty("site_count").GetInt32().Should().Be(1);
        createdStudy.GetProperty("linked_site").GetProperty("site_code").GetString().Should().Be(sandboxSiteCode);

        keycloak.StudyGrants.Should().Contain(g =>
            g.SubjectId == founderSub && g.TenantCode == tenantCode && g.StudyCode == "CADENCE-101");

        // 3. Site and study-site mutations require Idempotency-Key and X-Change-Reason headers
        using (var missingHeadersReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/organizations")
        {
            Content = JsonContent.Create(new { code = "INST-TORONTO", name = "Toronto Neuro Centre", kind = "SiteInstitution" }),
        })
        {
            using var missingResp = await founderClient.SendAsync(missingHeadersReq, ct);
            missingResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // 4. Register a custom SiteInstitution, register an investigational site with ISO country, and link it to CADENCE-101
        string customInstRef;
        using (var createOrgReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/organizations")
        {
            Content = JsonContent.Create(new { code = "INST-TORONTO", name = "Toronto Neuro Centre", kind = "SiteInstitution" }),
        })
        {
            createOrgReq.Headers.Add("Idempotency-Key", "idem-vanguard-inst-toronto");
            createOrgReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Register Toronto site institution"));
            using var createOrgResp = await founderClient.SendAsync(createOrgReq, ct);
            string orgBody = await createOrgResp.Content.ReadAsStringAsync(ct);
            createOrgResp.StatusCode.Should().Be(HttpStatusCode.Created, orgBody);
            customInstRef = JsonDocument.Parse(orgBody).RootElement.GetProperty("ref").GetString()!;
        }

        string customSiteRef;
        using (var createSiteReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/sites")
        {
            Content = JsonContent.Create(new
            {
                site_code = "SITE-TOR-01",
                name = "Toronto General Neurology Unit",
                country = "ca",
                institution_ref = customInstRef,
            }),
        })
        {
            createSiteReq.Headers.Add("Idempotency-Key", "idem-vanguard-site-tor-01");
            createSiteReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Register Toronto investigational site"));
            using var createSiteResp = await founderClient.SendAsync(createSiteReq, ct);
            string siteBody = await createSiteResp.Content.ReadAsStringAsync(ct);
            createSiteResp.StatusCode.Should().Be(HttpStatusCode.Created, siteBody);
            var siteJson = JsonDocument.Parse(siteBody).RootElement;
            siteJson.GetProperty("country").GetString().Should().Be("CA");
            customSiteRef = siteJson.GetProperty("ref").GetString()!;
        }

        using (var linkSiteReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/studies/CADENCE-101/sites")
        {
            Content = JsonContent.Create(new { site_ref = customSiteRef }),
        })
        {
            linkSiteReq.Headers.Add("Idempotency-Key", "idem-vanguard-link-tor-01");
            linkSiteReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Link Toronto site to CADENCE-101"));
            using var linkResp = await founderClient.SendAsync(linkSiteReq, ct);
            string linkBody = await linkResp.Content.ReadAsStringAsync(ct);
            linkResp.StatusCode.Should().Be(HttpStatusCode.Created, linkBody);
            JsonDocument.Parse(linkBody).RootElement.GetProperty("site_code").GetString().Should().Be("SITE-TOR-01");
        }

        var studySites = await GetJsonAsync(founderClient, "/api/v1/kernel/studies/CADENCE-101/sites", ct);
        studySites.EnumerateArray().Select(s => s.GetProperty("site_code").GetString())
            .Should().BeEquivalentTo(sandboxSiteCode, "SITE-TOR-01");

        // 5. A second tenant only sees its own starter sandbox site, never Vanguard's sites or CADENCE-101 study sites
        using var unprovisionedOther = host.CreateSignedClient(new GatewayIdentity(otherSub, [], [], TenantId: null));
        using var otherOnboard = await unprovisionedOther.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Solstice Bio", residency = "US", tier = "Individual" },
            ct);
        otherOnboard.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherJson = JsonDocument.Parse(await otherOnboard.Content.ReadAsStringAsync(ct)).RootElement;
        string otherTenantCode = otherJson.GetProperty("tenant_code").GetString()!;
        string otherSandboxSite = otherJson.GetProperty("sandbox_site_code").GetString()!;

        using var otherClient = host.CreateSignedClient(
            new GatewayIdentity(otherSub, ["Sponsor Admin", "Sponsor Designer", "Site Investigator"], [otherSandboxSite], TenantId: otherTenantCode));
        var otherSites = await GetJsonAsync(otherClient, "/api/v1/kernel/sites", ct);
        otherSites.EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Equal(otherSandboxSite);
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateHost(
        FakeClock clock,
        IKeycloakOnboardingClient keycloakClient,
        string environment) =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<IKeycloakOnboardingClient>();
                services.RemoveAll<IKernelIdentityScopeSynchronizer>();
                services.AddSingleton(keycloakClient);
                services.AddSingleton<IKernelIdentityScopeSynchronizer>(keycloakClient);
            }));

    private static async Task<CommandResult<TResult>> SendAsPersonAsync<TResult>(
        IServiceProvider rootServices,
        string subjectId,
        ICommand<TResult> command,
        CancellationToken ct)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, subjectId),
                new System.Security.Claims.Claim("sub", subjectId),
            ],
            authenticationType: "Gateway");
        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(identity),
        };
        try
        {
            var sender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
            return await sender.SendAsync(command, cancellationToken: ct);
        }
        finally
        {
            accessor.HttpContext = previous;
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<string?> ScalarStringAsync(
        NpgsqlConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is DBNull or null ? null : (string)result;
    }

    private sealed class RecordingKeycloakOnboardingClient : IKeycloakOnboardingClient
    {
        public ConcurrentQueue<(string SubjectId, string TenantCode, string[] Roles, string[] SiteCodes, string[] StudyCodes)> Calls { get; } = new();

        public ConcurrentQueue<(string SubjectId, string? TenantCode, string StudyCode)> StudyGrants { get; } = new();

        public Task AssignTrialWorkspaceAsync(
            string subjectId,
            string tenantCode,
            IReadOnlyList<string> realmRoles,
            IReadOnlyList<string> siteCodes,
            IReadOnlyList<string> studyCodes,
            CancellationToken cancellationToken)
        {
            Calls.Enqueue((subjectId, tenantCode, [.. realmRoles], [.. siteCodes], [.. studyCodes]));
            return Task.CompletedTask;
        }

        public Task GrantStudyScopeAsync(
            Actor caller,
            TenantId tenantId,
            string? tenantCode,
            string studyCode,
            CancellationToken cancellationToken = default)
        {
            if (caller.Kind == ActorKind.Person)
            {
                StudyGrants.Enqueue((caller.Id, tenantCode, studyCode));
            }

            return Task.CompletedTask;
        }
    }
}
