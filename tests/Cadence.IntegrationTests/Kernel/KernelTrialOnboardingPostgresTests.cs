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
            .Should().Equal("Sponsor Admin", "Sponsor Designer", "Trial Coordinator");
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
            && c.Roles.SequenceEqual(new[] { "Sponsor Admin", "Sponsor Designer", "Trial Coordinator" }));

        // Verify PostgreSQL state: 1 tenant, 1 org, 1 pseudonymous person (zero name/email columns), 1 membership, 1 subscription.
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
            (await ScalarLongAsync(conn, "SELECT count(*) FROM kernel.organizations WHERE tenant_id = @tid", ("tid", tenantId))).Should().Be(1L);
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
            new GatewayIdentity(subjectTwo, ["Sponsor Admin", "Sponsor Designer", "Trial Coordinator"], ["*"], TenantId: tenantCodeTwo));
        var searchTwo = await GetJsonAsync(refreshedTwo, "/api/v1/kernel/search?q=MERIDIAN-101", ct);
        searchTwo.GetArrayLength().Should().Be(0, "PostgreSQL RLS isolates self-registered trial tenants");
    }

    [Fact(DisplayName = "TrialFounderRegistersSiteInstitutionLinksToStudyAndScreensParticipant_AcrossIsolatedTrialTenants")]
    public async Task TrialFounderRegistersSiteInstitutionLinksToStudyAndScreensParticipantAcrossIsolatedTrialTenants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        var keycloak = new RecordingKeycloakOnboardingClient();

        using var host = CreateHost(clock, keycloak, environment: "Production");

        foreach (int idx in new[] { 1, 2 })
        {
            string founderSub = Guid.CreateVersion7(clock.UtcNow.AddSeconds(idx * 10)).ToString();
            using var unprovisioned = host.CreateSignedClient(new GatewayIdentity(founderSub, [], [], TenantId: null));
            using var onboardResp = await unprovisioned.PostAsJsonAsync(
                "/api/v1/kernel/onboarding/trial",
                new { organization_name = $"Founder Org {idx}", residency = "US", tier = "Individual" },
                ct);
            onboardResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var onboardJson = JsonDocument.Parse(await onboardResp.Content.ReadAsStringAsync(ct)).RootElement;
            string tenantCode = onboardJson.GetProperty("tenant_code").GetString()!;
            string sponsorRef = onboardJson.GetProperty("organization_ref").GetString()!;

            using var founderClient = host.CreateSignedClient(
                new GatewayIdentity(
                    founderSub,
                    ["Sponsor Admin", "Sponsor Designer", "Trial Coordinator"],
                    ["*"],
                    TenantId: tenantCode));

            // 1. Create CADENCE-101 via HTTP endpoint
            using (var studyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/studies")
            {
                Content = JsonContent.Create(new
                {
                    study_code = "CADENCE-101",
                    title = "Cardiovascular Feasibility Trial",
                    phase = "Phase 2",
                    sponsor_ref = sponsorRef,
                }),
            })
            {
                studyReq.Headers.Add("Idempotency-Key", $"idem-study-{idx}");
                studyReq.Headers.Add(GatewayHeaders.ChangeReason, "Create CADENCE-101 in trial workspace");
                using var studyResp = await founderClient.SendAsync(studyReq, ct);
                studyResp.StatusCode.Should().Be(HttpStatusCode.Created, await studyResp.Content.ReadAsStringAsync(ct));
            }

            // 2. Register SITE-01 via POST /api/v1/kernel/sites -> resolves/registers OrganizationKind.SiteInstitution
            using (var siteReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/sites")
            {
                Content = JsonContent.Create(new
                {
                    site_code = "SITE-01",
                    name = "Metro Cardiology Research Center",
                    country = "US",
                    institution_name = $"Founder Org {idx}",
                }),
            })
            {
                siteReq.Headers.Add("Idempotency-Key", $"idem-site-{idx}");
                siteReq.Headers.Add(GatewayHeaders.ChangeReason, "Register clinical site in trial workspace");
                using var siteResp = await founderClient.SendAsync(siteReq, ct);
                string siteBody = await siteResp.Content.ReadAsStringAsync(ct);
                siteResp.StatusCode.Should().Be(HttpStatusCode.Created, siteBody);
            }

            var siteInstitutions = await GetJsonAsync(founderClient, "/api/v1/kernel/organizations?kind=SiteInstitution", ct);
            siteInstitutions.EnumerateArray().Should().ContainSingle(o => o.GetProperty("kind").GetString() == "SiteInstitution");

            // 3. Link SITE-01 to CADENCE-101 via POST /api/v1/kernel/studies/CADENCE-101/sites
            using (var linkReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/studies/CADENCE-101/sites")
            {
                Content = JsonContent.Create(new { site_code = "SITE-01" }),
            })
            {
                linkReq.Headers.Add("Idempotency-Key", $"idem-link-{idx}");
                linkReq.Headers.Add(GatewayHeaders.ChangeReason, "Link SITE-01 to CADENCE-101");
                using var linkResp = await founderClient.SendAsync(linkReq, ct);
                string linkBody = await linkResp.Content.ReadAsStringAsync(ct);
                linkResp.StatusCode.Should().Be(HttpStatusCode.Created, linkBody);
                JsonDocument.Parse(linkBody).RootElement.GetProperty("site_code").GetString().Should().Be("SITE-01");
            }

            var studySites = await GetJsonAsync(founderClient, "/api/v1/kernel/studies/CADENCE-101/sites", ct);
            studySites.EnumerateArray().Should().ContainSingle(s => s.GetProperty("site_code").GetString() == "SITE-01");

            // 4. Screen SUBJ-001 via POST /api/v1/kernel/enrollments/screen -> 201 Created without StudySiteUnknown
            using var screenReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/enrollments/screen")
            {
                Content = JsonContent.Create(new
                {
                    participant_id = Guid.CreateVersion7(clock.UtcNow.AddSeconds(idx * 20)),
                    study_code = "CADENCE-101",
                    site_code = "SITE-01",
                    subject_code = "SUBJ-001",
                    screened_at = clock.UtcNow,
                }),
            };
            screenReq.Headers.Add("Idempotency-Key", $"idem-screen-{idx}");
            screenReq.Headers.Add(GatewayHeaders.ChangeReason, "Screen first participant at linked site");
            using var screenResp = await founderClient.SendAsync(screenReq, ct);
            string screenBody = await screenResp.Content.ReadAsStringAsync(ct);
            screenResp.StatusCode.Should().Be(HttpStatusCode.Created, screenBody);
        }
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

        string originalTrialEndsAt = onboardJson.GetProperty("entitlements").GetProperty("trial_ends_at").GetString()!;
        originalTrialEndsAt.Should().NotBeNullOrWhiteSpace();

        // Multi-step commercial tier transitions during an active trial preserve Status == Trialing and original trial_ends_at:
        // Individual -> Select -> Complete -> Select -> Complete
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
            upgraded.GetProperty("status").GetString().Should().Be("Trialing", "switching tiers on an active trial must not bypass scoping intake");
            upgraded.GetProperty("trial_ends_at").GetString().Should().Be(originalTrialEndsAt, "switching trial tiers preserves the original 30-day trial end timestamp");
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

        // 3. Direct trial tier switching (POST /api/v1/kernel/entitlements/tier) is refused with 403 K.Subscription.Expired once expired,
        //    so an expired workspace can never reactivate without completing structured study scoping.
        using (var blockedTierSwitchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/entitlements/tier")
        {
            Content = JsonContent.Create(new { tier = "Select" }),
        })
        {
            blockedTierSwitchReq.Headers.Add("Idempotency-Key", "idem-expired-tier-switch-blocked");
            blockedTierSwitchReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Attempt tier switch after trial expiry"));
            using var blockedTierSwitchResp = await founderClient.SendAsync(blockedTierSwitchReq, ct);
            string blockedTierSwitchBody = await blockedTierSwitchResp.Content.ReadAsStringAsync(ct);
            blockedTierSwitchResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, blockedTierSwitchBody);
            JsonDocument.Parse(blockedTierSwitchBody).RootElement.GetProperty("code").GetString().Should().Be("K.Subscription.Expired");
        }

        // 4. Scoped activation without required scoping fields fails validation with 422 K.Commercial.ScopingInvalid and field errors.
        using (var invalidActivateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/entitlements/activate")
        {
            Content = JsonContent.Create(new { tier = "Select" }),
        })
        {
            invalidActivateReq.Headers.Add("Idempotency-Key", "idem-activate-missing-scoping");
            invalidActivateReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Attempt activation without scoping fields"));
            using var invalidActivateResp = await founderClient.SendAsync(invalidActivateReq, ct);
            string invalidActivateBody = await invalidActivateResp.Content.ReadAsStringAsync(ct);
            invalidActivateResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, invalidActivateBody);
            var invalidDoc = JsonDocument.Parse(invalidActivateBody).RootElement;
            invalidDoc.GetProperty("code").GetString().Should().Be("K.Commercial.ScopingInvalid");
            var fieldErrors = invalidDoc.GetProperty("errors");
            fieldErrors.TryGetProperty("study_phase", out _).Should().BeTrue();
            fieldErrors.TryGetProperty("study_duration_months", out _).Should().BeTrue();
            fieldErrors.TryGetProperty("countries", out _).Should().BeTrue();
            fieldErrors.TryGetProperty("site_count", out _).Should().BeTrue();
            fieldErrors.TryGetProperty("participant_count", out _).Should().BeTrue();
            fieldErrors.TryGetProperty("active_study_count", out _).Should().BeTrue();
            fieldErrors.TryGetProperty("billing_confirmation", out _).Should().BeTrue();
        }

        // 5. Scoped activation with valid study scoping and PO/quote confirmation transitions Expired -> Active, clears read-only,
        //    persists scoping metadata, restores study mutations, and still enforces clinical Grant boundaries!
        using (var validActivateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/entitlements/activate")
        {
            Content = JsonContent.Create(new
            {
                tier = "Select",
                study_phase = "Phase II",
                study_duration_months = 24,
                countries = new[] { "US", "DE", "NL" },
                site_count = 12,
                participant_count = 240,
                active_study_count = 2,
                billing_confirmation = "PO-HELIOS-2026-441",
            }),
        })
        {
            validActivateReq.Headers.Add("Idempotency-Key", "idem-activate-valid-scoping");
            validActivateReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Activate Select subscription with validated study scoping"));
            using var validActivateResp = await founderClient.SendAsync(validActivateReq, ct);
            string validActivateBody = await validActivateResp.Content.ReadAsStringAsync(ct);
            validActivateResp.StatusCode.Should().Be(HttpStatusCode.OK, validActivateBody);
            var activated = JsonDocument.Parse(validActivateBody).RootElement;
            activated.GetProperty("tier").GetString().Should().Be("Select");
            activated.GetProperty("status").GetString().Should().Be("Active");
            activated.GetProperty("is_read_only").GetBoolean().Should().BeFalse();
            var scopingJson = activated.GetProperty("scoping");
            scopingJson.GetProperty("study_phase").GetString().Should().Be("Phase II");
            scopingJson.GetProperty("study_duration_months").GetInt32().Should().Be(24);
            scopingJson.GetProperty("countries").EnumerateArray().Select(c => c.GetString()).Should().Equal("US", "DE", "NL");
            scopingJson.GetProperty("site_count").GetInt32().Should().Be(12);
            scopingJson.GetProperty("participant_count").GetInt32().Should().Be(240);
            scopingJson.GetProperty("active_study_count").GetInt32().Should().Be(2);
            scopingJson.GetProperty("billing_confirmation").GetString().Should().Be("PO-HELIOS-2026-441");
        }

        // Clinical study mutation succeeds again now that the workspace is Active
        var restoredStudy = await SendAsPersonAsync(
            host.Services,
            founderSub,
            new RegisterStudy(
                tenantId,
                StudyId.New(clock.UtcNow),
                sponsorId,
                "HELIOS-203",
                "Restored Study After Scoped Activation",
                "Phase II",
                "idem-helios-restored-203",
                "Register study after scoped subscription activation"),
            ct);
        restoredStudy.IsSuccess.Should().BeTrue(restoredStudy.Error?.Message);
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
                services.AddSingleton(keycloakClient);
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
        public ConcurrentQueue<(string SubjectId, string TenantCode, string[] Roles)> Calls { get; } = new();

        public Task AssignTrialWorkspaceAsync(
            string subjectId,
            string tenantCode,
            IReadOnlyList<string> realmRoles,
            CancellationToken cancellationToken)
        {
            Calls.Enqueue((subjectId, tenantCode, [.. realmRoles]));
            return Task.CompletedTask;
        }
    }
}
