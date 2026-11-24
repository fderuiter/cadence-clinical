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

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Application.Supply;
using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Cgt;
using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// End-to-end <c>CallerScope</c> study and site isolation checks across all ten <c>apps/execution</c> endpoint groups
/// through <see cref="ExecutionApiFactory"/> (<see cref="WebApplicationFactory{TEntryPoint}"/> of
/// <see cref="Cadence.Apps.Execution.Program"/>). Verifies fail-closed HTTP 403 <c>ProblemDetails</c> responses for
/// cross-study, cross-site, and missing-scope callers, confirms <c>RandomizationAllocation.ArmCiphertext</c> is never
/// decrypted before study and site checks succeed, verifies site-optional list narrowing, and requires wildcard
/// (<c>*</c>) study scope for global (<c>ENTERPRISE</c>) synonym operations.
/// </summary>
public sealed class ExecutionCallerScopeTests : IDisposable
{
    private const string Reason = "CallerScope isolation verification with clinical justification exceeding fifty characters.";
    private const string StudyAlpha = "STUDY-ALPHA";
    private const string StudyBeta = "STUDY-BETA";
    private const string Site01 = "SITE-01";
    private const string Site02 = "SITE-02";
    private const string SubjectBeta01 = "SUBJ-BETA-01";
    private const string SecretArm = "ACTIVE_DRUG_100MG"; // pragma: allowlist secret
    private const string DeviceSerialBeta = "SN-BETA-001";
    private const string LabRangeBeta = "HGB";

    private static readonly GatewayIdentity CrcAlpha = new(
        UserId: "crc-alpha",
        Roles: ["crc"],
        SiteIds: [Site01],
        TenantId: "tenant-alpha",
        StudyIds: [StudyAlpha],
        ChangeReason: Reason);

    private static readonly GatewayIdentity PharmAlpha = new(
        UserId: "pharm-alpha",
        Roles: ["pharmacist"],
        SiteIds: [Site01],
        TenantId: "tenant-alpha",
        StudyIds: [StudyAlpha],
        UnblindedAccess: true,
        ChangeReason: Reason);

    private readonly ExecutionApiFactory _baseFactory = new();
    private readonly SpyAllocationProtector _protector = new();
    private readonly WebApplicationFactory<Cadence.Apps.Execution.Program> _factory;

    public ExecutionCallerScopeTests()
    {
        _factory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var existingProtector = services.Single(d => d.ServiceType == typeof(IAllocationProtector));
                services.Remove(existingProtector);
                services.AddSingleton<IAllocationProtector>(sp =>
                {
                    var inner = (IAllocationProtector)(existingProtector.ImplementationInstance
                        ?? existingProtector.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, existingProtector.ImplementationType!));
                    _protector.Attach(inner);
                    return _protector;
                });
            }));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Callers outside the study or site are refused with 403 before allocation ciphertexts can be revealed.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task CrossStudyAndTenantCallers_CrcAlphaAndPharmAlpha_Receive403AcrossSeededStudyBetaRoutes_WithoutRevealingArm()
    {
        await SeedStudyBetaFixturesAsync();
        _protector.ResetRevealCount();

        foreach (var caller in new[] { CrcAlpha, PharmAlpha })
        {
            using var client = _factory.CreateSignedClient(caller);

            await AssertForbiddenWithoutArmLeakAsync(
                await client.GetAsync($"/api/v1/execution/subjects?study_id={StudyBeta}", Ct));

            await AssertForbiddenWithoutArmLeakAsync(
                await client.GetAsync($"/api/v1/execution/subjects/{SubjectBeta01}", Ct));

            await AssertForbiddenWithoutArmLeakAsync(
                await client.PostAsJsonAsync(
                    "/api/v1/execution/subjects",
                    new SubjectCreateRequest("SUBJ-BETA-UNAUTHORIZED", StudyBeta, Site02, null),
                    ExecutionApiFactory.JsonOptions,
                    Ct));

            await AssertForbiddenWithoutArmLeakAsync(
                await SendJsonWithReasonAsync(
                    client,
                    HttpMethod.Put,
                    $"/api/v1/execution/subjects/{SubjectBeta01}/demographics",
                    new SubjectDemographicsUpdateRequest(new DemographicsPayload("Jane Roe", "1985-05-05", "F", null), null))); // pragma: allowlist secret

            await AssertForbiddenWithoutArmLeakAsync(
                await SendJsonWithReasonAsync(
                    client,
                    HttpMethod.Patch,
                    $"/api/v1/execution/subjects/{SubjectBeta01}/state",
                    new SubjectStateUpdateRequest("ACTIVE", null)));

            await AssertForbiddenWithoutArmLeakAsync(
                await client.GetAsync($"/api/v1/execution/devices/inventory?study_id={StudyBeta}", Ct));

            await AssertForbiddenWithoutArmLeakAsync(
                await client.GetAsync($"/api/v1/execution/lab-ranges?study_id={StudyBeta}", Ct));
        }

        _protector.RevealCount.Should().Be(0,
            "an out-of-scope unblinded caller must be rejected before IAllocationProtector.Reveal is ever called");

        // Same study (STUDY-BETA) but wrong site (SITE-01 instead of SITE-02) also fails closed before Reveal.
        var pharmBetaWrongSite = PharmAlpha with { UserId = "pharm-beta-s1", TenantId = "tenant-beta", StudyIds = [StudyBeta], SiteIds = [Site01] };
        using (var wrongSiteClient = _factory.CreateSignedClient(pharmBetaWrongSite))
        {
            await AssertForbiddenWithoutArmLeakAsync(
                await wrongSiteClient.GetAsync($"/api/v1/execution/subjects/{SubjectBeta01}", Ct));
        }

        _protector.RevealCount.Should().Be(0,
            "a cross-site unblinded caller within the same study must also fail closed before decrypting ArmCiphertext");

        // Positive control: unblinded pharmacist scoped to STUDY-BETA and SITE-02 sees the decrypted arm and inventory.
        var pharmBetaAuthorized = PharmAlpha with { UserId = "pharm-beta-s2", TenantId = "tenant-beta", StudyIds = [StudyBeta], SiteIds = [Site02] };
        using var authorizedClient = _factory.CreateSignedClient(pharmBetaAuthorized);

        var detailResp = await authorizedClient.GetAsync($"/api/v1/execution/subjects/{SubjectBeta01}", Ct);
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK, await detailResp.Content.ReadAsStringAsync(Ct));
        using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync(Ct));
        detailDoc.RootElement.GetProperty("treatment_group").GetString().Should().Be(SecretArm);
        _protector.RevealCount.Should().Be(1);

        var invResp = await authorizedClient.GetAsync($"/api/v1/execution/devices/inventory?study_id={StudyBeta}", Ct);
        invResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await invResp.Content.ReadAsStringAsync(Ct)).Should().Contain(DeviceSerialBeta);

        var crcBetaAuthorized = CrcAlpha with { UserId = "crc-beta-s2", TenantId = "tenant-beta", StudyIds = [StudyBeta], SiteIds = [Site02] };
        using var authorizedCrcClient = _factory.CreateSignedClient(crcBetaAuthorized);
        var rangeResp = await authorizedCrcClient.GetAsync($"/api/v1/execution/lab-ranges?study_id={StudyBeta}", Ct);
        rangeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rangeResp.Content.ReadAsStringAsync(Ct)).Should().Contain(LabRangeBeta);
    }

    /// <summary>
    /// Cross-site subject mutations within an assigned study answer 403 and leave stored subjects unchanged.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task SubjectMutations_CrossSiteWithinAssignedStudy_Return403AndLeaveSubjectUnchanged()
    {
        await SeedStudyBetaFixturesAsync();
        var crcBetaSite01 = CrcAlpha with { UserId = "crc-beta-s1", TenantId = "tenant-beta", StudyIds = [StudyBeta], SiteIds = [Site01] };
        using var client = _factory.CreateSignedClient(crcBetaSite01);

        var consent = await client.PostAsJsonAsync(
            $"/api/v1/execution/subjects/{SubjectBeta01}/consent",
            new SubjectConsentRequest(new ProtocolVersionPayload(StudyBeta, "1.0", 1), IcfSigned: true, IcfSignedDate: FakeClock.DefaultStart),
            ExecutionApiFactory.JsonOptions,
            Ct);
        var deleteDemo = await SendWithReasonAsync(client, HttpMethod.Delete, $"/api/v1/execution/subjects/{SubjectBeta01}/demographics"); // pragma: allowlist secret
        var wrongSiteCreate = await client.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-WRONG-SITE", StudyBeta, Site02, null),
            ExecutionApiFactory.JsonOptions,
            Ct);

        consent.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deleteDemo.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        wrongSiteCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var stored = await db.ClinicalSubjects.AsNoTracking()
            .SingleAsync(s => s.SubjectCode == new SubjectCode(SubjectBeta01), Ct);
        stored.EncryptedDemographics.Should().Be("beta-seed-ciphertext");
        stored.Status.Should().Be(SubjectState.Randomized);
    }

    /// <summary>
    /// Site-scoped callers omitting site_id receive only assigned-site rows, and empty site assignments answer 403.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task SiteScopedCallers_OmittingSiteIdOnListEndpoints_ReceiveOnlyAssignedSiteRows_AndEmptySitesReturn403()
    {
        await SeedDualSiteListFixturesAsync();

        var crcSite01 = new GatewayIdentity(
            UserId: "crc-site01",
            Roles: ["crc"],
            SiteIds: [Site01],
            TenantId: "tenant-beta",
            StudyIds: [StudyBeta],
            ChangeReason: Reason);

        using var site01Client = _factory.CreateSignedClient(crcSite01);

        await AssertContainsOnlySite01Async(site01Client, $"/api/v1/execution/subjects?study_id={StudyBeta}", "SUBJ-LIST-S1", "SUBJ-LIST-S2");
        await AssertContainsOnlySite01Async(site01Client, $"/api/v1/execution/devices/inventory?study_id={StudyBeta}", "SN-LIST-S1", "SN-LIST-S2");
        await AssertContainsOnlySite01Async(site01Client, $"/api/v1/execution/devices/deficiencies?study_id={StudyBeta}", "DEF-S1", "DEF-S2");
        await AssertContainsOnlySite01Async(site01Client, $"/api/v1/execution/devices/quarantine?study_id={StudyBeta}", "QUAR-S1", "QUAR-S2");
        await AssertContainsOnlySite01Async(site01Client, $"/api/v1/execution/lab-ranges?study_id={StudyBeta}", "ALT", "AST");
        await AssertContainsOnlySite01Async(site01Client, $"/api/v1/execution/labs/toxicity/review-queue?study_id={StudyBeta}", "SUBJ-LIST-S1", "SUBJ-LIST-S2");

        // Site-scoped caller with empty AssignedSites fails closed with 403 on every list endpoint.
        var crcNoSites = crcSite01 with { UserId = "crc-nosites", SiteIds = [] };
        using var noSitesClient = _factory.CreateSignedClient(crcNoSites);

        foreach (string path in new[]
        {
            $"/api/v1/execution/subjects?study_id={StudyBeta}",
            $"/api/v1/execution/devices/inventory?study_id={StudyBeta}",
            $"/api/v1/execution/devices/deficiencies?study_id={StudyBeta}",
            $"/api/v1/execution/devices/quarantine?study_id={StudyBeta}",
            $"/api/v1/execution/lab-ranges?study_id={StudyBeta}",
            $"/api/v1/execution/labs/toxicity/review-queue?study_id={StudyBeta}",
        })
        {
            var resp = await noSitesClient.GetAsync(path, Ct);
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"empty AssignedSites must fail closed on {path}");
        }
    }

    /// <summary>
    /// Randomization, IP kits, site compliance, synonyms, eTMF webhooks, CGT, CEC, and lab toxicity routes enforce caller scope fail-closed.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task RemainingEndpointGroups_EnforceStudySiteAndGlobalSynonymScope_FailClosedWith403()
    {
        var seeded = await SeedRemainingEndpointFixturesAsync();

        // 1. RandomizationEndpoints: draft, list, approve (stored scheme), and randomize (stored subject)
        var statAlpha = new GatewayIdentity(UserId: "stat-alpha", Roles: [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN], SiteIds: [], TenantId: "tenant-alpha", StudyIds: [StudyAlpha], ChangeReason: Reason);
        using (var statClient = _factory.CreateSignedClient(statAlpha))
        {
            var draft = await statClient.PostAsJsonAsync(
                "/api/v1/execution/randomization/schemes",
                new RandomizationSchemeDraftRequest(new StudyCode(StudyBeta), "minimization", new Dictionary<string, int> { ["A"] = 1, ["B"] = 1 }, null, null, null, 0.8, false),
                ExecutionApiFactory.JsonOptions,
                Ct);
            draft.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await statClient.GetAsync($"/api/v1/execution/randomization/schemes?study_id={StudyBeta}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            string approvePath = RandomizationEndpoints.ApproveSchemeAction(seeded.DraftSchemeId);
            string approveSig = TestSecurityContext.CreateSigToken(
                statAlpha.UserId, approvePath, RandomizationEndpoints.ApproveSchemeSemanticAction, [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN]);
            using var approveReq = new HttpRequestMessage(HttpMethod.Post, approvePath);
            approveReq.Headers.Add(GatewayHeaders.ChangeReason, Reason);
            approveReq.Headers.Add(GatewayHeaders.SigToken, approveSig);
            (await statClient.SendAsync(approveReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        string randPath = RandomizationEndpoints.RandomizeSubjectAction(seeded.EnrolledSubjectCode);
        var crcWrongSite = new GatewayIdentity(UserId: "crc-beta-s1", Roles: ["crc"], SiteIds: [Site01], TenantId: "tenant-beta", StudyIds: [StudyBeta], ChangeReason: Reason);
        using (var crcClient = _factory.CreateSignedClient(crcWrongSite))
        {
            string randSig = TestSecurityContext.CreateSigToken(
                crcWrongSite.UserId, randPath, RandomizationEndpoints.RandomizeSubjectSemanticAction, [RoleNormalization.ROLE_CRC]);
            using var randReq = new HttpRequestMessage(HttpMethod.Post, randPath)
            {
                Content = JsonContent.Create(new RandomizeSubjectRequest(null), options: ExecutionApiFactory.JsonOptions),
            };
            randReq.Headers.Add(GatewayHeaders.ChangeReason, Reason);
            randReq.Headers.Add(GatewayHeaders.SigToken, randSig);
            (await crcClient.SendAsync(randReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 2. IpKitEndpoints: inventory, seed-inventory, reconcile-batch
        using (var pharmClient = _factory.CreateSignedClient(PharmAlpha))
        {
            (await pharmClient.GetAsync($"/api/v1/execution/rtsm/kits/inventory/{StudyBeta}/{Site02}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await pharmClient.PostAsJsonAsync(
                "/api/v1/execution/rtsm/kits/reconcile-batch",
                new IpKitBatchReconciliationRequest(StudyBeta, Site02, [new IpKitItemReconcileRequest("KIT-B1", "LOT-1", "LOT-1", 0, 0, "RETURNED", "Unused")], ReasonForChange: Reason),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var dmAlpha = new GatewayIdentity(UserId: "dm-alpha", Roles: ["DATA_MANAGER", "Medical Coder"], SiteIds: [], TenantId: "tenant-alpha", StudyIds: [StudyAlpha], ChangeReason: Reason);
        using (var dmAlphaClient = _factory.CreateSignedClient(dmAlpha))
        {
            (await dmAlphaClient.PostAsJsonAsync(
                "/api/v1/execution/rtsm/kits/seed-inventory",
                new SeedInventoryRequest(StudyBeta, Site02, [new IpKitSeedItem("KIT-B2", "LOT-1", FakeClock.DefaultStart.AddYears(1), "AVAILABLE")]),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 3. SiteComplianceEndpoints: activate & compliance-status for STUDY-BETA
            (await dmAlphaClient.PostAsJsonAsync(
                $"/api/v1/execution/sites/{Site02}/activate",
                new SiteActivationRequest(StudyBeta),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await dmAlphaClient.GetAsync($"/api/v1/execution/sites/{Site02}/compliance-status?study_id={StudyBeta}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 4. SynonymEndpoints: non-wildcard Data Manager cannot manage or query ENTERPRISE rules
            (await dmAlphaClient.GetAsync("/api/v1/execution/coding/synonyms", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var importForm = BuildEnterpriseSynonymForm();
            (await dmAlphaClient.PostAsync("/api/v1/execution/coding/synonyms/import", importForm, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await dmAlphaClient.DeleteAsync($"/api/v1/execution/coding/synonyms/{seeded.EnterpriseRuleId}?reason_for_change=cleanup", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // Wildcard ('*') Data Manager CAN list and deactivate ENTERPRISE synonym rules.
        var dmGlobal = dmAlpha with { UserId = "dm-global", StudyIds = [StudyAccessEvaluator.AllStudies] };
        using (var dmGlobalClient = _factory.CreateSignedClient(dmGlobal))
        {
            (await dmGlobalClient.GetAsync("/api/v1/execution/coding/synonyms", Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await dmGlobalClient.DeleteAsync($"/api/v1/execution/coding/synonyms/{seeded.EnterpriseRuleId}?reason_for_change=cleanup", Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 5. eTMF webhook scoped to STUDY-ALPHA cannot write STUDY-BETA compliance
        var etmfAlpha = new GatewayIdentity(UserId: "svc:etmf-alpha", Roles: [PermissionMatrix.EtmfServiceRole], SiteIds: [], TenantId: "tenant-alpha", StudyIds: [StudyAlpha]);
        using (var etmfClient = _factory.CreateSignedClient(etmfAlpha))
        {
            (await etmfClient.PostAsJsonAsync(
                "/api/v1/execution/webhooks/etmf",
                new EtmfCompletenessWebhookPayload(StudyBeta, Site02, "SITE_ACTIVATION", true, null),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 6. CgtEndpoints: stored chain-of-custody ID lookup, subject traceability lookup, and apheresis fail closed on wrong site
        using (var crcWrongSiteClient = _factory.CreateSignedClient(crcWrongSite))
        {
            (await crcWrongSiteClient.PostAsJsonAsync(
                "/api/v1/execution/rtsm/cgt/milestone",
                new CgtMilestoneCreateRequest(StudyBeta, seeded.CocId, "COLLECTION", "HARVESTED"),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await crcWrongSiteClient.GetAsync($"/api/v1/execution/rtsm/cgt/chain-of-custody/{StudyBeta}/{seeded.EnrolledSubjectCode}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await crcWrongSiteClient.PostAsJsonAsync(
                "/api/v1/execution/rtsm/cgt/apheresis",
                new CgtApheresisCreateRequest(StudyBeta, Site02, seeded.EnrolledSubjectCode, "DIN-9999", FakeClock.DefaultStart),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 7. CecEndpoints: stored dossier lookup & chair adjudication fail closed on wrong study
        var cecAlpha = new GatewayIdentity(UserId: "chair-alpha", Roles: [PermissionMatrix.CecChairRole], SiteIds: [], TenantId: "tenant-alpha", StudyIds: [StudyAlpha], ChangeReason: Reason);
        using (var cecClient = _factory.CreateSignedClient(cecAlpha))
        {
            (await cecClient.GetAsync($"/api/v1/execution/cec/dossiers/{seeded.DossierId}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            string cecActionPath = CecEndpoints.ChairAdjudicateAction(seeded.DossierId);
            string cecSig = TestSecurityContext.CreateSigToken(
                cecAlpha.UserId, cecActionPath, CecEndpoints.ChairAdjudicateSemanticAction, [PermissionMatrix.CecChairRole]);
            using var cecReq = new HttpRequestMessage(HttpMethod.Post, cecActionPath)
            {
                Content = JsonContent.Create(
                    new { final_classification = "MACE", final_adjudicated_onset_date = "2026-01-01", adjudication_rationale = Reason },
                    options: ExecutionApiFactory.JsonOptions),
            };
            cecReq.Headers.Add(GatewayHeaders.ChangeReason, Reason);
            cecReq.Headers.Add(GatewayHeaders.SigToken, cecSig);
            (await cecClient.SendAsync(cecReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var cecBeta = cecAlpha with { UserId = "chair-beta", TenantId = "tenant-beta", StudyIds = [StudyBeta] };
        using (var cecBetaClient = _factory.CreateSignedClient(cecBeta))
        {
            (await cecBetaClient.GetAsync($"/api/v1/execution/cec/dossiers/{seeded.DossierId}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 8. LabToxicityEndpoints: stored review action fails closed on wrong site
        var piWrongSite = new GatewayIdentity(UserId: "pi-beta-s1", Roles: ["Site Investigator"], SiteIds: [Site01], TenantId: "tenant-beta", StudyIds: [StudyBeta], ChangeReason: Reason);
        using var piClient = _factory.CreateSignedClient(piWrongSite);
        string toxPath = LabToxicityEndpoints.ReviewActionPath(seeded.ToxicityReviewId);
        string toxSig = TestSecurityContext.CreateSigToken(
            piWrongSite.UserId, toxPath, LabToxicityEndpoints.ReviewActionSemanticAction, [RoleNormalization.ROLE_INVESTIGATOR]);
        using var toxReq = new HttpRequestMessage(HttpMethod.Post, toxPath)
        {
            Content = JsonContent.Create(new StagedAdverseEventActionRequest("DISMISS", Reason), options: ExecutionApiFactory.JsonOptions),
        };
        toxReq.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        toxReq.Headers.Add(GatewayHeaders.SigToken, toxSig);
        (await piClient.SendAsync(toxReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Unassigned pharmacist (and dual sponsor + pharmacist) callers fail closed with 403 on RTSM IP kit routes,
    /// while an assigned pharmacist reaches only their assigned site.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task UnassignedPharmacist_FailsClosedWith403AcrossIpKitRoutes_WhileAssignedPharmacistReachesOnlyAssignedSite()
    {
        var unassignedPharmacist = new GatewayIdentity(
            UserId: "pharm-unassigned",
            Roles: [RoleNormalization.ROLE_PHARMACIST],
            SiteIds: [],
            TenantId: "tenant-01",
            StudyIds: ["STUDY-01"],
            UnblindedAccess: true,
            ChangeReason: Reason);

        const string unassignedInventoryPath = "/api/v1/execution/rtsm/kits/inventory/STUDY-01/SITE-99"; // pragma: allowlist secret

        using (var unassignedClient = _factory.CreateSignedClient(unassignedPharmacist))
        {
            (await unassignedClient.GetAsync(unassignedInventoryPath, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await unassignedClient.PostAsJsonAsync(
                "/api/v1/execution/rtsm/kits/validate-scan",
                new IpKitScanValidateRequest("KIT-01", "STUDY-01", "SITE-99"),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await unassignedClient.GetAsync("/api/v1/execution/rtsm/kits/certificates/CERT-01", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await unassignedClient.PostAsJsonAsync(
                "/api/v1/execution/rtsm/kits/reconcile-batch",
                new IpKitBatchReconciliationRequest(
                    "STUDY-01",
                    "SITE-99",
                    [new IpKitItemReconcileRequest("KIT-01", "LOT-1", "LOT-1", 0, 0, "RETURNED", "Unused")],
                    ReasonForChange: Reason),
                ExecutionApiFactory.JsonOptions,
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var dualRoleUnassigned = unassignedPharmacist with
        {
            UserId = "pharm-dm-unassigned",
            Roles = [RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_PHARMACIST],
        };
        using (var dualClient = _factory.CreateSignedClient(dualRoleUnassigned))
        {
            (await dualClient.GetAsync(unassignedInventoryPath, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var assignedPharmacist = unassignedPharmacist with
        {
            UserId = "pharm-site01",
            SiteIds = [Site01],
        };
        using var assignedClient = _factory.CreateSignedClient(assignedPharmacist);
        (await assignedClient.GetAsync($"/api/v1/execution/rtsm/kits/inventory/STUDY-01/{Site01}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await assignedClient.GetAsync(unassignedInventoryPath, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static MultipartFormDataContent BuildEnterpriseSynonymForm()
    {
        var form = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level\nheadache,MEDDRA,26.0,10019211,Headache,PT\n"));
        form.Add(csv, "file", "synonyms.csv");
        form.Add(new StringContent("ENTERPRISE"), "scope");
        form.Add(new StringContent(Reason), "reason_for_change");
        return form;
    }

    private static async Task<HttpResponseMessage> SendJsonWithReasonAsync<TBody>(
        HttpClient client, HttpMethod method, string path, TBody body)
    {
        using var req = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: ExecutionApiFactory.JsonOptions),
        };
        req.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        return await client.SendAsync(req, Ct);
    }

    private static async Task<HttpResponseMessage> SendWithReasonAsync(HttpClient client, HttpMethod method, string path)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        return await client.SendAsync(req, Ct);
    }

    private static async Task AssertForbiddenWithoutArmLeakAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        body.Should().NotContain(SecretArm);
    }

    private static async Task AssertContainsOnlySite01Async(HttpClient client, string path, string expectedToken, string forbiddenToken)
    {
        var response = await client.GetAsync(path, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain(expectedToken).And.NotContain(forbiddenToken);
    }

    private async Task SeedStudyBetaFixturesAsync()
    {
        using var warmClient = _factory.CreateSignedClient();
        using var scope = _factory.Services.CreateSeedingScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var protector = scope.ServiceProvider.GetRequiredService<IAllocationProtector>();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

        if (await db.ClinicalSubjects.AnyAsync(s => s.SubjectCode == new SubjectCode(SubjectBeta01), Ct))
        {
            return;
        }

        var study = new StudyCode(StudyBeta);
        var site = new SiteCode(Site02);
        var subject = ClinicalSubject.Create(
            new SubjectCode(SubjectBeta01),
            study,
            site,
            enrollmentIndex: 0,
            encryptedDemographics: "beta-seed-ciphertext",
            stratFactors: null);
        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();

        var scheme = RandomizationScheme.Draft(
            study,
            version: 1,
            RandomizationMethod.Minimization,
            new Dictionary<string, int> { ["PLACEBO"] = 1, [SecretArm] = 1 },
            new Dictionary<string, IReadOnlyList<string>> { ["sex"] = ["F", "M"] },
            blockSizes: null,
            slotsPerStratum: 0,
            preferredArmProbability: 0.8,
            requiresKit: false,
            draftedBy: "stat-seed-a",
            reasonForChange: Reason,
            clock: clock).Value!;
        scheme.Approve("stat-seed-b", Reason, clock).IsSuccess.Should().BeTrue();

        var allocationId = Guid.NewGuid();
        var allocation = RandomizationAllocation.Record(
            allocationId,
            scheme,
            subject,
            "sex=F",
            new Dictionary<string, string> { ["sex"] = "F" },
            sequence: 1,
            armCiphertext: protector.Protect(SecretArm, RandomizationAllocation.ArmPurpose(allocationId)),
            listSlotId: null,
            kitNumber: "KIT-BETA-01",
            allocatedBy: "crc-seed",
            allocatedAt: clock.UtcNow,
            reasonForChange: Reason);
        subject.Randomize(allocationId, "KIT-BETA-01", new Dictionary<string, object?>()).IsSuccess.Should().BeTrue();

        var device = new DeviceInventoryItem(
            Guid.NewGuid(),
            StudyBeta,
            Site02,
            "BetaMonitor",
            "00850006789990",
            "LOT-BETA-01",
            serialNumber: DeviceSerialBeta);

        var labRange = new LabReferenceRange(
            Guid.NewGuid(),
            StudyBeta,
            LabRangeBeta,
            "Hemoglobin",
            "LOCAL",
            Site02,
            "g/dL",
            "g/dL",
            "ALL",
            18,
            99,
            12.0,
            16.0,
            7.0,
            20.0,
            "718-7",
            "svc:seed",
            Reason,
            FakeClock.DefaultStart.UtcDateTime);

        db.ClinicalSubjects.Add(subject);
        db.RandomizationSchemes.Add(scheme);
        db.RandomizationAllocations.Add(allocation);
        db.DeviceInventoryItems.Add(device);
        db.LabReferenceRanges.Add(labRange);
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedDualSiteListFixturesAsync()
    {
        using var warmClient = _factory.CreateSignedClient();
        using var scope = _factory.Services.CreateSeedingScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var now = FakeClock.DefaultStart;

        var study = new StudyCode(StudyBeta);
        var s1 = ClinicalSubject.Create(new SubjectCode("SUBJ-LIST-S1"), study, new SiteCode(Site01), 10, "c1", null);
        var s2 = ClinicalSubject.Create(new SubjectCode("SUBJ-LIST-S2"), study, new SiteCode(Site02), 11, "c2", null);

        db.ClinicalSubjects.AddRange(s1, s2);
        db.DeviceInventoryItems.AddRange(
            new DeviceInventoryItem(Guid.NewGuid(), StudyBeta, Site01, "Pump", "00850006780001", "LOT-S1", serialNumber: "SN-LIST-S1"),
            new DeviceInventoryItem(Guid.NewGuid(), StudyBeta, Site02, "Pump", "00850006780002", "LOT-S2", serialNumber: "SN-LIST-S2"));
        db.DeviceDeficiencies.AddRange(
            new DeviceDeficiency(Guid.NewGuid(), StudyBeta, Site01, "00850006780001", "LOT-S1", "DEF-S1", now, now),
            new DeviceDeficiency(Guid.NewGuid(), StudyBeta, Site02, "00850006780002", "LOT-S2", "DEF-S2", now, now));
        db.DeviceQuarantineRecords.AddRange(
            new DeviceQuarantineRecord(Guid.NewGuid(), StudyBeta, Site01, "QUAR-S1", now, serialNumber: "SN-LIST-S1"),
            new DeviceQuarantineRecord(Guid.NewGuid(), StudyBeta, Site02, "QUAR-S2", now, serialNumber: "SN-LIST-S2"));
        db.LabReferenceRanges.AddRange(
            new LabReferenceRange(Guid.NewGuid(), StudyBeta, "ALT", "ALT", "LOCAL", Site01, "U/L", "U/L", "ALL", 18, 99, 7, 56, null, null, null, "svc:seed", Reason, now.UtcDateTime),
            new LabReferenceRange(Guid.NewGuid(), StudyBeta, "AST", "AST", "LOCAL", Site02, "U/L", "U/L", "ALL", 18, 99, 8, 48, null, null, null, "svc:seed", Reason, now.UtcDateTime));
        db.StagedAdverseEventReviews.AddRange(
            StagedAdverseEventReview.Create(study, new SubjectCode("SUBJ-LIST-S1"), new TestCode("ALT"), "ALT", 240, "U/L", 3, "ALT increased", "Grade 3", false, false, null, siteId: new SiteCode(Site01)),
            StagedAdverseEventReview.Create(study, new SubjectCode("SUBJ-LIST-S2"), new TestCode("AST"), "AST", 260, "U/L", 3, "AST increased", "Grade 3", false, false, null, siteId: new SiteCode(Site02)));

        await db.SaveChangesAsync(Ct);
    }

    private async Task<RemainingFixtures> SeedRemainingEndpointFixturesAsync()
    {
        using var warmClient = _factory.CreateSignedClient();
        using var scope = _factory.Services.CreateSeedingScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

        var study = new StudyCode(StudyBeta);
        const string enrolledCode = "SUBJ-BETA-ENR-02";
        var enrolled = ClinicalSubject.Create(new SubjectCode(enrolledCode), study, new SiteCode(Site02), 20, "c-enr", null);
        enrolled.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();

        var draftSchemeResult = RandomizationScheme.Draft(
            study,
            version: 2,
            RandomizationMethod.Minimization,
            new Dictionary<string, int> { ["A"] = 1, ["B"] = 1 },
            new Dictionary<string, IReadOnlyList<string>> { ["sex"] = ["F", "M"] },
            null,
            0,
            0.8,
            false,
            "stat-author",
            Reason,
            clock);
        draftSchemeResult.IsSuccess.Should().BeTrue();
        var draftScheme = draftSchemeResult.Value!;

        var coc = new CgtChainOfCustody(Guid.NewGuid(), StudyBeta, Site02, enrolledCode, "DIN-BETA-02", Site02, clock.UtcNow);
        const string dossierId = "DOSSIER-BETA-01";
        var dossier = CecDossier.Initialize(dossierId, "EVT-BETA-01", StudyBeta, enrolledCode, "MASK-SUBJ-01", "MACE", "MI", "2026-01-01", Site02, "MASK-SITE-02");
        var review = StagedAdverseEventReview.Create(study, new SubjectCode(enrolledCode), new TestCode("ALT"), "ALT", 300, "U/L", 3, "ALT increased", "Grade 3", false, false, null, siteId: new SiteCode(Site02));
        var enterpriseRule = new SynonymRule(
            Guid.NewGuid(),
            "ENTERPRISE",
            null,
            "HEADACHE",
            "MEDDRA",
            "26.0",
            "10019211",
            "Headache",
            "PT",
            Guid.NewGuid(),
            "svc:seed",
            Reason,
            FakeClock.DefaultStart.UtcDateTime);

        db.ClinicalSubjects.Add(enrolled);
        db.RandomizationSchemes.Add(draftScheme);
        db.CGTChainsOfCustody.Add(coc);
        db.CecDossiers.Add(dossier);
        db.StagedAdverseEventReviews.Add(review);
        db.SynonymRules.Add(enterpriseRule);
        await db.SaveChangesAsync(Ct);

        return new RemainingFixtures(draftScheme.Id, enrolledCode, coc.Id, dossierId, review.Id, enterpriseRule.Id);
    }

    private sealed record RemainingFixtures(
        Guid DraftSchemeId,
        string EnrolledSubjectCode,
        Guid CocId,
        string DossierId,
        string ToxicityReviewId,
        Guid EnterpriseRuleId);

    private sealed class SpyAllocationProtector : IAllocationProtector
    {
        private IAllocationProtector? _inner;
        private int _revealCount;

        public int RevealCount => Volatile.Read(ref _revealCount);

        public void Attach(IAllocationProtector inner) => _inner = inner;

        public void ResetRevealCount() => Interlocked.Exchange(ref _revealCount, 0);

        public string Protect(string arm, string purpose) =>
            (_inner ?? throw new InvalidOperationException("Inner protector not attached.")).Protect(arm, purpose);

        public IReadOnlyList<string> ProtectBatch(IReadOnlyList<(string Arm, string Purpose)> items) =>
            (_inner ?? throw new InvalidOperationException("Inner protector not attached.")).ProtectBatch(items);

        public string Reveal(string ciphertext, string purpose)
        {
            Interlocked.Increment(ref _revealCount);
            return (_inner ?? throw new InvalidOperationException("Inner protector not attached.")).Reveal(ciphertext, purpose);
        }
    }
}
