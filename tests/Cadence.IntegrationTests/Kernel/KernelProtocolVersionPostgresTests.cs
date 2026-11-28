using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Protocol;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Protocol versions and attestations through the production pipeline against PostgreSQL (spec §6.2, §9.5, #4775):
/// the lifecycle with attested approval and withdrawal, forks that leave the parent approved, and the database
/// trigger that freezes approved content whoever connects. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelProtocolVersionPostgresTests(KernelPostgresFixture fixture)
    : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// Validates the full path: a draft is created, revised and submitted (which sets the canonical content hash), a
    /// person attests approval of that hash, and approval records the attestation, with one event per step.
    /// </summary>
    [Fact(DisplayName = "Protocol_DraftReviewAttestApprove_EndToEnd")]
    public async Task ProtocolDraftReviewAttestApproveEndToEnd()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var id = ProtocolVersionId.New();
        const string revised = """
            {
              "schemaVersion": 1,
              "title": "CARDIO-1",
              "arms": [
                { "id": "arm.A", "name": "Active", "type": "Experimental", "interventionIds": [] },
                { "id": "arm.B", "name": "Placebo", "type": "PlaceboComparator", "interventionIds": [] },
                { "id": "arm.C", "name": "Cohort C", "type": "Experimental", "interventionIds": [] }
              ]
            }
            """;

        var draft = await _scenario.CreateDraftAsync(tenant, study, id, ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, id, revised, null, KernelProtocolScenario.Key(), "Add treatment arm C"), ct);
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready for review"), ct);
        var attestation = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        var approved = await _scenario.ApproveAsync(tenant, id, attestation, ct);

        draft.Value.Should().Be(new ProtocolVersionState(id, study, 1, ProtocolVersionStatus.Draft, null));
        inReview.ContentHash.Should().Be(ContentHash.Compute(revised));
        approved.Value.Should().Be(new ProtocolVersionState(id, study, 1, ProtocolVersionStatus.Approved, inReview.ContentHash));

        await using var db = fixture.CreateContext(tenant);
        var stored = await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == id, ct);
        stored.Content.Should().Be(revised, "the exact submitted text is kept");
        stored.ApprovalAttestationId.Should().Be(attestation);
        stored.VersionIndex.Should().Be(4);
        var signature = await db.Attestations.AsNoTracking().SingleAsync(a => a.Id == attestation, ct);
        signature.ActorId.Should().Be(KernelPostgresFixture.ActorId);
        signature.Target.Should().Be(KernelProtocolScenario.Target(inReview));
        signature.RecordedAt.Should().Be(Cadence.Kernel.Time.KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow));

        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant).OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => e.Type).ToListAsync(ct);
        events.Should().Contain(["ProtocolVersionDrafted", "ProtocolVersionRevised", "ProtocolVersionSubmittedForReview", "AttestationRecorded", "ProtocolVersionApproved"]);
    }

    /// <summary>
    /// Validates that approval needs an attestation meaning Approved on this version's current content: a reviewed
    /// attestation, one for other content (signed before the version went back to draft and changed) and an unknown
    /// one are all refused, and the version stays in review.
    /// </summary>
    [Fact(DisplayName = "Approve_RequiresAnApprovedAttestation_OnTheCurrentContentHash")]
    public async Task ApproveRequiresAnApprovedAttestationOnTheCurrentContentHash()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var first = await _scenario.InReviewAsync(tenant, study, ct);
        var id = first.ProtocolVersionId;
        var stale = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(first)), ct);
        var reviewed = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Reviewed, KernelProtocolScenario.Target(first)), ct);

        await _scenario.OkAsync(new ReturnProtocolVersionToDraft(tenant, id, KernelProtocolScenario.Key(), "Changes requested"), ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, id, ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with { Title = "CARDIO-1b" }), null, KernelProtocolScenario.Key(), "Rename protocol title"), ct);
        var second = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Resubmit for review"), ct);
        second.ContentHash.Should().NotBe(first.ContentHash);

        foreach (var offered in new[] { stale, reviewed, AttestationId.New() })
        {
            var result = await _scenario.ApproveAsync(tenant, id, offered, ct);
            result.Error!.Code.Should().Be(ProtocolErrors.AttestationRejected.Code);
        }

        (await _scenario.AttestAsync(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(first), ct))
            .Error.Should().Be(TrustErrors.TargetContentChanged, "an attestation cannot be signed against superseded content");

        await using var db = fixture.CreateContext(tenant);
        (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == id, ct)).Status.Should().Be(ProtocolVersionStatus.InReview);
    }

    /// <summary>
    /// Validates the refusals when signing: a draft has nothing to sign, an unknown record or kind is refused, and a
    /// service account cannot sign even when the command is otherwise valid.
    /// </summary>
    [Fact(DisplayName = "Attest_RefusesDrafts_UnknownTargets_AndNonPersons")]
    public async Task AttestRefusesDraftsUnknownTargetsAndNonPersons()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, id, ct)).IsSuccess.Should().BeTrue();
        var draftTarget = new AttestationTarget(AttestationTarget.ProtocolVersionKind, id.Value, ContentHash.Compute(KernelProtocolScenario.Content));

        (await _scenario.AttestAsync(tenant, AttestationMeaning.Approved, draftTarget, ct)).Error.Should().Be(TrustErrors.TargetNotSignable);
        (await _scenario.AttestAsync(tenant, AttestationMeaning.Approved, draftTarget with { Id = Guid.CreateVersion7() }, ct))
            .Error.Should().Be(TrustErrors.TargetUnknown);
        (await _scenario.AttestAsync(tenant, AttestationMeaning.Approved, draftTarget with { Kind = "Artifact" }, ct))
            .Error!.Code.Should().Be(TrustErrors.TargetKindUnsupported.Code);

        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready for review"), ct);
        fixture.CurrentActor.Actor = Actor.ServiceAccount("importer");
        (await _scenario.AttestAsync(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview), ct))
            .Error.Should().Be(TrustErrors.PersonRequired);
    }

    /// <summary>
    /// Validates #4775's core defect: forking an approved version leaves it approved, numbers the draft after every
    /// version in the study, and stores the amendment; a study's first version can only be created once.
    /// </summary>
    [Fact(DisplayName = "Fork_KeepsTheParentApproved_NumbersTheDraft_AndStoresTheAmendment")]
    [GxPRequirement("PRD-CRF-012")]
    public async Task ForkKeepsTheParentApprovedNumbersTheDraftAndStoresTheAmendment()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (parent, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var forkId = ProtocolVersionId.New();

        var fork = await _scenario.OkAsync(
            new ForkProtocolVersion(tenant, parent.ProtocolVersionId, forkId, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment 1"), ct);
        var second = await _scenario.OkAsync(
            new ForkProtocolVersion(tenant, parent.ProtocolVersionId, ProtocolVersionId.New(), KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment 1b"), ct);

        fork.Should().Be(new ProtocolVersionState(forkId, study, 2, ProtocolVersionStatus.Draft, null));
        second.Number.Should().Be(3);
        (await _scenario.CreateDraftAsync(tenant, study, ProtocolVersionId.New(), ct)).Error.Should().Be(ProtocolErrors.FirstVersionExists);

        await using var db = fixture.CreateContext(tenant);
        (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == parent.ProtocolVersionId, ct)).Status.Should().Be(ProtocolVersionStatus.Approved);
        var stored = await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == forkId, ct);
        stored.ParentVersionId.Should().Be(parent.ProtocolVersionId);
        stored.Amendment.Should().BeEquivalentTo(KernelProtocolScenario.Amendment);
        stored.Content.Should().Be(KernelProtocolScenario.Content);
    }

    /// <summary>Validates withdrawal: it needs an attestation meaning Withdrawn on the content and is final.</summary>
    [Fact(DisplayName = "Withdraw_RequiresAWithdrawnAttestation_AndIsFinal")]
    public async Task WithdrawRequiresAWithdrawnAttestationAndIsFinal()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (version, approval) = await _scenario.ApprovedAsync(tenant, study, ct);
        var target = KernelProtocolScenario.Target(version);
        var withdrawal = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Withdrawn, target), ct);

        (await fixture.Sender.SendAsync(new WithdrawProtocolVersion(tenant, version.ProtocolVersionId, approval, KernelProtocolScenario.Key(), "Terminated"), cancellationToken: ct))
            .Error!.Code.Should().Be(ProtocolErrors.AttestationRejected.Code);
        var withdrawn = await _scenario.OkAsync(new WithdrawProtocolVersion(tenant, version.ProtocolVersionId, withdrawal, KernelProtocolScenario.Key(), "Terminated"), ct);
        withdrawn.Status.Should().Be(ProtocolVersionStatus.Withdrawn);

        (await fixture.Sender.SendAsync(
                new ForkProtocolVersion(tenant, version.ProtocolVersionId, ProtocolVersionId.New(), KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Attempted too late"),
                cancellationToken: ct))
            .Error!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code);

        await using var db = fixture.CreateContext(tenant);
        var stored = await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == version.ProtocolVersionId, ct);
        stored.WithdrawalAttestationId.Should().Be(withdrawal);
        stored.WithdrawalReason.Should().Be("Terminated");
    }

    /// <summary>
    /// Validates that an attestation in another tenant cannot approve a version: the verifier reads through the
    /// version's tenant, where the attestation does not exist (ADR-2250).
    /// </summary>
    [Fact(DisplayName = "Approve_IgnoresAttestationsFromAnotherTenant")]
    public async Task ApproveIgnoresAttestationsFromAnotherTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenantA, studyA) = await _scenario.NewStudyAsync(ct);
        var (tenantB, studyB) = await _scenario.NewStudyAsync(ct);
        var versionA = await _scenario.InReviewAsync(tenantA, studyA, ct);
        var versionB = await _scenario.InReviewAsync(tenantB, studyB, ct);
        var signedInB = await _scenario.OkAsync(_scenario.AttestCommand(tenantB, AttestationMeaning.Approved, KernelProtocolScenario.Target(versionB)), ct);

        var result = await _scenario.ApproveAsync(tenantA, versionA.ProtocolVersionId, signedInB, ct);

        result.Error!.Code.Should().Be(ProtocolErrors.AttestationRejected.Code);
        result.Error.Message.Should().Contain("does not exist");
    }

    /// <summary>
    /// Validates the database side of the content freeze (spec §6.2, PRD-CRF-012): even the superuser cannot change an
    /// approved version's content, hash or status other than along the lifecycle, delete it, or insert a version that
    /// is not a fresh draft (SQLSTATE KA002).
    /// </summary>
    [Fact(DisplayName = "ProtocolVersions_Trigger_FreezesApprovedContent_ForEveryDatabaseUser")]
    [GxPRequirement("PRD-CRF-012")]
    public async Task ProtocolVersionsTriggerFreezesApprovedContentForEveryDatabaseUser()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var id = version.ProtocolVersionId.Value;

        foreach (var statement in new[]
        {
            $"UPDATE kernel.protocol_versions SET content = '{{\"tampered\":true}}' WHERE id = '{id}'",
            $"UPDATE kernel.protocol_versions SET content_hash = repeat('0', 64) WHERE id = '{id}'",
            $"UPDATE kernel.protocol_versions SET status = 'Draft', content_hash = NULL WHERE id = '{id}'",
            $"UPDATE kernel.protocol_versions SET number = 9 WHERE id = '{id}'",
            $"UPDATE kernel.protocol_versions SET approval_attestation_id = NULL WHERE id = '{id}'",
            $"DELETE FROM kernel.protocol_versions WHERE id = '{id}'",
            "TRUNCATE kernel.protocol_versions CASCADE",
            $$"""
            INSERT INTO kernel.protocol_versions (id, tenant_id, study_id, number, status, content, content_hash, created_at, created_by, reason_for_change, version_index)
            VALUES ('{{Guid.CreateVersion7()}}', '{{tenant.Value}}', '{{study.Value}}', 50, 'Approved', '{}', repeat('0', 64), now(), 'sql', 'sql', 1)
            """,
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement))
                .Which.SqlState.Should().Be(ProtocolVersionSql.ViolationSqlState);
        }

        // A lifecycle step the trigger allows still works for the superuser, so the guard is not a blanket lock.
        await ExecuteAsAdminAsync($"UPDATE kernel.protocol_versions SET status = 'Superseded' WHERE id = '{id}'", ct);
    }

    /// <summary>
    /// Validates that approval is signed by the person approving (21 CFR 11.50): an Approved attestation over the
    /// current content, signed by someone else, is refused. A version that is not in review is refused before any
    /// signature is looked at, whether it is still a draft or already approved.
    /// </summary>
    [Fact(DisplayName = "Approve_RefusesAnotherPersonsSignature_AndVersionsNotInReview")]
    public async Task ApproveRefusesAnotherPersonsSignatureAndVersionsNotInReview()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var inReview = await _scenario.InReviewAsync(tenant, study, ct);
        fixture.CurrentActor.Actor = Actor.Person("someone-else@cadence.test");
        var someoneElses = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);

        (await _scenario.ApproveAsync(tenant, inReview.ProtocolVersionId, someoneElses, ct)).Error!.Code.Should().Be(ProtocolErrors.AttestationRejected.Code);

        var signed = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        (await _scenario.ApproveAsync(tenant, inReview.ProtocolVersionId, signed, ct)).IsSuccess.Should().BeTrue();
        (await _scenario.ApproveAsync(tenant, inReview.ProtocolVersionId, signed, ct)).Error!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code);

        var (otherTenant, otherStudy) = await _scenario.NewStudyAsync(ct);
        var draft = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(otherTenant, otherStudy, draft, ct)).IsSuccess.Should().BeTrue();
        (await _scenario.ApproveAsync(otherTenant, draft, AttestationId.New(), ct)).Error!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code);
        (await _scenario.ApproveAsync(otherTenant, ProtocolVersionId.New(), AttestationId.New(), ct)).Error.Should().Be(KernelErrors.NotFound);

        await using var db = fixture.CreateContext(otherTenant);
        (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == draft, ct)).Status.Should().Be(ProtocolVersionStatus.Draft);
    }

    /// <summary>
    /// Validates the lookups before a version is created: a first draft for an unknown study and a fork of an unknown
    /// parent are refused, and a version or attestation id already in use cannot be reused under a new idempotency key.
    /// </summary>
    [Fact(DisplayName = "ProtocolCommands_RefuseUnknownStudiesAndParents_AndReusedIds")]
    public async Task ProtocolCommandsRefuseUnknownStudiesAndParentsAndReusedIds()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (approved, approval) = await _scenario.ApprovedAsync(tenant, study, ct);

        (await _scenario.CreateDraftAsync(tenant, StudyId.New(), ProtocolVersionId.New(), ct)).Error.Should().Be(ProtocolErrors.StudyUnknown);
        (await fixture.Sender.SendAsync(
                new ForkProtocolVersion(tenant, ProtocolVersionId.New(), ProtocolVersionId.New(), KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Protocol amendment"),
                cancellationToken: ct))
            .Error!.Code.Should().Be(KernelErrors.NotFound.Code);

        (await _scenario.CreateDraftAsync(tenant, study, approved.ProtocolVersionId, ct)).Error.Should().Be(KernelErrors.AlreadyExists);
        (await fixture.Sender.SendAsync(
                new ForkProtocolVersion(tenant, approved.ProtocolVersionId, approved.ProtocolVersionId, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Protocol amendment"),
                cancellationToken: ct))
            .Error.Should().Be(KernelErrors.AlreadyExists);
        var reused = _scenario.AttestCommand(tenant, AttestationMeaning.Reviewed, KernelProtocolScenario.Target(approved)) with { AttestationId = approval };
        (await fixture.Sender.SendAsync(reused, cancellationToken: ct)).Error.Should().Be(KernelErrors.AlreadyExists);

        await using var db = fixture.CreateContext(tenant);
        (await db.ProtocolVersions.AsNoTracking().CountAsync(v => v.StudyId == study, ct)).Should().Be(1);
    }

    /// <summary>
    /// Validates withdrawal from review: a version in review can be withdrawn with a Withdrawn attestation, while a
    /// draft cannot be withdrawn at all.
    /// </summary>
    [Fact(DisplayName = "Withdraw_FromReviewWithASignature_ButNeverADraft")]
    public async Task WithdrawFromReviewWithASignatureButNeverADraft()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var inReview = await _scenario.InReviewAsync(tenant, study, ct);
        var signed = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Withdrawn, KernelProtocolScenario.Target(inReview)), ct);

        var withdrawn = await _scenario.OkAsync(new WithdrawProtocolVersion(tenant, inReview.ProtocolVersionId, signed, KernelProtocolScenario.Key(), "Study cancelled"), ct);
        withdrawn.Status.Should().Be(ProtocolVersionStatus.Withdrawn);

        var (otherTenant, otherStudy) = await _scenario.NewStudyAsync(ct);
        var draft = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(otherTenant, otherStudy, draft, ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(new WithdrawProtocolVersion(otherTenant, draft, AttestationId.New(), KernelProtocolScenario.Key(), "Abandoned draft"), cancellationToken: ct))
            .Error!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code);
    }

    /// <summary>
    /// Validates that forks sent at the same time number one after another under the study's lock (K-TX-4): each gets
    /// a distinct number following the parent, with none skipped.
    /// </summary>
    [Fact(DisplayName = "Fork_ConcurrentForks_GetConsecutiveNumbers")]
    [KernelRule("K-TX-4")]
    public async Task ForkConcurrentForksGetConsecutiveNumbers()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const int count = 6;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (parent, _) = await _scenario.ApprovedAsync(tenant, study, ct);

        var results = await Task.WhenAll(Enumerable.Range(0, count).Select(i => fixture.Sender.SendAsync(
            new ForkProtocolVersion(tenant, parent.ProtocolVersionId, ProtocolVersionId.New(), KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), $"Amendment {i}"),
            cancellationToken: ct)));

        results.Should().OnlyContain(r => r.IsSuccess);
        results.Select(r => r.Value!.Number).Should().BeEquivalentTo(Enumerable.Range(2, count));
    }

    /// <summary>
    /// Validates ADR-2475, K-PROT-2, and K-SIM-5 on PostgreSQL: submitting a forked draft with undeclared schedule or
    /// eligibility changes refuses with structured undeclared element IDs; approving a forked amendment requires a
    /// verified Reviewed attestation on an unmasked SimulationReport artifact matching both parent and amended hashes,
    /// and returning the amendment to draft invalidates prior simulation attestations.
    /// </summary>
    [Fact(DisplayName = "ForkedAmendment_EnforcesSoaDirectiveCheck_AndAttestedSimulationReportGate")]
    [KernelRule("K-PROT-2")]
    [KernelRule("K-SIM-5")]
    public async Task ForkedAmendmentEnforcesSoaDirectiveCheckAndAttestedSimulationReportGate()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (parent, _) = await _scenario.ApprovedAsync(tenant, study, ct);

        var forkId = ProtocolVersionId.New();
        var baseDesign = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
        };
        var amendedDesign = baseDesign with
        {
            PlannedActivities =
            [
                new PlannedActivity("visit.day15", "Day 15 Visit", "ep.treatment", [ActivityLocation.Site], [], new ActivityTiming(15, 1, 1)),
            ],
            EligibilityCriteria =
            [
                new EligibilityCriterion("crit.inc1", CriterionCategory.Inclusion, "Confirmed diagnosis", []),
            ],
        };

        var incompleteAmendment = new ProtocolAmendment(
            "Add Day 15 visit and inclusion check",
            "Safety monitoring update",
            AmendmentClassification.Substantial,
            "Adds Day 15 visit and inclusion criterion",
            ["ep.treatment"],
            TransitionPolicy.RequiresReconsent);

        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent.ProtocolVersionId, forkId, incompleteAmendment, KernelProtocolScenario.Key(), "Fork protocol v2"), ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, forkId, ProtocolDesignDocument.Write(amendedDesign), incompleteAmendment, KernelProtocolScenario.Key(), "Add Day 15"), ct);

        // 1. SubmitProtocolVersionForReview refuses undeclared SoA/eligibility elements.
        var refusedSubmit = await _scenario.SubmitAsync(tenant, forkId, ct);
        refusedSubmit.Error!.Code.Should().Be(ProtocolErrors.UndeclaredImpactedElements.Code);
        refusedSubmit.Error.Errors.Should().ContainKey("impactedElements");
        refusedSubmit.Error.Errors!["impactedElements"].Should().BeEquivalentTo("crit.inc1", "visit.day15");
        refusedSubmit.Error.Explanation.Should().NotBeNull();

        // 2. Declare all impacted elements and submit -> InReview.
        var completeAmendment = incompleteAmendment with
        {
            ImpactedElements = ["ep.treatment", "visit.day15", "crit.inc1"],
        };
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, forkId, ProtocolDesignDocument.Write(amendedDesign), completeAmendment, KernelProtocolScenario.Key(), "Declare elements"), ct);
        var frozenV2 = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, forkId, KernelProtocolScenario.Key(), "Submit protocol v2"), ct);
        var approvedSigV2 = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(frozenV2)), ct);

        // 3. Approve without SimulationReport -> SimulationReviewRequired.
        (await _scenario.ApproveAsync(tenant, forkId, approvedSigV2, ct)).Error!.Code.Should().Be(ProtocolErrors.SimulationReviewRequired.Code);

        // 4. Save SimulationReport without Reviewed attestation -> still SimulationReviewRequired; Approved meaning on report -> still refused.
        var sim = (await fixture.Simulation.SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(forkId)), ct)).Value!;
        var savedReport = await _scenario.OkAsync(
            new SaveSimulationReport(tenant, study, ArtifactId.New(), ArtifactVersionId.New(), "v2 Simulation Report", sim, KernelProtocolScenario.Key(), "Save report"),
            ct);
        (await _scenario.ApproveAsync(tenant, forkId, approvedSigV2, ct)).Error!.Code.Should().Be(ProtocolErrors.SimulationReviewRequired.Code);

        var reportTarget = new AttestationTarget(NodeKinds.ArtifactVersion, savedReport.LatestVersionId.Value, savedReport.LatestContentHash);
        var wrongMeaningOnReport = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, reportTarget), ct);
        (await _scenario.ApproveAsync(tenant, forkId, approvedSigV2, ct, wrongMeaningOnReport)).Error!.Code.Should().Be(ProtocolErrors.SimulationReviewRequired.Code);

        // 5. Sign Reviewed on report, then ReturnToDraft + Revise + Submit -> prior Reviewed attestation is stale and refused!
        var staleReviewedSig = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Reviewed, reportTarget), ct);
        await _scenario.OkAsync(new ReturnProtocolVersionToDraft(tenant, forkId, KernelProtocolScenario.Key(), "Adjust window"), ct);
        var revisedDesign2 = amendedDesign with
        {
            PlannedActivities =
            [
                new PlannedActivity("visit.day15", "Day 15 Visit", "ep.treatment", [ActivityLocation.Site], [], new ActivityTiming(16, 1, 2)),
            ],
        };
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, forkId, ProtocolDesignDocument.Write(revisedDesign2), completeAmendment, KernelProtocolScenario.Key(), "Move to Day 16"), ct);
        var reFrozenV2 = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, forkId, KernelProtocolScenario.Key(), "Resubmit v2"), ct);
        var newApprovedSigV2 = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(reFrozenV2)), ct);

        (await _scenario.ApproveAsync(tenant, forkId, newApprovedSigV2, ct, staleReviewedSig)).Error!.Code.Should().Be(ProtocolErrors.SimulationReviewRequired.Code);

        // 6. Simulate & attest Reviewed on current frozen v2 -> Approve succeeds and records DerivedFrom lineage!
        var (_, currentReport, validReviewedSig) = await _scenario.SimulateAndAttestReviewAsync(tenant, study, forkId, ct);
        var approvedV2 = await _scenario.ApproveAsync(tenant, forkId, newApprovedSigV2, ct, validReviewedSig);
        approvedV2.IsSuccess.Should().BeTrue(approvedV2.Error?.Message);
        approvedV2.Value!.Status.Should().Be(ProtocolVersionStatus.Approved);

        await using var db = fixture.CreateContext(tenant);
        var derivedEdges = await db.Set<LineageEdgeRecord>().AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.Artifact && e.ToId == currentReport.ArtifactId.Value && e.Kind == LineageEdgeKind.DerivedFrom)
            .ToListAsync(ct);
        derivedEdges.Should().ContainSingle().Which.FromId.Should().Be(forkId.Value);
    }

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
