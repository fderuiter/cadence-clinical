using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Rules.Packs;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Kernel.Protocol;
using Cadence.TestSupport.Kernel.Rules;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Kernel.Api;

/// <summary>
/// HTTP endpoint and command-pipeline tests for <see cref="KernelProtocolGovernanceEndpoints"/> hosted in the real
/// <c>Cadence.Kernel.Host.Program</c> over the real <see cref="KernelCommandPipeline"/>, <see cref="AuthorityEvaluator"/>,
/// <see cref="SigTokenVerifier"/>, and domain aggregates (<see cref="ProtocolVersion"/>, <see cref="Attestation"/>,
/// <see cref="Artifact"/>, <see cref="Effectivity"/>, <see cref="Site"/>, <see cref="StudySite"/>, and
/// <see cref="CollectionInstance"/>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelProtocolGovernanceEndpointsTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly GatewayIdentity DesignerIdentity = TestSecurityContext.Identity("designer.lead", "Sponsor Designer");
    private static readonly GatewayIdentity CoordinatorIdentity = TestSecurityContext.Identity("crc.site101", "CRC");

    private const string ApprovalHash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly FakeClock _clock = new(T0);
    private readonly ReplayCache _sigTokenUseStore;
    private readonly InMemoryGovernanceStore _store;
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public KernelProtocolGovernanceEndpointsTests()
    {
        _sigTokenUseStore = new ReplayCache(_clock);
        _store = new InMemoryGovernanceStore(Tenant, _clock);

        _store.GrantRole(
            Actor.Person(DesignerIdentity.UserId),
            "SPONSOR_DESIGNER",
            [
                "organization.register",
                "study.register",
                "site.register",
                "study_site.link",
                "study_site.activate",
                "concept.define",
                "protocol.draft",
                "protocol.amend",
                "protocol.revise_draft",
                "protocol.submit_for_review",
                "protocol.return_to_draft",
                "protocol.approve",
                "protocol.withdraw",
                "simulation.run",
                "simulation.save_report",
                "artifact.register",
                "artifact.add_version",
                "effectivity.declare",
                "effectivity.change_status",
                "attestation.sign",
                "participant.register",
                "participant.screen",
                "collection.open",
            ],
            AuthorityScope.Tenant());

        // Coordinator holds attestation.sign + collection.open on the study, but lacks protocol.approve.
        _store.GrantRole(
            Actor.Person(CoordinatorIdentity.UserId),
            "SITE_CRC",
            ["attestation.sign", "participant.register", "participant.screen", "collection.open"],
            AuthorityScope.Study(_store.StudyId));

        // Execution forms service account holds participant.register + execution.form.open.
        _store.GrantRole(
            Actor.ServiceAccount("execution-forms"),
            "TRIAL_EXECUTION_FORMS",
            ["execution.form.open", "participant.register"],
            AuthorityScope.Tenant());
    }

    public void Dispose() => _host?.Dispose();

    [Fact]
    public async Task ApproveProtocolVersion_EnforcesStepUpSigToken_Headers_Authorization_ContentHashBinding_AndSingleUse()
    {
        var ct = TestContext.Current.CancellationToken;
        var versionId = _store.SeedDraftProtocolVersion(ProtocolDesignSamples.Minimal);
        var versionRef = NodeRef.For(versionId).ToString();
        var approvePath = $"/api/v1/kernel/protocol-versions/{versionRef}/approve";

        using var designer = CreateClient(DesignerIdentity);
        using var crc = CreateClient(CoordinatorIdentity);

        // 1. Submit for review requires Idempotency-Key and X-Change-Reason.
        using (var noIdem = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review", idempotencyKey: null, reason: "Submit", sigToken: null), ct))
        {
            noIdem.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noIdem.Content.ReadAsStringAsync(ct)).Should().Contain(KernelErrors.IdempotencyKeyRequired.Code);
        }

        using (var noReason = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review", idempotencyKey: "gov-no-reason", reason: null, sigToken: null), ct))
        {
            noReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noReason.Content.ReadAsStringAsync(ct)).Should().Contain(KernelErrors.ReasonForChangeRequired.Code);
        }

        // 2. Submit Draft -> InReview to freeze ContentHash.
        string frozenHash;
        using (var submitRes = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review", "gov-submit-1", "Freeze draft v1 for QA review", null), ct))
        {
            submitRes.StatusCode.Should().Be(HttpStatusCode.OK, await submitRes.Content.ReadAsStringAsync(ct));
            var json = JsonDocument.Parse(await submitRes.Content.ReadAsStringAsync(ct)).RootElement;
            json.GetProperty("status").GetString().Should().Be("InReview");
            frozenHash = json.GetProperty("content_hash").GetString()!;
            frozenHash.Should().StartWith("sha256:");
        }

        // 3. Missing, expired (>60s), wrong-action, wrong-semantic-action, and wrong-signer X-Sig-Token all fail closed with 401.
        var now = _clock.UtcNow;
        var unusableTokens = new (string Name, string? Token)[]
        {
            ("missing", null),
            ("expired (>60s)", TestSecurityContext.CreateSigToken(
                DesignerIdentity.UserId,
                approvePath,
                KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
                issuedAt: now.AddSeconds(-65))),
            ("wrong action path", TestSecurityContext.CreateSigToken(
                DesignerIdentity.UserId,
                $"/api/v1/kernel/protocol-versions/{NodeRef.For(ProtocolVersionId.New())}/approve",
                KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
                issuedAt: now)),
            ("wrong semantic action", TestSecurityContext.CreateSigToken(
                DesignerIdentity.UserId,
                approvePath,
                KernelLockEndpoints.ApplyLockSemanticAction,
                issuedAt: now)),
            ("wrong signer", TestSecurityContext.CreateSigToken(
                CoordinatorIdentity.UserId,
                approvePath,
                KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
                issuedAt: now)),
        };

        foreach (var (name, badToken) in unusableTokens)
        {
            using var res = await designer.SendAsync(
                BuildPost(approvePath, $"gov-bad-sig-{name}", "Approve protocol v1", badToken, new { meaning = "Approved" }),
                ct);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, name);
        }

        // 4. Coordinator holds attestation.sign but lacks protocol.approve -> 403 and zero orphan Attestations!
        var crcToken = TestSecurityContext.CreateSigToken(
            CoordinatorIdentity.UserId,
            approvePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var forbidden = await crc.SendAsync(
            BuildPost(approvePath, "gov-crc-denied", "Attempt unauthorized approval", crcToken, new { meaning = "Approved" }),
            ct))
        {
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await forbidden.Content.ReadAsStringAsync(ct)).Should().Contain(KernelErrors.AuthorizationDenied.Code);
        }

        _store.ReadAll<Attestation>().Should().BeEmpty("a denied approval must not write an orphan Attestation");

        // 5. ExpectedContentHash mismatch -> 409 K.Trust.TargetContentChanged without recording Attestation.
        var mismatchToken = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            approvePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var mismatch = await designer.SendAsync(
            BuildPost(approvePath, "gov-hash-mismatch", "Approve stale hash", mismatchToken, new
            {
                meaning = "Approved",
                expected_content_hash = ApprovalHash,
            }),
            ct))
        {
            mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await mismatch.Content.ReadAsStringAsync(ct)).Should().Contain(TrustErrors.TargetContentChanged.Code);
        }

        _store.ReadAll<Attestation>().Should().BeEmpty("a hash mismatch must refuse before recording Attestation");

        // 6. Valid single-use 60s X-Sig-Token -> records Attest(Approved, frozenHash) + transitions to Approved; replay -> 401!
        var validSigToken = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            approvePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var approvedRes = await designer.SendAsync(
            BuildPost(approvePath, "gov-approve-ok", "Approve frozen protocol v1 under 21 CFR Part 11", validSigToken, new
            {
                meaning = "Approved",
                expected_content_hash = frozenHash,
            }),
            ct))
        {
            approvedRes.StatusCode.Should().Be(HttpStatusCode.OK, await approvedRes.Content.ReadAsStringAsync(ct));
            var body = JsonDocument.Parse(await approvedRes.Content.ReadAsStringAsync(ct)).RootElement;
            body.GetProperty("status").GetString().Should().Be("Approved");
            body.GetProperty("approval_attestation").GetString().Should().StartWith("attestation:");
        }

        var attestation = _store.ReadAll<Attestation>().Should().ContainSingle().Subject;
        attestation.Meaning.Should().Be(AttestationMeaning.Approved);
        attestation.Target.Kind.Should().Be(AttestationTarget.ProtocolVersionKind);
        attestation.Target.Id.Should().Be(versionId.Value);
        attestation.Target.ContentHash.Should().Be(frozenHash);

        // Replaying the consumed single-use token is rejected with 401.
        using var replayRes = await designer.SendAsync(
            BuildPost(approvePath, "gov-approve-replay", "Replay consumed token", validSigToken, new { meaning = "Approved" }),
            ct);
        replayRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EndToEndGovernanceLifecycle_ReviewReturnApproveArtifactEffectivityAndSiteActivation_UnblocksCollectionOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        var sbpConcept = _store.SeedSbpConcept();
        var design = new ProtocolDesign
        {
            Title = "CADENCE-101 Vitals Protocol",
            Epochs = [new Epoch("ep.screening", "Screening", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    "visit.screening",
                    "Screening Visit",
                    "ep.screening",
                    [ActivityLocation.Site],
                    ["cs.vitals"],
                    new ActivityTiming(1, 0, 0)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [new CollectionItem("item.sbp", sbpConcept, "mm[Hg]", Required: true)]),
            ],
        };

        var versionId = _store.SeedDraftProtocolVersion(ProtocolDesignDocument.Write(design));
        var studyRef = NodeRef.For(_store.StudyId).ToString();
        var versionRef = NodeRef.For(versionId).ToString();
        var approvePath = $"/api/v1/kernel/protocol-versions/{versionRef}/approve";

        var factory = CreateFactory();
        using var designer = factory.CreateSignedClient(DesignerIdentity);

        // 1. Draft -> SubmitForReview (InReview) -> ReturnToDraft (Draft) -> SubmitForReview (InReview).
        using (var review1 = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review", "e2e-review-1", "Initial review freeze", null), ct))
        {
            review1.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonDocument.Parse(await review1.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("status").GetString().Should().Be("InReview");
        }

        using (var backToDraft = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/return-to-draft", "e2e-return-1", "Return to draft for schedule check", null), ct))
        {
            backToDraft.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = JsonDocument.Parse(await backToDraft.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("Draft");
            doc.GetProperty("content_hash").ValueKind.Should().Be(JsonValueKind.Null);
        }

        string frozenHash;
        using (var review2 = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review", "e2e-review-2", "Re-submit frozen protocol v1", null), ct))
        {
            review2.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = JsonDocument.Parse(await review2.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("InReview");
            frozenHash = doc.GetProperty("content_hash").GetString()!;
        }

        // 2. Signed Part 11 Approval -> Approved.
        var sigToken = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            approvePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var approveRes = await designer.SendAsync(
            BuildPost(approvePath, "e2e-approve-1", "Approve protocol v1", sigToken, new { meaning = "Approved", expected_content_hash = frozenHash }),
            ct))
        {
            approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync(ct));
            JsonDocument.Parse(await approveRes.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("status").GetString().Should().Be("Approved");
        }

        // 3. Link study site SITE-101 -> Selected; activating BEFORE an Effective protocol version fails with K.Effectivity.NoEffectiveVersion (K-TIME-4).
        string studySiteRef;
        Guid studySiteGuid;
        using (var linkSite = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/studies/{studyRef}/sites", "e2e-link-site-101", "Link SITE-101 to study", null, new
            {
                site_code = "SITE-101",
                site_name = "Riverside University Hospital",
                country = "US",
            }),
            ct))
        {
            linkSite.StatusCode.Should().Be(HttpStatusCode.Created, await linkSite.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await linkSite.Content.ReadAsStringAsync(ct)).RootElement;
            studySiteRef = doc.GetProperty("study_site_ref").GetString()!;
            studySiteGuid = doc.GetProperty("study_site_id").GetGuid();
            doc.GetProperty("status").GetString().Should().Be("Selected");
        }

        using (var prematureActivate = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/study-sites/{studySiteRef}/activate", "e2e-premature-activate", "Attempt site activation before effectivity", null),
            ct))
        {
            prematureActivate.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await prematureActivate.Content.ReadAsStringAsync(ct)).Should().Contain(EffectivityErrors.NoEffectiveVersion.Code);
        }

        // 4. Declare study-wide Pending effectivity; transitioning to Effective WITHOUT approval evidence is refused (K-TIME-5).
        string effectivityRef;
        using (var declareRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{versionRef}/effectivities", "e2e-declare-eff", "Declare study-wide effectivity", null, new
            {
                scope_level = "Study",
            }),
            ct))
        {
            declareRes.StatusCode.Should().Be(HttpStatusCode.Created, await declareRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await declareRes.Content.ReadAsStringAsync(ct)).RootElement;
            effectivityRef = doc.GetProperty("effectivity_ref").GetString()!;
            doc.GetProperty("status").GetString().Should().Be("Pending");
        }

        _clock.Advance(TimeSpan.FromSeconds(10));

        using (var noEvidenceRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/effectivities/{effectivityRef}/status", "e2e-eff-no-evidence", "Attempt go-live without IRB evidence", null, new
            {
                status = "Effective",
                evidence_artifact_version_refs = Array.Empty<string>(),
            }),
            ct))
        {
            noEvidenceRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noEvidenceRes.Content.ReadAsStringAsync(ct)).Should().Contain(EffectivityErrors.EvidenceRequired.Code);
        }

        // 5. Register EthicsApproval artifact, transition effectivity to Effective, and activate SITE-101!
        string artifactVersionRef;
        using (var artRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/studies/{studyRef}/artifacts", "e2e-register-irb", "Register central IRB approval letter", null, new
            {
                kind = "EthicsApproval",
                title = "Central IRB Approval v1.0",
                content_hash = ApprovalHash,
                size_bytes = 2048,
                media_type = "application/pdf",
                storage_ref = "s3://cadence-etmf/irb-v1.pdf",
            }),
            ct))
        {
            artRes.StatusCode.Should().Be(HttpStatusCode.Created, await artRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await artRes.Content.ReadAsStringAsync(ct)).RootElement;
            artifactVersionRef = doc.GetProperty("artifact_version_ref").GetString()!;
        }

        var artifactsList = await designer.GetFromJsonAsync<JsonElement>($"/api/v1/kernel/studies/{studyRef}/artifacts", ct);
        artifactsList.GetArrayLength().Should().Be(1);

        _clock.Advance(TimeSpan.FromSeconds(10));

        using (var effectiveRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/effectivities/{effectivityRef}/status", "e2e-eff-go-live", "Go live study-wide with IRB approval", null, new
            {
                status = "Effective",
                evidence_artifact_version_refs = new[] { artifactVersionRef },
            }),
            ct))
        {
            effectiveRes.StatusCode.Should().Be(HttpStatusCode.OK, await effectiveRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await effectiveRes.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("Effective");
        }

        var effectivitiesList = await designer.GetFromJsonAsync<JsonElement>($"/api/v1/kernel/studies/{studyRef}/effectivities", ct);
        effectivitiesList.GetArrayLength().Should().Be(1);
        effectivitiesList[0].GetProperty("status").GetString().Should().Be("Effective");

        _clock.Advance(TimeSpan.FromSeconds(5));

        using (var activateRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/study-sites/{studySiteRef}/activate", "e2e-activate-site-101", "Activate SITE-101 with effective protocol v1", null),
            ct))
        {
            activateRes.StatusCode.Should().Be(HttpStatusCode.OK, await activateRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await activateRes.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("Active");
            doc.GetProperty("effective_versions").EnumerateArray().Should().ContainSingle()
                .Which.GetProperty("number").GetInt32().Should().Be(1);
        }

        var sitesList = await designer.GetFromJsonAsync<JsonElement>($"/api/v1/kernel/studies/{studyRef}/sites", ct);
        sitesList.GetArrayLength().Should().Be(1);
        sitesList[0].GetProperty("status").GetString().Should().Be("Active");

        // 6. Screen a participant as execution-forms (verifying participant.register) + designer participant.screen,
        //    then open a casebook form (OpenCollectionInstance) and confirm governing version 1 resolves without K.Collection.NoGoverningVersion!
        var participantId = ParticipantId.New();
        var enrollmentId = EnrollmentId.New();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = Actor.ServiceAccount("execution-forms");
            var svcSender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
            var regPart = await svcSender.SendAsync(
                new RegisterParticipant(Tenant, participantId, "e2e-reg-part", "Register participant on site assignment"),
                cancellationToken: ct);
            regPart.IsSuccess.Should().BeTrue(regPart.Error?.Message);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = Actor.Person(DesignerIdentity.UserId);
            var designerSender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
            var screened = await designerSender.SendAsync(
                new ScreenParticipant(
                    Tenant,
                    enrollmentId,
                    participantId,
                    StudySiteId.FromGuid(studySiteGuid),
                    SubjectCode.From("SUBJ-101-001"),
                    _clock.UtcNow,
                    "e2e-screen-part",
                    "Screen participant at active SITE-101"),
                cancellationToken: ct);
            screened.IsSuccess.Should().BeTrue(screened.Error?.Message);

            _clock.Advance(TimeSpan.FromSeconds(5));

            var opened = await designerSender.SendAsync(
                new OpenCollectionInstance(
                    Tenant,
                    CollectionInstanceId.New(),
                    enrollmentId,
                    "cs.vitals",
                    "visit.screening",
                    _clock.UtcNow,
                    "e2e-open-vitals",
                    "Open Vital Signs casebook form"),
                cancellationToken: ct);
            opened.IsSuccess.Should().BeTrue($"expected OpenCollectionInstance to succeed instead of K.Collection.NoGoverningVersion, got: {opened.Error}");
            opened.Value!.ProtocolVersionId.Should().Be(versionId);
        }
    }

    [Fact]
    [KernelRule("K-PROT-2")]
    [KernelRule("K-SIM-5")]
    public async Task ForkedAmendmentGovernance_EnforcesUndeclaredElementRefusal_MultiKindSimulation_AndAttestedSimulationReportGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var sbpConcept = _store.SeedSbpConcept();
        var baseDesign = new ProtocolDesign
        {
            Title = "CADENCE-101 Vitals Protocol",
            Epochs = [new Epoch("ep.screening", "Screening", 1), new Epoch("ep.treatment", "Treatment", 2)],
            PlannedActivities =
            [
                new PlannedActivity(
                    "visit.screening",
                    "Screening Visit",
                    "ep.screening",
                    [ActivityLocation.Site],
                    ["cs.vitals"],
                    new ActivityTiming(1, 0, 0)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [new CollectionItem("item.sbp", sbpConcept, "mm[Hg]", Required: true)]),
            ],
        };

        var v1Id = _store.SeedDraftProtocolVersion(ProtocolDesignDocument.Write(baseDesign));
        var studyRef = NodeRef.For(_store.StudyId).ToString();
        var v1Ref = NodeRef.For(v1Id).ToString();
        var v1ApprovePath = $"/api/v1/kernel/protocol-versions/{v1Ref}/approve";

        var factory = CreateFactory();
        using var designer = factory.CreateSignedClient(DesignerIdentity);

        // 1. First version approves with AttestationMeaning.Approved alone (no SimulationReport required for v1).
        string v1Hash;
        using (var submitV1 = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/submit-for-review", "fork-v1-submit", "Freeze protocol v1", null), ct))
        {
            submitV1.StatusCode.Should().Be(HttpStatusCode.OK);
            v1Hash = JsonDocument.Parse(await submitV1.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        var v1Sig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v1ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var approveV1 = await designer.SendAsync(
            BuildPost(v1ApprovePath, "fork-v1-approve", "Approve v1", v1Sig, new { meaning = "Approved", expected_content_hash = v1Hash }),
            ct))
        {
            approveV1.StatusCode.Should().Be(HttpStatusCode.OK, await approveV1.Content.ReadAsStringAsync(ct));
        }

        // 2. Fork v2 adding visit.week4 + eligibility.age18 while omitting them from ImpactedElements -> SubmitForReview refuses with 422!
        var amendedDesign = baseDesign with
        {
            PlannedActivities =
            [
                .. baseDesign.PlannedActivities,
                new PlannedActivity(
                    "visit.week4",
                    "Week 4 Visit",
                    "ep.treatment",
                    [ActivityLocation.Site],
                    ["cs.vitals"],
                    new ActivityTiming(28, 2, 2)),
            ],
            EligibilityCriteria =
            [
                new EligibilityCriterion("eligibility.age18", CriterionCategory.Inclusion, "Participant is at least 18 years old.", []),
            ],
        };
        var v2Id = ProtocolVersionId.New();
        using (var forkRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/fork", "fork-v2-create", "Fork v2 from v1", null, new
            {
                intent = "Add Week 4 visit",
                rationale = "Follow-up blood pressure check",
                classification = "Substantial",
                summary_of_changes = "Adds Week 4 and age criterion",
                impacted_elements = new[] { "cs.vitals" },
                transition_policy = "RequiresReconsent",
                protocol_version_id = v2Id.Value,
            }),
            ct))
        {
            forkRes.StatusCode.Should().Be(HttpStatusCode.Created, await forkRes.Content.ReadAsStringAsync(ct));
            var forkDoc = JsonDocument.Parse(await forkRes.Content.ReadAsStringAsync(ct)).RootElement;
            forkDoc.GetProperty("number").GetInt32().Should().Be(2);
            forkDoc.GetProperty("status").GetString().Should().Be("Draft");
            forkDoc.GetProperty("parent_version").GetString().Should().Be(v1Ref);
            forkDoc.GetProperty("amendment_intent").GetString().Should().Be("Add Week 4 visit");
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = Actor.Person(DesignerIdentity.UserId);
            var sender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
            var incompleteAmendment = new ProtocolAmendment(
                "Add Week 4 visit",
                "Follow-up blood pressure check",
                AmendmentClassification.Substantial,
                "Adds Week 4 and age criterion",
                ["cs.vitals"],
                TransitionPolicy.RequiresReconsent);
            var revised = await sender.SendAsync(
                new ReviseProtocolDraft(Tenant, v2Id, ProtocolDesignDocument.Write(amendedDesign), incompleteAmendment, "fork-v2-revise-incomplete", "Add Week 4 and eligibility"),
                cancellationToken: ct);
            revised.IsSuccess.Should().BeTrue(revised.Error?.Message);
        }

        var v2Ref = NodeRef.For(v2Id).ToString();
        using (var refusedSubmit = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v2Ref}/submit-for-review", "fork-v2-submit-refused", "Submit incomplete amendment", null),
            ct))
        {
            refusedSubmit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var problem = JsonDocument.Parse(await refusedSubmit.Content.ReadAsStringAsync(ct)).RootElement;
            problem.GetProperty("code").GetString().Should().Be(ProtocolErrors.UndeclaredImpactedElements.Code);
            var undeclared = problem.GetProperty("errors").GetProperty("impactedElements").EnumerateArray().Select(e => e.GetString()).ToList();
            undeclared.Should().BeEquivalentTo("eligibility.age18", "visit.week4");
            problem.GetProperty("explanation").GetProperty("producer").GetString().Should().Be("kernel.protocol.amendment_scope");
            problem.GetProperty("explanation").GetProperty("conditions")[0].GetProperty("code").GetString().Should().Be("protocol.amendment.impacted_elements");
        }

        // 3. Declare all changed elements and submit v2 -> InReview.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = Actor.Person(DesignerIdentity.UserId);
            var sender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
            var completeAmendment = new ProtocolAmendment(
                "Add Week 4 visit",
                "Follow-up blood pressure check",
                AmendmentClassification.Substantial,
                "Adds Week 4 and age criterion",
                ["cs.vitals", "visit.week4", "eligibility.age18"],
                TransitionPolicy.RequiresReconsent);
            var fixedDraft = await sender.SendAsync(
                new ReviseProtocolDraft(Tenant, v2Id, ProtocolDesignDocument.Write(amendedDesign), completeAmendment, "fork-v2-revise-complete", "Declare all impacted elements"),
                cancellationToken: ct);
            fixedDraft.IsSuccess.Should().BeTrue(fixedDraft.Error?.Message);
        }

        string v2Hash;
        using (var submitV2 = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v2Ref}/submit-for-review", "fork-v2-submit-ok", "Freeze protocol v2", null),
            ct))
        {
            submitV2.StatusCode.Should().Be(HttpStatusCode.OK, await submitV2.Content.ReadAsStringAsync(ct));
            v2Hash = JsonDocument.Parse(await submitV2.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        // 4. Multi-kind HTTP simulation works across ProtocolVersion, MappingSpecification, and RulePack.
        var httpJsonOptions = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        KernelSimulationResponse protocolSimulation;
        using (var simProtRes = await designer.PostAsJsonAsync(
            "/api/v1/kernel/simulation/simulate",
            new { stored_version_ref = v2Ref, valid_at = T0, known_at = T0 },
            ct))
        {
            simProtRes.StatusCode.Should().Be(HttpStatusCode.OK, await simProtRes.Content.ReadAsStringAsync(ct));
            protocolSimulation = (await simProtRes.Content.ReadFromJsonAsync<KernelSimulationResponse>(httpJsonOptions, ct))!;
            protocolSimulation.Candidate.Kind.Should().Be("ProtocolVersion");
            protocolSimulation.ScheduleChanges.Should().ContainSingle(c => c.ActivityId == "visit.week4");
        }

        var mapSpecJson = MappingSpecificationJson.ToJson(
            SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbpConcept))).GetRawText();
        using (var simMapRes = await designer.PostAsJsonAsync(
            "/api/v1/kernel/simulation/simulate",
            new { kind = "MappingSpecification", study_ref = studyRef, candidate_content = mapSpecJson, valid_at = T0, known_at = T0 },
            ct))
        {
            simMapRes.StatusCode.Should().Be(HttpStatusCode.OK, await simMapRes.Content.ReadAsStringAsync(ct));
            var mapSim = (await simMapRes.Content.ReadFromJsonAsync<KernelSimulationResponse>(httpJsonOptions, ct))!;
            mapSim.Candidate.Kind.Should().Be("MappingSpecification");
        }

        var rulePackJson = RulePackJson.ToJson(SampleRulePacks.Sample()).GetRawText();
        using (var simRuleRes = await designer.PostAsJsonAsync(
            "/api/v1/kernel/simulation/simulate",
            new { kind = "RulePack", study_ref = studyRef, candidate_content = rulePackJson, valid_at = T0, known_at = T0 },
            ct))
        {
            simRuleRes.StatusCode.Should().Be(HttpStatusCode.OK, await simRuleRes.Content.ReadAsStringAsync(ct));
            var ruleSim = (await simRuleRes.Content.ReadFromJsonAsync<KernelSimulationResponse>(httpJsonOptions, ct))!;
            ruleSim.Candidate.Kind.Should().Be("RulePack");
        }

        // 5. Read-only approval-readiness precheck reports unmet simulation conditions BEFORE step-up auth so no X-Sig-Token is burned.
        using (var precheckBeforeReport = await designer.GetAsync($"/api/v1/kernel/protocol-versions/{v2Ref}/approval-readiness", ct))
        {
            precheckBeforeReport.StatusCode.Should().Be(HttpStatusCode.OK, await precheckBeforeReport.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await precheckBeforeReport.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("is_amendment").GetBoolean().Should().BeTrue();
            doc.GetProperty("ready").GetBoolean().Should().BeFalse();
            doc.GetProperty("simulation_report_saved").GetBoolean().Should().BeFalse();
            doc.GetProperty("review_signed").GetBoolean().Should().BeFalse();
            doc.GetProperty("review_attestation_ref").ValueKind.Should().Be(JsonValueKind.Null);
            doc.GetProperty("blocking_code").GetString().Should().Be(ProtocolErrors.SimulationReviewRequired.Code);
            doc.GetProperty("explanation").GetProperty("producer").GetString().Should().Be("kernel.protocol.simulation_gate");
            doc.GetProperty("explanation").GetProperty("conditions")
                .EnumerateArray()
                .Should()
                .Contain(c => c.GetProperty("code").GetString() == "protocol.amendment.simulation_saved" && !c.GetProperty("met").GetBoolean());
        }

        // Approving v2 without an attested SimulationReport still fails with 409 K.Protocol.SimulationReviewRequired and writes zero Approved attestations for v2!
        var v2ApprovePath = $"/api/v1/kernel/protocol-versions/{v2Ref}/approve";
        var v2SigMissingReport = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v2ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var missingReportRes = await designer.SendAsync(
            BuildPost(v2ApprovePath, "fork-v2-approve-no-report", "Approve v2 without simulation report", v2SigMissingReport, new
            {
                meaning = "Approved",
                expected_content_hash = v2Hash,
            }),
            ct))
        {
            missingReportRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await missingReportRes.Content.ReadAsStringAsync(ct)).Should().Contain(ProtocolErrors.SimulationReviewRequired.Code);
        }

        _store.ReadAll<Attestation>().Count(a => a.Target.Id == v2Id.Value).Should().Be(0, "gate checks before recording orphan Approved attestation");

        // 6. Saving a masked SimulationResult is refused (K-SIM-5); saving the unmasked SimulationResult registers a SimulationReport artifact.
        var maskedSimulation = protocolSimulation with
        {
            Masked = [new KernelMaskedPartResponse(SimulationSnapshotMasking.SitesPart, MaskReason.NotPermitted.ToString())],
        };
        using (var saveMaskedRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/studies/{studyRef}/simulation-reports", "fork-v2-save-masked", "Attempt to save masked simulation", null, new
            {
                title = "Masked report",
                result = maskedSimulation,
            }, httpJsonOptions),
            ct))
        {
            saveMaskedRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await saveMaskedRes.Content.ReadAsStringAsync(ct)).Should().Contain(EvidenceErrors.SimulationReportInvalid.Code);
        }

        string reportArtifactVersionRef;
        string reportContentHash;
        using (var saveReportRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/studies/{studyRef}/simulation-reports", "fork-v2-save-report", "Save unmasked simulation report for v2", null, new
            {
                title = "Protocol v2 Simulation Impact Report",
                result = protocolSimulation,
            }, httpJsonOptions),
            ct))
        {
            saveReportRes.StatusCode.Should().Be(HttpStatusCode.Created, await saveReportRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await saveReportRes.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("kind").GetString().Should().Be("SimulationReport");
            reportArtifactVersionRef = doc.GetProperty("artifact_version_ref").GetString()!;
            reportContentHash = doc.GetProperty("content_hash").GetString()!;
        }

        using (var precheckUnreviewed = await designer.GetAsync($"/api/v1/kernel/protocol-versions/{v2Ref}/approval-readiness", ct))
        {
            precheckUnreviewed.StatusCode.Should().Be(HttpStatusCode.OK, await precheckUnreviewed.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await precheckUnreviewed.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("ready").GetBoolean().Should().BeFalse();
            doc.GetProperty("simulation_report_saved").GetBoolean().Should().BeTrue();
            doc.GetProperty("review_signed").GetBoolean().Should().BeFalse();
            doc.GetProperty("simulation_report_artifact_version_ref").GetString().Should().Be(reportArtifactVersionRef);
            doc.GetProperty("review_attestation_ref").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 7. Approving before signing Reviewed on the SimulationReport still refuses with 409; once Reviewed is signed via HTTP, Approve succeeds!
        var v2SigUnreviewed = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v2ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var unreviewedRes = await designer.SendAsync(
            BuildPost(v2ApprovePath, "fork-v2-approve-unreviewed", "Approve v2 with unsigned report", v2SigUnreviewed, new
            {
                meaning = "Approved",
                expected_content_hash = v2Hash,
            }),
            ct))
        {
            unreviewedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await unreviewedRes.Content.ReadAsStringAsync(ct)).Should().Contain(ProtocolErrors.SimulationReviewRequired.Code);
        }

        var reviewPath = KernelSimulationEndpoints.ReviewArtifactVersionAction(reportArtifactVersionRef);
        var reviewSigWrongHash = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            reviewPath,
            KernelSimulationEndpoints.ReviewArtifactVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var wrongHashReviewRes = await designer.SendAsync(
            BuildPost(reviewPath, "fork-v2-review-wrong-hash", "Sign review with stale hash", reviewSigWrongHash, new
            {
                meaning = "Reviewed",
                expected_content_hash = ApprovalHash,
            }),
            ct))
        {
            wrongHashReviewRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await wrongHashReviewRes.Content.ReadAsStringAsync(ct)).Should().Contain(TrustErrors.TargetContentChanged.Code);
        }

        var reviewSig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            reviewPath,
            KernelSimulationEndpoints.ReviewArtifactVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        string reviewAttestationRef;
        using (var reviewRes = await designer.SendAsync(
            BuildPost(reviewPath, "fork-v2-review-attest", "Sign Reviewed attestation on v2 SimulationReport", reviewSig, new
            {
                meaning = "Reviewed",
                expected_content_hash = reportContentHash,
            }),
            ct))
        {
            reviewRes.StatusCode.Should().Be(HttpStatusCode.OK, await reviewRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await reviewRes.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("meaning").GetString().Should().Be("Reviewed");
            doc.GetProperty("artifact_version_ref").GetString().Should().Be(reportArtifactVersionRef);
            doc.GetProperty("content_hash").GetString().Should().Be(reportContentHash);
            reviewAttestationRef = doc.GetProperty("attestation_ref").GetString()!;
        }

        using (var replayReviewRes = await designer.SendAsync(
            BuildPost(reviewPath, "fork-v2-review-replay", "Replay single-use review sig token", reviewSig, new
            {
                meaning = "Reviewed",
                expected_content_hash = reportContentHash,
            }),
            ct))
        {
            replayReviewRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var precheckReady = await designer.GetAsync($"/api/v1/kernel/protocol-versions/{v2Ref}/approval-readiness", ct))
        {
            precheckReady.StatusCode.Should().Be(HttpStatusCode.OK, await precheckReady.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await precheckReady.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("ready").GetBoolean().Should().BeTrue();
            doc.GetProperty("simulation_report_saved").GetBoolean().Should().BeTrue();
            doc.GetProperty("review_signed").GetBoolean().Should().BeTrue();
            doc.GetProperty("review_attestation_ref").GetString().Should().Be(reviewAttestationRef);
            doc.GetProperty("blocking_code").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var v2SigWrongRef = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v2ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var wrongRefRes = await designer.SendAsync(
            BuildPost(v2ApprovePath, "fork-v2-approve-wrong-ref", "Approve v2 with unrelated review attestation ref", v2SigWrongRef, new
            {
                meaning = "Approved",
                expected_content_hash = v2Hash,
                review_attestation_ref = NodeRef.For(AttestationId.New()).ToString(),
            }),
            ct))
        {
            wrongRefRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await wrongRefRes.Content.ReadAsStringAsync(ct)).Should().Contain(ProtocolErrors.SimulationReviewRequired.Code);
        }

        var v2SigApproved = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v2ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var approvedV2Res = await designer.SendAsync(
            BuildPost(v2ApprovePath, "fork-v2-approve-ok", "Approve v2 with verified SimulationReport review signature", v2SigApproved, new
            {
                meaning = "Approved",
                expected_content_hash = v2Hash,
                review_attestation_ref = reviewAttestationRef,
            }),
            ct))
        {
            approvedV2Res.StatusCode.Should().Be(HttpStatusCode.OK, await approvedV2Res.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await approvedV2Res.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("Approved");
        }
    }

    [Fact]
    public async Task ApproveProtocolVersion_WhenCurrentActorAndSigTokenSubjectAreUnresolved_ReturnsActorUnresolved()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = CreateFactory();
        using var designer = factory.CreateSignedClient(DesignerIdentity);

        var v1Id = _store.SeedDraftProtocolVersion(ProtocolDesignSamples.Minimal);
        var v1Ref = NodeRef.For(v1Id).ToString();
        string v1Hash;
        using (var submitV1 = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/submit-for-review", "actor-unresolved-v1-submit", "Freeze protocol v1", null), ct))
        {
            submitV1.StatusCode.Should().Be(HttpStatusCode.OK);
            v1Hash = JsonDocument.Parse(await submitV1.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        int attestationsBefore = _store.ReadAll<Attestation>().Count;
        await using var scope = factory.Services.CreateAsyncScope();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentActor>(new NullCurrentActor());
        services.AddSingleton<IKernelUnitOfWorkFactory>(_store);
        services.AddSingleton<IAttestationVerifier>(_store);
        services.AddSingleton<ITenantEntitlementReader>(_store);
        services.AddSingleton<IClock>(_clock);
        await using var requestProvider = services.BuildServiceProvider();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = requestProvider,
        };
        KernelRequestTenant.Set(httpContext, Tenant);
        httpContext.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] =
            new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                claims: [new System.Security.Claims.Claim("sub", "   ")]);
        httpContext.Request.Headers["Idempotency-Key"] = "actor-unresolved-v1-approve";
        httpContext.Request.Headers[GatewayHeaders.ChangeReason] = "Attempt approval with unresolved actor";

        var response = await KernelProtocolGovernanceEndpoints.ApproveProtocolVersionAsync(
            v1Ref,
            new KernelApproveProtocolVersionRequest("Approved", v1Hash, null, null),
            httpContext,
            _store,
            scope.ServiceProvider.GetRequiredService<IKernelCommandSender>(),
            new PermittedAuthorizationProbe(),
            new FixedKernelChangeReason("Attempt approval with unresolved actor"),
            ct);

        var problem = response.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        problem.ProblemDetails.Extensions["code"].Should().Be(KernelErrors.ActorUnresolved.Code);
        _store.ReadAll<Attestation>().Should().HaveCount(attestationsBefore, "unresolved actor must fail closed before recording any attestation");
    }

    [Fact]
    public async Task WithdrawProtocolVersion_EnforcesStepUpSigToken_Authorization_AndWithdrawsInReviewAndApprovedVersions()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = CreateFactory();
        using var designer = factory.CreateSignedClient(DesignerIdentity);
        using var crc = factory.CreateSignedClient(CoordinatorIdentity);

        var v1Id = _store.SeedDraftProtocolVersion(ProtocolDesignSamples.Minimal);
        var v1Ref = NodeRef.For(v1Id).ToString();
        var v1WithdrawPath = KernelProtocolGovernanceEndpoints.WithdrawProtocolVersionAction(v1Ref);
        var v1ApprovePath = KernelProtocolGovernanceEndpoints.ApproveProtocolVersionAction(v1Ref);

        // 1. Missing X-Sig-Token on withdraw is rejected with 401.
        using (var noSigRes = await designer.SendAsync(
            BuildPost(v1WithdrawPath, "withdraw-v1-no-sig", "Missing sig token", sigToken: null, new { meaning = "Withdrawn" }),
            ct))
        {
            noSigRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 2. Withdrawing a Draft version fails with 409 InvalidTransition without recording an attestation.
        var draftWithdrawSig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v1WithdrawPath,
            KernelProtocolGovernanceEndpoints.WithdrawProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var draftWithdrawRes = await designer.SendAsync(
            BuildPost(v1WithdrawPath, "withdraw-v1-draft", "Attempt draft withdrawal", draftWithdrawSig, new { meaning = "Withdrawn" }),
            ct))
        {
            draftWithdrawRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await draftWithdrawRes.Content.ReadAsStringAsync(ct)).Should().Contain(ProtocolErrors.InvalidTransition.Code);
        }

        _store.ReadAll<Attestation>().Count(a => a.Target.Id == v1Id.Value).Should().Be(0);

        // 3. Submit v1 for review, then verify CRC (who holds attestation.sign but lacks protocol.withdraw) gets 403 and writes zero orphan attestations.
        string v1Hash;
        using (var submitV1 = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/submit-for-review", "withdraw-v1-submit", "Freeze protocol v1", null),
            ct))
        {
            submitV1.StatusCode.Should().Be(HttpStatusCode.OK);
            v1Hash = JsonDocument.Parse(await submitV1.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        var crcWithdrawSig = TestSecurityContext.CreateSigToken(
            CoordinatorIdentity.UserId,
            v1WithdrawPath,
            KernelProtocolGovernanceEndpoints.WithdrawProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var crcDenied = await crc.SendAsync(
            BuildPost(v1WithdrawPath, "withdraw-v1-crc-denied", "CRC unauthorized withdrawal", crcWithdrawSig, new
            {
                meaning = "Withdrawn",
                expected_content_hash = v1Hash,
            }),
            ct))
        {
            crcDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await crcDenied.Content.ReadAsStringAsync(ct)).Should().Contain(KernelErrors.AuthorizationDenied.Code);
        }

        _store.ReadAll<Attestation>().Count(a => a.Target.Id == v1Id.Value).Should().Be(0, "unauthorized withdrawal must not leave an orphan Withdrawn attestation");

        // 4. Approve v1, fork v2, submit v2 -> withdraw v2 from InReview -> withdraw v1 from Approved!
        var approveV1Sig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v1ApprovePath,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var approveV1 = await designer.SendAsync(
            BuildPost(v1ApprovePath, "withdraw-v1-approve", "Approve v1", approveV1Sig, new { meaning = "Approved", expected_content_hash = v1Hash }),
            ct))
        {
            approveV1.StatusCode.Should().Be(HttpStatusCode.OK, await approveV1.Content.ReadAsStringAsync(ct));
        }

        string v2Ref;
        using (var forkV2 = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/fork", "withdraw-v2-fork", "Fork protocol v2", null, new
            {
                intent = "Adjust screening window",
                rationale = "Operational site feedback",
                classification = "NonSubstantial",
                summary_of_changes = "Screening window tolerance note",
                impacted_elements = new[] { "cs.vitals" },
                transition_policy = "Automatic",
            }),
            ct))
        {
            forkV2.StatusCode.Should().Be(HttpStatusCode.Created, await forkV2.Content.ReadAsStringAsync(ct));
            v2Ref = JsonDocument.Parse(await forkV2.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("ref").GetString()!;
        }

        string v2Hash;
        using (var submitV2 = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v2Ref}/submit-for-review", "withdraw-v2-submit", "Submit protocol v2", null),
            ct))
        {
            submitV2.StatusCode.Should().Be(HttpStatusCode.OK);
            v2Hash = JsonDocument.Parse(await submitV2.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        }

        var v2WithdrawPath = KernelProtocolGovernanceEndpoints.WithdrawProtocolVersionAction(v2Ref);
        var withdrawV2Sig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v2WithdrawPath,
            KernelProtocolGovernanceEndpoints.WithdrawProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var withdrawV2 = await designer.SendAsync(
            BuildPost(v2WithdrawPath, "withdraw-v2-inreview", "Superseded by revised safety plan", withdrawV2Sig, new
            {
                meaning = "Withdrawn",
                expected_content_hash = v2Hash,
            }),
            ct))
        {
            withdrawV2.StatusCode.Should().Be(HttpStatusCode.OK, await withdrawV2.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await withdrawV2.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("Withdrawn");
            doc.GetProperty("withdrawal_reason").GetString().Should().Be("Superseded by revised safety plan");
        }

        // Withdraw Approved v1 as well.
        var withdrawV1Sig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            v1WithdrawPath,
            KernelProtocolGovernanceEndpoints.WithdrawProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using (var withdrawV1 = await designer.SendAsync(
            BuildPost(v1WithdrawPath, "withdraw-v1-approved", "Protocol terminated before enrollment", withdrawV1Sig, new
            {
                meaning = "Withdrawn",
                expected_content_hash = v1Hash,
            }),
            ct))
        {
            withdrawV1.StatusCode.Should().Be(HttpStatusCode.OK, await withdrawV1.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await withdrawV1.Content.ReadAsStringAsync(ct)).RootElement;
            doc.GetProperty("status").GetString().Should().Be("Withdrawn");
            doc.GetProperty("withdrawal_reason").GetString().Should().Be("Protocol terminated before enrollment");
        }
    }

    [Fact]
    public async Task ApprovalReadiness_MasksRestrictedEvidenceNodesInPlace_WithoutLeakingHiddenUuids()
    {
        var ct = TestContext.Current.CancellationToken;
        var v1Id = _store.SeedDraftProtocolVersion(ProtocolDesignSamples.Minimal);
        var v1Ref = NodeRef.For(v1Id).ToString();
        var studyRef = NodeRef.For(_store.StudyId).ToString();

        var maskedNodes = new Dictionary<NodeRef, MaskReason>();
        using var factory = CreateFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INodeVisibility>();
                services.AddSingleton<INodeVisibility>(new SelectiveMaskNodeVisibility(maskedNodes));
            });
        });
        using var designer = factory.CreateSignedClient(DesignerIdentity);

        using var submitV1 = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/submit-for-review", "mask-v1-sub", "Freeze protocol v1", null), ct);
        var v1Hash = JsonDocument.Parse(await submitV1.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("content_hash").GetString()!;
        var v1ApproveSig = TestSecurityContext.CreateSigToken(
            DesignerIdentity.UserId,
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionAction(v1Ref),
            KernelProtocolGovernanceEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: _clock.UtcNow);
        using var approveV1 = await designer.SendAsync(
            BuildPost(KernelProtocolGovernanceEndpoints.ApproveProtocolVersionAction(v1Ref), "mask-v1-app", "Approve v1", v1ApproveSig, new { meaning = "Approved", expected_content_hash = v1Hash }),
            ct);
        approveV1.StatusCode.Should().Be(HttpStatusCode.OK);

        using var forkV2 = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/protocol-versions/{v1Ref}/fork", "mask-v2-fork", "Fork protocol v2", null, new
            {
                intent = "Masking check",
                rationale = "Verify wire masking",
                classification = "NonSubstantial",
                summary_of_changes = "Masking test",
                impacted_elements = new[] { "cs.vitals" },
                transition_policy = "Automatic",
            }),
            ct);
        var v2Ref = JsonDocument.Parse(await forkV2.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("ref").GetString()!;
        using var submitV2 = await designer.SendAsync(BuildPost($"/api/v1/kernel/protocol-versions/{v2Ref}/submit-for-review", "mask-v2-sub", "Freeze protocol v2", null), ct);
        submitV2.StatusCode.Should().Be(HttpStatusCode.OK);

        var httpJsonOptions = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        using var simRes = await designer.PostAsJsonAsync("/api/v1/kernel/simulation/simulate", new { stored_version_ref = v2Ref, valid_at = T0, known_at = T0 }, ct);
        var simPayload = (await simRes.Content.ReadFromJsonAsync<KernelSimulationResponse>(httpJsonOptions, ct))!;
        using var saveRes = await designer.SendAsync(
            BuildPost($"/api/v1/kernel/studies/{studyRef}/simulation-reports", "mask-v2-save", "Save report", null, new { title = "Mask test report", result = simPayload }, httpJsonOptions),
            ct);
        var reportVersionRef = JsonDocument.Parse(await saveRes.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("artifact_version_ref").GetString()!;
        var reportNode = NodeRef.Parse(reportVersionRef);

        // Mask the parent version and simulation report artifact_version -> readiness response scrubs their UUIDs in place!
        maskedNodes[NodeRef.For(v1Id)] = MaskReason.NotPermitted;
        maskedNodes[reportNode] = MaskReason.Blinded;

        using var readinessRes = await designer.GetAsync($"/api/v1/kernel/protocol-versions/{v2Ref}/approval-readiness", ct);
        readinessRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await readinessRes.Content.ReadAsStringAsync(ct);
        raw.Should().NotContain(v1Id.Value.ToString());
        raw.Should().NotContain(reportNode.Id.ToString());
        var doc = JsonDocument.Parse(raw).RootElement;
        doc.GetProperty("parent_version_ref").GetString().Should().Be("masked:protocol_version:NotPermitted");
        doc.GetProperty("simulation_report_artifact_version_ref").GetString().Should().Be("masked:artifact_version:Blinded");
    }

    private sealed class SelectiveMaskNodeVisibility(IReadOnlyDictionary<NodeRef, MaskReason> masks) : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant,
            Actor reader,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken)
        {
            var matched = new Dictionary<NodeRef, MaskReason>();
            foreach (var node in nodes)
            {
                if (masks.TryGetValue(node, out var exact))
                {
                    matched[node] = exact;
                }
                else if (masks.TryGetValue(node.Unversioned(), out var unversioned))
                {
                    matched[node] = unversioned;
                }
            }

            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(matched);
        }
    }

    private sealed class NullCurrentActor : ICurrentActor
    {
        public Actor? Actor => null;
    }

    private sealed class FixedKernelChangeReason(string reason) : IKernelChangeReason
    {
        public string? Read(Microsoft.AspNetCore.Http.HttpContext context) => reason;
    }

    private sealed class PermittedAuthorizationProbe : IAuthorizationProbe
    {
        public ValueTask<AuthorizationDecision> CanIAsync(
            TenantId tenant,
            Capability capability,
            NodeRef resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(
            TenantId tenant,
            NodeRef resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CommandAvailability>>([]);
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateFactory() =>
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestBiometricLivenessKey().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(_sigTokenUseStore);
                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(_store);
                services.RemoveAll<IAuthorityStore>();
                services.AddSingleton<IAuthorityStore>(_store);
                services.RemoveAll<IStudyDirectory>();
                services.AddSingleton<IStudyDirectory>(_store);
                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(_store);
                services.RemoveAll<IStudyGovernanceReader>();
                services.AddSingleton<IStudyGovernanceReader>(_store);
                services.RemoveAll<IAttestationVerifier>();
                services.AddSingleton<IAttestationVerifier>(_store);
                services.RemoveAll<ITenantEntitlementReader>();
                services.AddSingleton<ITenantEntitlementReader>(_store);
                services.RemoveAll<ITenantResidencyReader>();
                services.AddSingleton<ITenantResidencyReader>(_store);
                services.RemoveAll<IKernelUnitOfWorkFactory>();
                services.AddSingleton<IKernelUnitOfWorkFactory>(_store);
                services.RemoveAll<ISimulationInputs>();
                services.AddSingleton<ISimulationInputs>(_store);
                services.RemoveAll<IProtocolSubmissionCheck>();

                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }
            });
        });

    private HttpClient CreateClient(GatewayIdentity identity) =>
        CreateFactory().CreateSignedClient(identity);

    private static HttpRequestMessage BuildPost(
        string path,
        string? idempotencyKey,
        string? reason,
        string? sigToken,
        object? payload = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = payload is not null ? JsonContent.Create(payload, options: jsonOptions) : null,
        };
        if (idempotencyKey is not null)
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (reason is not null)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        }

        if (sigToken is not null)
        {
            req.Headers.Add(GatewayHeaders.SigToken, sigToken);
        }

        return req;
    }

    private sealed class InMemoryGovernanceStore :
        IKernelUnitOfWorkFactory,
        ITenantDirectory,
        ITenantEntitlementReader,
        ITenantResidencyReader,
        IAuthorityStore,
        IStudyDirectory,
        IProtocolVersionReader,
        IStudyGovernanceReader,
        IAttestationVerifier,
        ISimulationInputs
    {
        private readonly TenantId _tenant;
        private readonly IClock _clock;
        private readonly Dictionary<(Type Type, object Id), AggregateRootBase> _entities = [];
        private readonly Dictionary<string, IdempotentOutcome> _idempotency = new(StringComparer.Ordinal);

        public InMemoryGovernanceStore(TenantId tenant, IClock clock)
        {
            _tenant = tenant;
            _clock = clock;
            SponsorId = OrganizationId.New();
            StudyId = StudyId.New();

            var org = new Organization(SponsorId, tenant, "CADENCE-PHARMA", "Cadence Pharma", OrganizationKind.Sponsor, "seed", "Seed sponsor");
            var study = new Study(StudyId, tenant, SponsorId, "CADENCE-101", "Cadence Cardiovascular Study", "Phase 3", "seed", "Seed study");
            Store(org);
            Store(study);
        }

        public OrganizationId SponsorId { get; }

        public StudyId StudyId { get; }

        public List<Role> DefinedRoles { get; } = [];

        public List<Grant> Grants { get; } = [];

        public void GrantRole(Actor grantee, string code, string[] capabilities, AuthorityScope scope)
        {
            var role = Role.Define(RoleId.New(), _tenant, code, code, capabilities).Value!;
            DefinedRoles.Add(role);
            var grant = Grant.Issue(GrantId.New(), _tenant, grantee, null, role.Id, scope, T0.AddDays(-1), null, false, null, Actor.ServiceAccount("seed"), T0.AddDays(-1)).Value!;
            Grants.Add(grant);
        }

        public ConceptRef SeedSbpConcept()
        {
            var conceptId = ConceptId.New();
            var concept = Concept.Define(
                conceptId,
                _tenant,
                ConceptOrigin.Sponsor,
                externalCode: null,
                ConceptKind.BiomedicalConcept,
                "Systolic Blood Pressure",
                "Sitting brachial systolic blood pressure",
                _clock.UtcNow).Value!;
            Store(concept);
            return new ConceptRef(conceptId, 1);
        }

        public ProtocolVersionId SeedDraftProtocolVersion(string content)
        {
            var id = ProtocolVersionId.New();
            var draft = ProtocolVersion.CreateFirst(id, _tenant, StudyId, content).Value!;
            Store(draft);
            return id;
        }

        public IReadOnlyList<TAggregate> ReadAll<TAggregate>()
            where TAggregate : AggregateRootBase =>
            [.. _entities.Values.OfType<TAggregate>()];

        public IKernelCommandTransaction Begin(CommandExecution execution) =>
            new Transaction(this, execution);

        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(code == TestSecurityContext.TenantCode ? (TenantId?)_tenant : null);

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. Grants.Where(g => g.Grantee == actor)]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([.. DefinedRoles.Where(r => roles.Contains(r.Id))]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            if (resource.Kind == NodeKinds.Study && StudyId.FromGuid(resource.Id) == StudyId)
            {
                return Task.FromResult(new ScopePath(SponsorId, StudyId));
            }

            if (resource.Kind == NodeKinds.ProtocolVersion
                && Find<ProtocolVersion>(ProtocolVersionId.FromGuid(resource.Id)) is { } pv)
            {
                return Task.FromResult(new ScopePath(SponsorId, pv.StudyId));
            }

            if (resource.Kind == NodeKinds.Effectivity
                && Find<Effectivity>(EffectivityId.FromGuid(resource.Id)) is { } eff)
            {
                return Task.FromResult(new ScopePath(SponsorId, eff.StudyId, eff.Scope.Country, eff.Scope.StudySiteId));
            }

            if (resource.Kind == NodeKinds.StudySite
                && Find<StudySite>(StudySiteId.FromGuid(resource.Id)) is { } ss)
            {
                return Task.FromResult(new ScopePath(SponsorId, ss.StudyId, Find<Site>(ss.SiteId)?.Country, ss.Id));
            }

            if (resource.Kind == NodeKinds.Enrollment
                && Find<Enrollment>(EnrollmentId.FromGuid(resource.Id)) is { } enr
                && Find<StudySite>(enr.StudySiteId) is { } enrSite)
            {
                return Task.FromResult(new ScopePath(SponsorId, enrSite.StudyId, Find<Site>(enrSite.SiteId)?.Country, enrSite.Id));
            }

            return Task.FromResult(ScopePath.TenantWide);
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);

        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyDirectoryEntry>>([ToStudyEntry()]);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(studyId == StudyId ? ToStudyEntry() : null);

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(studyCode, "CADENCE-101", StringComparison.OrdinalIgnoreCase) ? ToStudyEntry() : null);

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>([new(SponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma", OrganizationKind.Sponsor)]);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(organizationId == SponsorId ? new OrganizationDirectoryEntry(SponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma", OrganizationKind.Sponsor) : null);

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(code, "CADENCE-PHARMA", StringComparison.OrdinalIgnoreCase) ? new OrganizationDirectoryEntry(SponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma", OrganizationKind.Sponsor) : null);

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>([
                .. ReadAll<Site>()
                    .OrderBy(s => s.SiteCode, StringComparer.Ordinal)
                    .Select(ToSiteDirectoryEntry),
            ]);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Find<Site>(siteId) is { } site ? ToSiteDirectoryEntry(site) : null);

        Task<SiteDirectoryEntry?> IStudyDirectory.FindSiteByCodeAsync(
            TenantId tenant,
            string siteCode,
            OrganizationId? institutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReadAll<Site>()
                .FirstOrDefault(s =>
                    string.Equals(s.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)
                    && (institutionId is null || s.InstitutionId == institutionId.Value)) is { } site
                ? ToSiteDirectoryEntry(site)
                : null);

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>([
                .. ReadAll<StudySite>()
                    .Where(ss => ss.StudyId == studyId)
                    .Select(ToStudySiteDirectoryEntry)
                    .OrderBy(ss => ss.SiteCode, StringComparer.Ordinal),
            ]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Find<StudySite>(studySiteId) is { } ss ? ToStudySiteDirectoryEntry(ss) : null);

        public Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(
            TenantId tenant,
            StudyId studyId,
            SiteId siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadAll<StudySite>()
                .FirstOrDefault(ss => ss.StudyId == studyId && ss.SiteId == siteId) is { } ss
                ? ToStudySiteDirectoryEntry(ss)
                : null);

        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>([
                .. ReadAll<ProtocolVersion>()
                    .Where(v => v.StudyId == study)
                    .OrderByDescending(v => v.Number)
                    .Select(ToSummary),
            ]);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(Find<ProtocolVersion>(version) is { } found ? ToSummary(found) : null);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(Find<ProtocolVersion>(version)?.Design());

        public Task<IReadOnlyList<StudyArtifactSummary>> ListStudyArtifactsAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyArtifactSummary>>([
                .. ReadAll<Artifact>()
                    .Where(a => a.StudyId == studyId)
                    .Select(a => new StudyArtifactSummary(
                        a.Id,
                        a.Kind,
                        studyId,
                        a.Title,
                        a.LatestVersion.Id,
                        a.LatestVersion.Number,
                        a.LatestVersion.ContentHash,
                        a.LatestVersion.SizeBytes,
                        a.LatestVersion.MediaType,
                        a.LatestVersion.StorageRef,
                        a.LatestVersion.RecordedAt)),
            ]);

        public Task<IReadOnlyList<StudyEffectivitySummary>> ListStudyEffectivitiesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyEffectivitySummary>>([
                .. ReadAll<Effectivity>()
                    .Where(e => e.StudyId == studyId)
                    .Select(ToEffectivitySummary),
            ]);

        public Task<StudyEffectivitySummary?> FindEffectivityAsync(TenantId tenant, EffectivityId effectivityId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Find<Effectivity>(effectivityId) is { } eff ? ToEffectivitySummary(eff) : null);

        public Task<IReadOnlyList<StudySiteGovernanceSummary>> ListStudySitesAsync(
            TenantId tenant, StudyId studyId, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteGovernanceSummary>>([
                .. ReadAll<StudySite>()
                    .Where(s => s.StudyId == studyId)
                    .Select(s => ToStudySiteSummary(s, validAt, knownAt)),
            ]);

        public Task<StudySiteGovernanceSummary?> FindStudySiteAsync(
            TenantId tenant, StudySiteId studySiteId, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(Find<StudySite>(studySiteId) is { } ss ? ToStudySiteSummary(ss, validAt, knownAt) : null);

        public Task<SiteLookupEntry?> FindSiteByCodeAsync(TenantId tenant, string siteCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadAll<Site>().FirstOrDefault(s => string.Equals(s.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)) is { } site
                ? new SiteLookupEntry(site.Id, site.InstitutionId, site.SiteCode, site.Name, site.Country)
                : null);

        public ValueTask<AttestationVerification> VerifyAsync(AttestationRequirement requirement, CancellationToken cancellationToken)
        {
            if (requirement.Tenant != _tenant || Find<Attestation>(requirement.AttestationId) is not { } attestation)
            {
                return ValueTask.FromResult(AttestationVerification.Unmet("Attestation not found."));
            }

            if (attestation.Meaning != requirement.Meaning || attestation.Target != requirement.Target)
            {
                return ValueTask.FromResult(AttestationVerification.Unmet("Attestation target or meaning mismatch."));
            }

            return ValueTask.FromResult(AttestationVerification.Met);
        }

        public Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == _tenant && Find<ProtocolVersion>(id) is { } pv
                ? new ProtocolVersionSnapshot(pv.Id, pv.StudyId, pv.Number, pv.Status, pv.ParentVersionId, pv.Content, pv.ContentHash, pv.VersionIndex, pv.Amendment)
                : null);

        public Task<DependentsReport> DependentsAsync(
            TenantId tenant,
            Actor reader,
            ProtocolVersionId baseVersion,
            ProtocolVersionId? candidate,
            IReadOnlySet<NodeRef> impactedElements,
            SimulationAsOf asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DependentsReport(
                NodeRef.For(baseVersion).ToString(),
                LineageQuery.DefaultDepth,
                asOf.ValidAt,
                asOf.KnownAt,
                [],
                0,
                false));

        public Task<bool> StudyExistsAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == _tenant && studyId == StudyId);

        public Task<SimulationStudySnapshot> LoadStudySnapshotAsync(
            TenantId tenant,
            StudyId studyId,
            SimulationAsOf asOf,
            CancellationToken cancellationToken)
        {
            if (tenant != _tenant || studyId != StudyId)
            {
                return Task.FromResult(SimulationStudySnapshot.Empty);
            }

            var designs = ReadAll<ProtocolVersion>()
                .Where(v => v.StudyId == studyId)
                .ToDictionary(v => v.Id, v => v.Design());
            return Task.FromResult(SimulationStudySnapshot.Empty with
            {
                Loaded = true,
                ProtocolDesigns = designs,
            });
        }

        private StudyDirectoryEntry ToStudyEntry() =>
            new(StudyId, 1, "CADENCE-101", "Cadence Cardiovascular Study", "Phase 3", SponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma", ReadAll<StudySite>().Count(s => s.StudyId == StudyId));

        private static SiteDirectoryEntry ToSiteDirectoryEntry(Site site) =>
            new(site.Id, 1, site.InstitutionId, 1, "CADENCE-PHARMA", "Cadence Pharma", site.SiteCode, site.Name, site.Country);

        private StudySiteDirectoryEntry ToStudySiteDirectoryEntry(StudySite ss)
        {
            var site = Find<Site>(ss.SiteId)!;
            return new StudySiteDirectoryEntry(ss.Id, 1, ss.StudyId, site.Id, 1, site.SiteCode, site.Name, site.Country, site.InstitutionId, 1, "CADENCE-PHARMA", "Cadence Pharma", ss.Status);
        }

        private static ProtocolVersionSummary ToSummary(ProtocolVersion v) =>
            new(v.Id, v.StudyId, v.Number, v.ParentVersionId, v.Status, v.ContentHash, v.Amendment?.Intent, v.ApprovalAttestationId, v.WithdrawalReason);

        private StudyEffectivitySummary ToEffectivitySummary(Effectivity e) =>
            new(
                e.Id,
                e.StudyId,
                e.ProtocolVersionId,
                Find<ProtocolVersion>(e.ProtocolVersionId)?.Number ?? 0,
                e.Scope,
                e.Status,
                e.StatusSince,
                e.CurrentEvidence());

        private StudySiteGovernanceSummary ToStudySiteSummary(StudySite ss, DateTimeOffset validAt, DateTimeOffset knownAt)
        {
            var site = Find<Site>(ss.SiteId)!;
            var deciding = EffectivityResolution.EffectiveAt(ReadAll<Effectivity>().Where(e => e.StudyId == ss.StudyId), ss.Id, site.Country, validAt, knownAt);
            var effective = deciding
                .Select(d => new EffectiveVersion(d.ProtocolVersionId, Find<ProtocolVersion>(d.ProtocolVersionId)?.Number ?? 0, d.Id, d.ScopeLevel))
                .OrderByDescending(v => v.Number)
                .ToList();
            return new StudySiteGovernanceSummary(ss.Id, ss.StudyId, ss.SiteId, site.SiteCode, site.Name, site.Country, ss.Status, effective);
        }

        public Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
            TenantId tenantId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken,
            DateTimeOffset? knownAt = null) =>
            Task.FromResult(new TenantEntitlementSnapshot(
                tenantId,
                SubscriptionId.New(),
                CommercialTier.Complete,
                SubscriptionStatus.Active,
                StartedAt: T0.AddDays(-30),
                TrialEndsAt: null,
                IsReadOnly: false,
                ProductFamilies:
                [
                    ProductFamily.Blueprint,
                    ProductFamily.Capture,
                    ProductFamily.Orbit,
                    ProductFamily.Engage,
                    ProductFamily.Allocate,
                    ProductFamily.Vigil,
                    ProductFamily.Lens,
                    ProductFamily.Assure,
                ]));

        public Task<TenantResidency?> FindAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<TenantResidency?>(null);

        private TAggregate? Find<TAggregate>(object id)
            where TAggregate : AggregateRootBase =>
            _entities.TryGetValue((typeof(TAggregate), id), out var found) ? (TAggregate)found : null;

        private void Store(AggregateRootBase aggregate)
        {
            var id = aggregate.GetType().GetProperty("Id")!.GetValue(aggregate)!;
            _entities[(aggregate.GetType(), id)] = aggregate;
        }

        private sealed class Transaction(InMemoryGovernanceStore store, CommandExecution execution) : IKernelCommandTransaction, IKernelUnitOfWork
        {
            private readonly List<AggregateRootBase> _staged = [];

            public IKernelUnitOfWork Work => this;

            public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
                Task.FromResult(store._idempotency.TryGetValue($"{execution.Command.TenantId}:{execution.Command.IdempotencyKey}", out var prior) ? prior : null);

            public void RecordPermit(AuthorizationDecision permit)
            {
            }

            public void RecordProposalExecution(NodeRef proposal)
            {
            }

            public Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken)
            {
                foreach (var aggregate in _staged)
                {
                    store.Store(aggregate);
                }

                store._idempotency[$"{execution.Command.TenantId}:{execution.Command.IdempotencyKey}"] =
                    new IdempotentOutcome(execution.RequestHash, resultJson);
                return Task.FromResult(CommitOutcome.Committed);
            }

            public Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task RecordRefusalAsync(KernelError refusal, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
                where TAggregate : AggregateRootBase =>
                Task.FromResult(store.Find<TAggregate>(id));

            public Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
                where TAggregate : AggregateRootBase =>
                Task.FromResult(store.Find<TAggregate>(id) is not null);

            public Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
                where TAggregate : AggregateRootBase =>
                Task.FromResult(store.Find<TAggregate>(id));

            public Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
                Expression<Func<TAggregate, bool>> filter, CancellationToken cancellationToken)
                where TAggregate : AggregateRootBase
            {
                var compiled = filter.Compile();
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. store.ReadAll<TAggregate>().Where(compiled)]);
            }

            public Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
                where TAggregate : AggregateRootBase =>
                Task.CompletedTask;

            public Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public void Add(AggregateRootBase aggregate) => _staged.Add(aggregate);

            public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
            {
            }

            public void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null, NodeRef? target = null)
            {
            }

            public void IncludePart(NodeRef part, bool newVersion)
            {
            }

            public void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind)
            {
            }
        }
    }
}
