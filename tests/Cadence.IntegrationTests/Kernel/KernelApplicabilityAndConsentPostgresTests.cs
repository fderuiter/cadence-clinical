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

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// PostgreSQL integration tests for bitemporal <see cref="Applicability"/> and <see cref="ConsentStatus"/>,
/// <see cref="TransitionPolicy"/> evaluation, re-consent capture locks (PRD-SUB-007), K-TIME-6 inconsistency locks,
/// K-TIME-8 valid-time <c>GovernedBy</c> lineage, active study-site gates (PRD-SSU-003), and signed Kernel HTTP
/// governance endpoints. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelApplicabilityAndConsentPostgresTests(KernelPostgresFixture fixture)
    : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string SpecVitals = "cs.vitals";
    private const string VisitWeek12 = "act.week12";
    private const string VisitWeek24 = "act.week24";
    private const string VisitWeek36 = "act.week36";
    private const string ItemSystolic = "item.sbp";
    private const string ItemDiastolic = "item.dbp";
    private const string Reason = KernelProtocolScenario.Reason;

    private static readonly JsonSerializerOptions SnakeJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelProtocolScenario _protocol = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// Validates K-TIME-1 and K-TIME-6 at both database and command levels: period and pending-transition tables are
    /// append-only for every role including superuser (<c>KT001</c>), <c>btree_gist</c> exclusion constraints reject
    /// overlapping governing applicability or consent periods (<c>23P01</c>), and flagging an applicability
    /// inconsistency refuses new capture (<c>K.Observation.ApplicabilityInconsistent</c>) while permitting corrections
    /// until cleared.
    /// </summary>
    [Fact(DisplayName = "ApplicabilityAndConsentPeriods_AreAppendOnlyAndExcludeOverlappingGoverningVersions")]
    [KernelRule("K-TIME-1")]
    [KernelRule("K-TIME-6")]
    public async Task ApplicabilityAndConsentPeriodsAreAppendOnlyAndExcludeOverlappingGoverningVersions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var (tenant, study, sbp, dbp) = await NewStudyWithConceptsAsync(ct);
        var v1 = await ApproveAndMakeEffectiveAsync(tenant, study, DesignFor(sbp, dbp, VisitWeek12), null, null, ct);
        var site = await AddStudySiteAsync(tenant, study, "US", active: true, ct);
        var enrollment = await ScreenAndEnrollAsync(tenant, site, "SUBJ-K6", ct);

        var consentAt = Tick();
        var consent = await _protocol.OkAsync(new RecordConsentStatus(
            tenant,
            ConsentStatusId.New(),
            enrollment,
            v1,
            Signed: true,
            consentAt,
            ConsentBasisKind.ElectronicAttestation,
            AttestationId: null,
            ArtifactVersionId: null,
            [new ConsentScopeEntry(ConsentPurposes.MainStudy, ConsentDecision.Granted)],
            KernelProtocolScenario.Key(),
            "Initial consent signed"), ct);

        var v2 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24),
            v1,
            Amendment(TransitionPolicy.RequiresReconsent, "Add Week 24"),
            ct);

        await using var db = fixture.CreateContext(tenant);
        var applicability = await db.Applicabilities.AsNoTracking().SingleAsync(a => a.EnrollmentId == enrollment, ct);
        var participantId = applicability.ParticipantId;

        foreach (var statement in new[]
        {
            $"UPDATE kernel.applicability_periods SET basis = 'Rewritten' WHERE applicability_id = '{applicability.Id.Value}'",
            $"DELETE FROM kernel.applicability_periods WHERE applicability_id = '{applicability.Id.Value}'",
            $"UPDATE kernel.applicability_pending_transitions SET required_since = now() WHERE applicability_id = '{applicability.Id.Value}'",
            $"DELETE FROM kernel.applicability_pending_transitions WHERE applicability_id = '{applicability.Id.Value}'",
            $"UPDATE kernel.consent_status_periods SET signed = false WHERE consent_status_id = '{consent.ConsentStatusId.Value}'",
            $"DELETE FROM kernel.consent_status_periods WHERE consent_status_id = '{consent.ConsentStatusId.Value}'",
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement))
                .Which.SqlState.Should().Be(BitemporalSql.AppendOnlyViolationSqlState);
        }

        var overlapApplicability = () => ExecuteAsAdminAsync(
            $"""
            INSERT INTO kernel.applicability_periods (
                id, tenant_id, applicability_id, enrollment_id, study_id, participant_id, protocol_version_id, basis, valid_period, recorded_period)
            VALUES (
                '{Guid.CreateVersion7()}', '{tenant.Value}', '{applicability.Id.Value}', '{enrollment.Value}', '{study.Value}', '{participantId.Value}',
                '{v2.Value}', 'OverlapTest', tstzrange('{consentAt:O}', NULL, '[)'), tstzrange('{consentAt:O}', NULL, '[)'))
            """,
            ct);
        (await overlapApplicability.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);

        var overlapConsent = () => ExecuteAsAdminAsync(
            $"""
            INSERT INTO kernel.consent_status_periods (
                id, tenant_id, consent_status_id, enrollment_id, protocol_version_id, signed, basis_kind, scopes, valid_period, recorded_period)
            VALUES (
                '{Guid.CreateVersion7()}', '{tenant.Value}', '{consent.ConsentStatusId.Value}', '{enrollment.Value}',
                '{v1.Value}', true, 'ElectronicAttestation', '[]'::jsonb, tstzrange('{consentAt:O}', NULL, '[)'), tstzrange('{consentAt:O}', NULL, '[)'))
            """,
            ct);
        (await overlapConsent.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);

        // Resolve re-consent first so we can record a baseline observation, then flag K-TIME-6 inconsistency.
        await _protocol.OkAsync(new RecordConsentStatus(
            tenant,
            ConsentStatusId.New(),
            enrollment,
            v2,
            Signed: true,
            Tick(),
            ConsentBasisKind.ElectronicAttestation,
            AttestationId: null,
            ArtifactVersionId: null,
            [new ConsentScopeEntry(ConsentPurposes.MainStudy, ConsentDecision.Granted)],
            KernelProtocolScenario.Key(),
            "Re-consented to v2"), ct);

        var baselineObs = await _protocol.OkAsync(new RecordObservation(
            tenant,
            ObservationId.New(),
            enrollment,
            sbp,
            new QuantityValue(134m, "mm[Hg]"),
            Tick(),
            KernelProtocolScenario.Key(),
            "Baseline SBP"), ct);

        var flagged = await _protocol.OkAsync(new FlagApplicabilityInconsistency(
            tenant,
            enrollment,
            IsInconsistent: true,
            "Conflicting site/study protocol transition window under investigation",
            KernelProtocolScenario.Key(),
            "Flag K-TIME-6 inconsistency"), ct);
        flagged.IsInconsistent.Should().BeTrue();

        var blockedObs = await fixture.Sender.SendAsync(new RecordObservation(
            tenant,
            ObservationId.New(),
            enrollment,
            sbp,
            new QuantityValue(140m, "mm[Hg]"),
            Tick(),
            KernelProtocolScenario.Key(),
            "Attempted new observation while inconsistent"), cancellationToken: ct);
        blockedObs.Error!.Code.Should().Be(ObservationErrors.ApplicabilityInconsistent.Code);

        var blockedOpen = await fixture.Sender.SendAsync(new OpenCollectionInstance(
            tenant,
            CollectionInstanceId.New(),
            enrollment,
            SpecVitals,
            VisitWeek12,
            Tick(),
            KernelProtocolScenario.Key(),
            "Attempted form open while inconsistent"), cancellationToken: ct);
        blockedOpen.Error!.Code.Should().Be(ObservationErrors.ApplicabilityInconsistent.Code);

        // Corrections to existing observations remain permitted while K-TIME-6 is investigated.
        var corrected = await _protocol.OkAsync(new CorrectObservation(
            tenant,
            baselineObs.ObservationId,
            enrollment,
            new QuantityValue(128m, "mm[Hg]"),
            ExpectedVersion: 1,
            KernelProtocolScenario.Key(),
            "Source chart reads 128"), ct);
        corrected.Version.Should().Be(2);

        // Clearing the K-TIME-6 inconsistency restores new clinical capture.
        var cleared = await _protocol.OkAsync(new FlagApplicabilityInconsistency(
            tenant,
            enrollment,
            IsInconsistent: false,
            InconsistencyReason: null,
            KernelProtocolScenario.Key(),
            "Resolved applicability window"), ct);
        cleared.IsInconsistent.Should().BeFalse();

        var unlockedObs = await _protocol.OkAsync(new RecordObservation(
            tenant,
            ObservationId.New(),
            enrollment,
            sbp,
            new QuantityValue(126m, "mm[Hg]"),
            Tick(),
            KernelProtocolScenario.Key(),
            "Capture unlocked after K-TIME-6 resolution"), ct);
        unlockedObs.Version.Should().Be(1);
    }

    /// <summary>
    /// Validates <see cref="TransitionPolicy"/> (<c>NewParticipantsOnly</c>, <c>Automatic</c>, and <c>ByCohort</c>)
    /// together with K-TIME-8 bitemporal <c>GovernedBy</c> lineage resolution at valid time (<c>CollectedAt</c> /
    /// <c>ObservedAt</c>), including back-dated observations recorded after an automatic amendment cutover.
    /// </summary>
    [Fact(DisplayName = "TransitionPolicies_EvaluateNewParticipantsOnlyAutomaticAndByCohort_StampingGovernedByFromBitemporalApplicability")]
    [KernelRule("K-TIME-8")]
    [GxPRequirement("PRD-SUB-007")]
    public async Task TransitionPoliciesEvaluateNewParticipantsOnlyAutomaticAndByCohortStampingGovernedByFromBitemporalApplicability()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var (tenant, study, sbp, dbp) = await NewStudyWithConceptsAsync(ct);
        var v1 = await ApproveAndMakeEffectiveAsync(tenant, study, DesignFor(sbp, dbp, VisitWeek12), null, null, ct);
        var site = await AddStudySiteAsync(tenant, study, "US", active: true, ct);

        var p1 = await ScreenAndEnrollAsync(tenant, site, "SUBJ-P1", ct);
        var duringV1 = Tick();

        // v2 is NewParticipantsOnly (e.g. imported protocol amendment): P1 stays on v1, new participant P2 gets v2.
        var v2 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24),
            v1,
            Amendment(TransitionPolicy.NewParticipantsOnly, "Imported amendment adding Week 24 for new participants"),
            ct);

        var p2 = await ScreenAndEnrollAsync(tenant, site, "SUBJ-P2", ct);
        var duringV2 = Tick();

        var nowAfterV2 = fixture.Clock.UtcNow;
        (await fixture.Applicability.ResolveAsync(tenant, p1, nowAfterV2, nowAfterV2, ct))
            .ProtocolVersionId.Should().Be(v1, "NewParticipantsOnly keeps pre-existing enrollments on v1");
        (await fixture.Applicability.ResolveAsync(tenant, p2, nowAfterV2, nowAfterV2, ct))
            .ProtocolVersionId.Should().Be(v2, "enrollments screened after v2 cutover start on v2");

        // P1 cannot open Week 24 (only in v2), while P2 can open Week 24 and gets GovernedBy = v2.
        var p1Week24 = await fixture.Sender.SendAsync(new OpenCollectionInstance(
            tenant, CollectionInstanceId.New(), p1, SpecVitals, VisitWeek24, Tick(), KernelProtocolScenario.Key(), "Attempt Week 24"), cancellationToken: ct);
        p1Week24.Error!.Code.Should().Be(CollectionErrors.SpecUnknown.Code);

        var p1Week12 = await _protocol.OkAsync(new OpenCollectionInstance(
            tenant, CollectionInstanceId.New(), p1, SpecVitals, VisitWeek12, Tick(), KernelProtocolScenario.Key(), "P1 Week 12"), ct);
        p1Week12.ProtocolVersionId.Should().Be(v1);

        var p2Week24 = await _protocol.OkAsync(new OpenCollectionInstance(
            tenant, CollectionInstanceId.New(), p2, SpecVitals, VisitWeek24, Tick(), KernelProtocolScenario.Key(), "P2 Week 24"), ct);
        p2Week24.ProtocolVersionId.Should().Be(v2);

        // v3 is Automatic: both P1 and P2 advance to v3 at cutover, while historical valid times retain v1 / v2 (K-TIME-8).
        var v3 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24, VisitWeek36),
            v2,
            Amendment(TransitionPolicy.Automatic, "Administrative clarification adding Week 36"),
            ct);
        var afterV3 = Tick();

        var backdatedP1Obs = await _protocol.OkAsync(new RecordObservation(
            tenant, ObservationId.New(), p1, sbp, new QuantityValue(118m, "mm[Hg]"), duringV1, KernelProtocolScenario.Key(), "Late entry from v1 visit"), ct);
        var backdatedP2Obs = await _protocol.OkAsync(new RecordObservation(
            tenant, ObservationId.New(), p2, sbp, new QuantityValue(121m, "mm[Hg]"), duringV2, KernelProtocolScenario.Key(), "Late entry from v2 visit"), ct);
        var currentP1Obs = await _protocol.OkAsync(new RecordObservation(
            tenant, ObservationId.New(), p1, sbp, new QuantityValue(124m, "mm[Hg]"), afterV3, KernelProtocolScenario.Key(), "Current entry under v3"), ct);

        await using (var db = fixture.CreateContext(tenant))
        {
            var obsEdges = await db.LineageEdges.AsNoTracking()
                .Where(e => e.Kind == LineageEdgeKind.GovernedBy && e.ToKind == NodeKinds.Observation)
                .ToListAsync(ct);

            obsEdges.Should().ContainSingle(e => e.ToId == backdatedP1Obs.ObservationId.Value)
                .Which.FromId.Should().Be(v1.Value, "K-TIME-8 resolves governing version at valid time duringV1");
            obsEdges.Should().ContainSingle(e => e.ToId == backdatedP2Obs.ObservationId.Value)
                .Which.FromId.Should().Be(v2.Value, "K-TIME-8 resolves governing version at valid time duringV2");
            obsEdges.Should().ContainSingle(e => e.ToId == currentP1Obs.ObservationId.Value)
                .Which.FromId.Should().Be(v3.Value, "K-TIME-8 resolves governing version at valid time afterV3");
        }

        // v4 is ByCohort: subject=SUBJ-P1 transitions automatically, default cohort requires re-consent (holding P2 on v3).
        var byCohortAmendment = new ProtocolAmendment(
            "Cohort-specific transition",
            "Targeted cohort schedule update",
            AmendmentClassification.Substantial,
            "Advances SUBJ-P1 automatically and requires re-consent for others",
            [VisitWeek36],
            TransitionPolicy.ByCohort,
            CohortRules: [new CohortTransitionRule("subject=SUBJ-P1", TransitionPolicy.Automatic)],
            DefaultCohortPolicy: TransitionPolicy.RequiresReconsent);

        var v4 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24, VisitWeek36),
            v3,
            byCohortAmendment,
            ct);

        var nowAfterV4 = fixture.Clock.UtcNow;
        var p1State = await fixture.Applicability.ResolveAsync(tenant, p1, nowAfterV4, nowAfterV4, ct);
        var p2State = await fixture.Applicability.ResolveAsync(tenant, p2, nowAfterV4, nowAfterV4, ct);

        p1State.ProtocolVersionId.Should().Be(v4);
        p1State.RequiresReconsent.Should().BeFalse();

        p2State.ProtocolVersionId.Should().Be(v3);
        p2State.RequiresReconsent.Should().BeTrue();
        p2State.PendingTransitions.Should().ContainSingle().Which.TargetVersionId.Should().Be(v4);
    }

    /// <summary>
    /// Validates PRD-SUB-007: when one or more <c>RequiresReconsent</c> amendments become effective, new form openings,
    /// new items in existing forms, and new standalone observations are refused with <c>K.Observation.ReconsentRequired</c>,
    /// whereas corrections to already-recorded values remain permitted; signing consent for the latest version resolves
    /// all covered pending transitions along the amendment chain and unlocks capture under the newly consented version.
    /// </summary>
    [Fact(DisplayName = "RequiresReconsent_BlocksNewCapturePermitsCorrections_AndMultiAmendmentConsentResolvesPendingTransitions")]
    [GxPRequirement("PRD-SUB-007")]
    [KernelRule("K-TIME-8")]
    public async Task RequiresReconsentBlocksNewCapturePermitsCorrectionsAndMultiAmendmentConsentResolvesPendingTransitions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var (tenant, study, sbp, dbp) = await NewStudyWithConceptsAsync(ct);
        var v1 = await ApproveAndMakeEffectiveAsync(tenant, study, DesignFor(sbp, dbp, VisitWeek12), null, null, ct);
        var site = await AddStudySiteAsync(tenant, study, "US", active: true, ct);
        var enrollment = await ScreenAndEnrollAsync(tenant, site, "SUBJ-RECON", ct);

        // Open Week 12 form on v1, record Systolic only (leaving Diastolic unrecorded), and record a standalone observation.
        var formId = CollectionInstanceId.New();
        await _protocol.OkAsync(new OpenCollectionInstance(
            tenant, formId, enrollment, SpecVitals, VisitWeek12, Tick(), KernelProtocolScenario.Key(), "Open v1 form"), ct);
        await _protocol.OkAsync(new RecordCollectedValues(
            tenant,
            formId,
            enrollment,
            [new CollectedValue(ItemSystolic, new QuantityValue(150m, "mm[Hg]"), ExpectedRevision: 0)],
            KernelProtocolScenario.Key(),
            "Record initial SBP"), ct);

        var standaloneObs = await _protocol.OkAsync(new RecordObservation(
            tenant, ObservationId.New(), enrollment, sbp, new QuantityValue(148m, "mm[Hg]"), Tick(), KernelProtocolScenario.Key(), "Standalone SBP"), ct);

        // Two sequential RequiresReconsent amendments (v2 and v3) become effective before the participant returns.
        var v2 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24),
            v1,
            Amendment(TransitionPolicy.RequiresReconsent, "Amendment v2"),
            ct);
        var v3 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24, VisitWeek36),
            v2,
            Amendment(TransitionPolicy.RequiresReconsent, "Amendment v3"),
            ct);

        var duringLock = fixture.Clock.UtcNow;
        var lockedState = await fixture.Applicability.ResolveAsync(tenant, enrollment, duringLock, duringLock, ct);
        lockedState.ProtocolVersionId.Should().Be(v1);
        lockedState.RequiresReconsent.Should().BeTrue();
        lockedState.PendingTransitions.Select(p => p.TargetVersionId).Should().Equal(v2, v3);

        // New form open, new standalone observation, and new item on an open form are all refused with 423 ReconsentRequired.
        (await fixture.Sender.SendAsync(new OpenCollectionInstance(
            tenant, CollectionInstanceId.New(), enrollment, SpecVitals, VisitWeek12, Tick(), KernelProtocolScenario.Key(), "Blocked open"), cancellationToken: ct))
            .Error!.Code.Should().Be(ObservationErrors.ReconsentRequired.Code);

        (await fixture.Sender.SendAsync(new RecordObservation(
            tenant, ObservationId.New(), enrollment, sbp, new QuantityValue(135m, "mm[Hg]"), Tick(), KernelProtocolScenario.Key(), "Blocked obs"), cancellationToken: ct))
            .Error!.Code.Should().Be(ObservationErrors.ReconsentRequired.Code);

        (await fixture.Sender.SendAsync(new RecordCollectedValues(
            tenant,
            formId,
            enrollment,
            [new CollectedValue(ItemDiastolic, new QuantityValue(88m, "mm[Hg]"), ExpectedRevision: 0)],
            KernelProtocolScenario.Key(),
            "Blocked new item on existing form"), cancellationToken: ct))
            .Error!.Code.Should().Be(ObservationErrors.ReconsentRequired.Code);

        // Corrections to previously recorded items and standalone observations remain permitted during re-consent lock.
        var correctedForm = await _protocol.OkAsync(new RecordCollectedValues(
            tenant,
            formId,
            enrollment,
            [new CollectedValue(ItemSystolic, new QuantityValue(142m, "mm[Hg]"), ExpectedRevision: 1)],
            KernelProtocolScenario.Key(),
            "Corrected SBP transcription error while awaiting re-consent"), ct);
        correctedForm.Values.Single(v => v.ItemId == ItemSystolic).Revision.Should().Be(2);

        var correctedStandalone = await _protocol.OkAsync(new CorrectObservation(
            tenant,
            standaloneObs.ObservationId,
            enrollment,
            new QuantityValue(140m, "mm[Hg]"),
            ExpectedVersion: 1,
            KernelProtocolScenario.Key(),
            "Corrected standalone SBP while awaiting re-consent"), ct);
        correctedStandalone.Version.Should().Be(2);

        // Signing consent directly to v3 resolves both v2 and v3 pending transitions and unlocks v3 schedule capture.
        var signedAt = Tick();
        var consentState = await _protocol.OkAsync(new RecordConsentStatus(
            tenant,
            ConsentStatusId.New(),
            enrollment,
            v3,
            Signed: true,
            signedAt,
            ConsentBasisKind.PaperSourceDocument,
            AttestationId: null,
            ArtifactVersionId: null,
            [new ConsentScopeEntry(ConsentPurposes.MainStudy, ConsentDecision.Granted)],
            KernelProtocolScenario.Key(),
            "Signed paper ICF for protocol v3"), ct);

        consentState.Applicability!.GoverningVersionId.Should().Be(v3);
        consentState.Applicability.PendingTransitions.Should().BeEmpty();

        var unlockedWeek36 = await _protocol.OkAsync(new OpenCollectionInstance(
            tenant, CollectionInstanceId.New(), enrollment, SpecVitals, VisitWeek36, Tick(), KernelProtocolScenario.Key(), "Open v3 Week 36"), ct);
        unlockedWeek36.ProtocolVersionId.Should().Be(v3);
    }

    /// <summary>
    /// Validates PRD-SSU-003 inside the Kernel: an unactivated (<c>Selected</c>) study site refuses screening,
    /// enrollment activation, and clinical data capture until <see cref="ActivateStudySite"/> marks it <c>Active</c>.
    /// </summary>
    [Fact(DisplayName = "UnactivatedStudySite_RefusesScreeningEnrollmentTransitionAndCapture")]
    [GxPRequirement("PRD-SSU-003")]
    public async Task UnactivatedStudySiteRefusesScreeningEnrollmentTransitionAndCapture()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var (tenant, study, sbp, dbp) = await NewStudyWithConceptsAsync(ct);
        await ApproveAndMakeEffectiveAsync(tenant, study, DesignFor(sbp, dbp, VisitWeek12), null, null, ct);
        var unactivatedSite = await AddStudySiteAsync(tenant, study, "US", active: false, ct);

        var participant = await _protocol.OkAsync(
            new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Candidate"), ct);
        SubjectCode.TryFrom("SUBJ-UNACT", out var subjectCode);

        var blockedScreen = await fixture.Sender.SendAsync(new ScreenParticipant(
            tenant, EnrollmentId.New(), participant, unactivatedSite, subjectCode, Tick(), KernelProtocolScenario.Key(), "Attempt screen before activation"),
            cancellationToken: ct);
        blockedScreen.Error.Should().Be(ExecutionErrors.StudySiteNotActive);

        // Seed a legacy Screening enrollment on the unactivated site to prove enrollment transition and capture also gate on Active.
        var seededAt = Tick();
        var seededEnrollment = Enrollment.Screen(
            EnrollmentId.New(), tenant, participant, study, unactivatedSite, subjectCode, seededAt, seededAt).Value!;
        seededEnrollment.CreatedAt = seededAt.UtcDateTime;
        seededEnrollment.CreatedBy = KernelPostgresFixture.ActorId;
        seededEnrollment.ReasonForChange = "Legacy import";
        await fixture.SeedAsync(seededEnrollment);

        var blockedEnroll = await fixture.Sender.SendAsync(new ChangeEnrollmentStatus(
            tenant, seededEnrollment.Id, EnrollmentStatus.Enrolled, Tick(), KernelProtocolScenario.Key(), "Attempt enroll before activation"),
            cancellationToken: ct);
        blockedEnroll.Error.Should().Be(ExecutionErrors.StudySiteNotActive);

        var blockedObs = await fixture.Sender.SendAsync(new RecordObservation(
            tenant, ObservationId.New(), seededEnrollment.Id, sbp, new QuantityValue(120m, "mm[Hg]"), Tick(), KernelProtocolScenario.Key(), "Attempt obs before activation"),
            cancellationToken: ct);
        blockedObs.Error!.Code.Should().Be(ObservationErrors.StudySiteNotActive.Code);

        // Activate the study site and verify screening, enrollment, and capture proceed.
        await _protocol.OkAsync(new ActivateStudySite(tenant, unactivatedSite, KernelProtocolScenario.Key(), "Greenlight verified"), ct);

        SubjectCode.TryFrom("SUBJ-ACT", out var activeSubjectCode);
        var activeParticipant = await _protocol.OkAsync(
            new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        var screened = await _protocol.OkAsync(new ScreenParticipant(
            tenant, EnrollmentId.New(), activeParticipant, unactivatedSite, activeSubjectCode, Tick(), KernelProtocolScenario.Key(), "Screened on active site"), ct);
        var enrolled = await _protocol.OkAsync(new ChangeEnrollmentStatus(
            tenant, screened.EnrollmentId, EnrollmentStatus.Enrolled, Tick(), KernelProtocolScenario.Key(), "Enrolled on active site"), ct);
        enrolled.Status.Should().Be(EnrollmentStatus.Enrolled);
    }

    /// <summary>
    /// Exercises the real <see cref="Cadence.Kernel.Host.Program"/> HTTP endpoints with gateway-signed identities:
    /// <c>POST /api/v1/kernel/effectivities</c> (refusing unapproved versions),
    /// <c>POST /api/v1/kernel/effectivities/{ref}/status</c>,
    /// <c>POST /api/v1/kernel/study-sites/activate</c> (and idempotent repeat),
    /// <c>POST /api/v1/kernel/enrollments/screen</c> (refusing unactivated sites),
    /// <c>GET /api/v1/kernel/enrollments/{ref}/schedule-of-activities</c>, and
    /// <c>POST /api/v1/kernel/consents/sync</c> as <c>svc:econsent-consumer</c>.
    /// </summary>
    [Fact(DisplayName = "HostGovernanceEndpoints_DeclareApproveActivateSyncConsentAndReadEnrollmentSchedule")]
    [GxPRequirement("PRD-SUB-007")]
    [GxPRequirement("PRD-SSU-003")]
    public async Task HostGovernanceEndpointsDeclareApproveActivateSyncConsentAndReadEnrollmentSchedule()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenantCode = $"T-GOV-{suffix}";
        var studyCode = $"STD-{suffix}";
        var siteCode = $"SITE-{suffix}";
        const string Admin = "study-lead@governance-host.test";
        const string EconsentSvc = "svc:econsent-consumer";

        var tenant = TenantId.New();
        await _protocol.OkAsync(new ProvisionTenant(tenant, tenantCode, "Governance host tenant", KernelProtocolScenario.Key(), Reason), ct);
        var sponsor = await _protocol.OkAsync(
            new RegisterOrganization(tenant, OrganizationId.New(), $"ORG-{suffix}", "Sponsor", OrganizationKind.Sponsor, KernelProtocolScenario.Key(), Reason), ct);
        var study = await _protocol.OkAsync(
            new RegisterStudy(tenant, StudyId.New(), sponsor, studyCode, "Bitemporal Governance Trial", "Phase 2", KernelProtocolScenario.Key(), Reason), ct);

        var site = new Site(SiteId.New(), tenant, sponsor, siteCode, "Boston General", "US", KernelPostgresFixture.ActorId, Reason);
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, Reason);
        await fixture.SeedAsync(site, studySite);

        foreach (var capability in new[]
        {
            "effectivity.declare",
            "effectivity.change_status",
            "study_site.activate",
            "participant.register",
            "participant.screen",
            "enrollment.change_status",
            "consent.record",
        })
        {
            await _protocol.OkAsync(new IssueGrant(
                tenant, GrantId.New(), Actor.Person(Admin), capability, null, AuthorityScope.Tenant(), Tick(), null, false,
                KernelProtocolScenario.Key(), "Granted study lead"), ct);
        }

        foreach (var capability in new[] { "participant.register", "participant.screen", "consent.record" })
        {
            await _protocol.OkAsync(new IssueGrant(
                tenant, GrantId.New(), Actor.ServiceAccount("econsent-consumer"), capability, null, AuthorityScope.Tenant(), Tick(), null, false,
                KernelProtocolScenario.Key(), "Granted econsent consumer"), ct);
        }

        var sbp = await DefineConceptAsync(tenant, "Systolic blood pressure", ct);
        var dbp = await DefineConceptAsync(tenant, "Diastolic blood pressure", ct);

        // Draft version must be rejected by POST /api/v1/kernel/effectivities with K.Effectivity.ProtocolVersionNotApproved.
        var draftId = ProtocolVersionId.New();
        await _protocol.CreateDraftAsync(tenant, study, draftId, ct, ProtocolDesignDocument.Write(DesignFor(sbp, dbp, VisitWeek12)));

        using var adminClient = Client(tenantCode, Admin);
        using var econsentClient = Client(tenantCode, EconsentSvc);

        var rejectedDraft = await ProblemAsync(adminClient, "effectivities", "Attempt unapproved declare", new
        {
            protocol_version_ref = NodeRef.For(draftId).ToString(),
            scope_kind = "Study",
            valid_from = Tick(),
        }, ct);
        rejectedDraft.Should().Be((HttpStatusCode.Conflict, EffectivityErrors.ProtocolVersionNotApproved.Code));

        // Approve v1, declare effectivity via HTTP, and transition to Effective via HTTP.
        var inReview = await _protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, draftId, KernelProtocolScenario.Key(), Reason), ct);
        var sig = await _protocol.OkAsync(_protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _protocol.OkAsync(new ApproveProtocolVersion(tenant, draftId, sig, KernelProtocolScenario.Key(), Reason), ct);
        var ethics = await RegisterEthicsApprovalAsync(tenant, study, ct);

        var declared = await SendJsonAsync<KernelEffectivityStateResponse>(adminClient, HttpMethod.Post, "effectivities", "Declare v1 study-wide", new
        {
            protocol_version_ref = NodeRef.For(draftId).ToString(),
            scope_kind = "Study",
            valid_from = Tick(),
        }, HttpStatusCode.Created, ct);
        declared.Status.Should().Be("Pending");

        var effective = await SendJsonAsync<KernelEffectivityStateResponse>(adminClient, HttpMethod.Post, $"effectivities/{declared.Ref}/status", "Ethics approved", new
        {
            target_status = "Effective",
            effective_at = Tick(),
            evidence_artifact_version_refs = new[] { NodeRef.For(ethics).ToString() },
        }, HttpStatusCode.OK, ct);
        effective.Status.Should().Be("Effective");

        // Screening before site activation is refused over HTTP with K.Execution.StudySiteNotActive.
        var blockedScreen = await ProblemAsync(adminClient, "enrollments/screen", "Attempt screen before greenlight", new
        {
            study_code = studyCode,
            site_code = siteCode,
            subject_code = "SUBJ-HTTP-1",
            screened_at = Tick(),
        }, ct);
        blockedScreen.Should().Be((HttpStatusCode.Conflict, ExecutionErrors.StudySiteNotActive.Code));

        // Activate site by study/site code (and verify idempotent second call succeeds).
        var activated = await SendJsonAsync<KernelStudySiteStateResponse>(adminClient, HttpMethod.Post, "study-sites/activate", "SSU greenlight verified", new
        {
            study_code = studyCode,
            site_code = siteCode,
        }, HttpStatusCode.OK, ct);
        activated.Status.Should().Be(ActivateStudySiteHandler.ActiveStatus);

        var idempotentRepeat = await SendJsonAsync<KernelStudySiteStateResponse>(adminClient, HttpMethod.Post, $"study-sites/{activated.Ref}/activate", "Idempotent retry", null, HttpStatusCode.OK, ct);
        idempotentRepeat.Status.Should().Be(ActivateStudySiteHandler.ActiveStatus);

        // Screen participant on active site.
        var screened = await SendJsonAsync<KernelEnrollmentStateResponse>(adminClient, HttpMethod.Post, "enrollments/screen", "Screen SUBJ-HTTP-1", new
        {
            study_code = studyCode,
            site_code = siteCode,
            subject_code = "SUBJ-HTTP-1",
            screened_at = Tick(),
        }, HttpStatusCode.Created, ct);

        // Fork & make effective a RequiresReconsent v2 amendment adding Week 24.
        var v2 = await ApproveAndMakeEffectiveAsync(
            tenant,
            study,
            DesignFor(sbp, dbp, VisitWeek12, VisitWeek24),
            draftId,
            Amendment(TransitionPolicy.RequiresReconsent, "Amendment v2 requiring re-consent"),
            ct);

        var lockedSchedule = await SendJsonAsync<KernelEnrollmentScheduleResponse>(
            adminClient, HttpMethod.Get, $"enrollments/{screened.Ref}/schedule-of-activities", null, null, HttpStatusCode.OK, ct);
        lockedSchedule.GoverningVersion!.Ref.Should().Be(NodeRef.For(draftId).ToString());
        lockedSchedule.RequiresReconsent.Should().BeTrue();
        lockedSchedule.PendingTransitions.Should().ContainSingle().Which.TargetVersionRef.Should().Be(NodeRef.For(v2).ToString());
        lockedSchedule.ScheduleOfActivities!.Columns.Select(c => c.ActivityId).Should().Equal(VisitWeek12);

        // Synchronize eConsent via svc:econsent-consumer -> clears re-consent lock and advances SOA to v2!
        var syncedConsent = await SendJsonAsync<KernelConsentStatusStateResponse>(econsentClient, HttpMethod.Post, "consents/sync", "eConsent signed for v2", new
        {
            study_code = studyCode,
            site_code = siteCode,
            subject_code = "SUBJ-HTTP-1",
            protocol_version_ref = NodeRef.For(v2).ToString(),
            version_index = 2,
            version_tag = "v2.0",
            signed_at = Tick(),
            is_paper_override = false,
        }, HttpStatusCode.OK, ct);
        syncedConsent.RequiresReconsent.Should().BeFalse();
        syncedConsent.GoverningVersionRef.Should().Be(NodeRef.For(v2).ToString());

        var unlockedSchedule = await SendJsonAsync<KernelEnrollmentScheduleResponse>(
            adminClient, HttpMethod.Get, $"enrollments/{screened.Ref}/schedule-of-activities", null, null, HttpStatusCode.OK, ct);
        unlockedSchedule.GoverningVersion!.Ref.Should().Be(NodeRef.For(v2).ToString());
        unlockedSchedule.RequiresReconsent.Should().BeFalse();
        unlockedSchedule.PendingTransitions.Should().BeEmpty();
        unlockedSchedule.ScheduleOfActivities!.Columns.Select(c => c.ActivityId).Should().Equal(VisitWeek12, VisitWeek24);

        await using var db = fixture.CreateContext(tenant);
        var consentAudit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(ConsentStatus) && a.AggregateId == syncedConsent.ConsentStatusId.ToString())
            .SingleAsync(ct);
        consentAudit.ActorId.Should().Be(EconsentSvc);
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task<(TenantId Tenant, StudyId Study, ConceptRef Sbp, ConceptRef Dbp)> NewStudyWithConceptsAsync(CancellationToken ct)
    {
        var (tenant, study) = await _protocol.NewStudyAsync(ct);
        var sbp = await DefineConceptAsync(tenant, "Systolic blood pressure", ct);
        var dbp = await DefineConceptAsync(tenant, "Diastolic blood pressure", ct);
        return (tenant, study, sbp, dbp);
    }

    private Task<ConceptRef> DefineConceptAsync(TenantId tenant, string label, CancellationToken ct) =>
        _protocol.OkAsync(new DefineConcept(
            tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, label, $"{label} definition.", KernelProtocolScenario.Key(), Reason), ct);

    private static ProtocolDesign DesignFor(ConceptRef sbp, ConceptRef dbp, params string[] visits)
    {
        var planned = new List<PlannedActivity>(visits.Length);
        for (var idx = 0; idx < visits.Length; idx++)
        {
            var id = visits[idx];
            planned.Add(new PlannedActivity(id, $"Visit {id}", "ep.treatment", [ActivityLocation.Site], [SpecVitals], new ActivityTiming((idx + 1) * 84, 3, 3)));
        }

        return ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities = planned,
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecVitals,
                    "Vital signs",
                    [
                        new CollectionItem(ItemSystolic, sbp, "mm[Hg]", Required: true),
                        new CollectionItem(ItemDiastolic, dbp, "mm[Hg]", Required: false),
                    ]),
            ],
        };
    }

    private static ProtocolAmendment Amendment(TransitionPolicy policy, string intent) =>
        new(
            intent,
            "Clinical governance update",
            AmendmentClassification.Substantial,
            intent,
            [SpecVitals],
            policy);

    private async Task<ProtocolVersionId> ApproveAndMakeEffectiveAsync(
        TenantId tenant,
        StudyId study,
        ProtocolDesign design,
        ProtocolVersionId? parentVersionId,
        ProtocolAmendment? amendment,
        CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        var json = ProtocolDesignDocument.Write(design);
        if (parentVersionId is null)
        {
            var created = await _protocol.CreateDraftAsync(tenant, study, id, ct, json);
            created.IsSuccess.Should().BeTrue(created.Error?.Message);
        }
        else
        {
            await _protocol.OkAsync(new ForkProtocolVersion(tenant, parentVersionId.Value, id, amendment!, KernelProtocolScenario.Key(), Reason), ct);
            await _protocol.OkAsync(new ReviseProtocolDraft(tenant, id, json, amendment, KernelProtocolScenario.Key(), Reason), ct);
        }

        var inReview = await _protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), Reason), ct);
        var attestation = await _protocol.OkAsync(_protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _protocol.OkAsync(new ApproveProtocolVersion(tenant, id, attestation, KernelProtocolScenario.Key(), Reason), ct);

        var effectivity = EffectivityId.New();
        await _protocol.OkAsync(new DeclareEffectivity(tenant, effectivity, id, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), Reason), ct);
        var ethics = await RegisterEthicsApprovalAsync(tenant, study, ct);
        await _protocol.OkAsync(new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), [ethics], KernelProtocolScenario.Key(), Reason), ct);
        return id;
    }

    private async Task<ArtifactVersionId> RegisterEthicsApprovalAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var bytes = $"ethics-{Guid.NewGuid():N}";
        var version = ArtifactVersionId.New();
        var content = new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/pdf", $"blob://evidence/{bytes}");
        await _protocol.OkAsync(new RegisterArtifact(tenant, ArtifactId.New(), version, ArtifactKind.EthicsApproval, study, "IRB Approval", content, KernelProtocolScenario.Key(), Reason), ct);
        return version;
    }

    private async Task<StudySiteId> AddStudySiteAsync(TenantId tenant, StudyId study, string country, bool active, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", $"Site {suffix}", country, KernelPostgresFixture.ActorId, Reason);
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, active ? ActivateStudySiteHandler.ActiveStatus : "Selected", KernelPostgresFixture.ActorId, Reason);
        await fixture.SeedAsync(site, studySite);
        return studySite.Id;
    }

    private async Task<EnrollmentId> ScreenAndEnrollAsync(TenantId tenant, StudySiteId studySite, string code, CancellationToken ct)
    {
        var participant = await _protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), Reason), ct);
        SubjectCode.TryFrom(code, out var subjectCode);
        var screened = await _protocol.OkAsync(new ScreenParticipant(
            tenant, EnrollmentId.New(), participant, studySite, subjectCode, Tick(), KernelProtocolScenario.Key(), Reason), ct);
        await _protocol.OkAsync(new ChangeEnrollmentStatus(
            tenant, screened.EnrollmentId, EnrollmentStatus.Enrolled, Tick(), KernelProtocolScenario.Key(), Reason), ct);
        return screened.EnrollmentId;
    }

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<Cadence.Kernel.Time.IClock>(fixture.Clock);
                services.AddDeclaredKernelErrorsCheck();
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendJsonAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var request = BuildRequest(method, path, reason, body);
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, SnakeJson)!;
    }

    private static async Task<(HttpStatusCode Status, string? Code)> ProblemAsync(
        HttpClient client, string path, string? reason, object? body, CancellationToken ct)
    {
        using var request = BuildRequest(HttpMethod.Post, path, reason, body);
        using var response = await client.SendAsync(request, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return (response.StatusCode, doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"/api/v1/kernel/{path}", UriKind.Relative));
        request.Headers.Add(KernelEffectivityEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
