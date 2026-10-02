using System;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Kernel.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Cec;

public sealed class CecDossierTests
{
    [Fact(DisplayName = "Initialize_CreatesDossierInPendingPrimaryReviewsStatus")]
    public void Initialize_CreatesDossierInPendingPrimaryReviewsStatus()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-001",
            eventId: "EVT-001",
            studyId: "STUDY-101",
            subjectId: "SUBJ-001",
            maskedSubjectId: "MASK-001",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        dossier.Id.Should().Be("DOSSIER-MACE-001");
        dossier.WorkflowStatus.Should().Be("PENDING_PRIMARY_REVIEWS");
        dossier.IsDiscordant.Should().BeFalse();
        dossier.SignatureManifest.Should().BeNull();
    }

    [Fact(DisplayName = "AdjudicateChair_TransitionsStatusToFinalizedByChairAndAttachesManifest")]
    public void AdjudicateChair_TransitionsStatusToFinalizedByChairAndAttachesManifest()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-001",
            eventId: "EVT-001",
            studyId: "STUDY-101",
            subjectId: "SUBJ-001",
            maskedSubjectId: "MASK-001",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10",
            workflowStatus: "ESCALATED_TO_CHAIR",
            isDiscordant: true);

        var manifest = SignatureManifest.Create(
            SignerId: "chair-user-01",
            SignerRole: "CEC_CHAIR",
            SigningReason: "I confirm this clinical endpoint adjudication determination is final",
            SignedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc),
            PayloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", // pragma: allowlist secret
            SignerName: "Dr. Adjudication Chair");

        var result = dossier.AdjudicateChair(
            finalClassification: "NON_ST_ELEVATION_MI",
            finalSubtype: "TYPE_1_SPONTANEOUS",
            finalAdjudicatedOnsetDate: "2026-02-10",
            adjudicationRationale: "ECG and troponin rise confirm NSTEMI",
            signatureManifest: manifest,
            finalizedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));

        result.IsSuccess.Should().BeTrue();
        dossier.WorkflowStatus.Should().Be("FINALIZED_BY_CHAIR");
        dossier.IsDiscordant.Should().BeFalse();
        dossier.FinalConsensusClassification.Should().Be("NON_ST_ELEVATION_MI");
        dossier.FinalConsensusSubtype.Should().Be("TYPE_1_SPONTANEOUS");
        dossier.SignatureManifest.Should().NotBeNull();
        dossier.SignatureManifest!.SignerId.Should().Be("chair-user-01");
    }

    [Fact(DisplayName = "AdjudicateChair_WhenAlreadyFinalized_ReturnsConflictError")]
    public void AdjudicateChair_WhenAlreadyFinalized_ReturnsConflictError()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-001",
            eventId: "EVT-001",
            studyId: "STUDY-101",
            subjectId: "SUBJ-001",
            maskedSubjectId: "MASK-001",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10",
            workflowStatus: "FINALIZED_BY_CHAIR",
            isDiscordant: false);

        var manifest = SignatureManifest.Create(
            SignerId: "chair-user-01",
            SignerRole: "CEC_CHAIR",
            SigningReason: "Sign-off",
            SignedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc),
            PayloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"); // pragma: allowlist secret

        var result = dossier.AdjudicateChair(
            finalClassification: "NON_ST_ELEVATION_MI",
            finalSubtype: null,
            finalAdjudicatedOnsetDate: "2026-02-10",
            adjudicationRationale: "Duplicate submission",
            signatureManifest: manifest,
            finalizedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CEC_DOSSIER_ALREADY_FINALIZED");
    }
}
