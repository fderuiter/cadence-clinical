using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Application.Cec.Sdtm;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Cec;

public sealed class CecDossierTests
{
    [Fact(DisplayName = "Initialize_CreatesDossierInPendingPrimaryReviewsStatus")]
    [GxPRequirement("PRD-CEC-001")]
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

    [Fact(DisplayName = "Initialize_RedactsPhiInNarrativeAndAttachments")]
    [GxPRequirement("PRD-CEC-001")]
    public void Initialize_RedactsPhiInNarrativeAndAttachments()
    {
        var attachments = new List<CecDiagnosticAttachment>
        {
            new("ATT-01", "Angiogram", "Angiogram Report", "Evaluated by Dr. John Smith at Site SITE-101", ["Patient Alice Brown had 95% occlusion"])
        };

        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-002",
            eventId: "EVT-002",
            studyId: "STUDY-101",
            subjectId: "SUBJ-002",
            maskedSubjectId: "MASK-002",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10",
            verbatimDescription: "Patient John Doe evaluated by Dr. Robert Vance",
            medicalNarrative: "Patient presented with chest pain to Hospital SITE-101 under care of Dr. Sarah Connor",
            diagnosticAttachments: attachments);

        dossier.VerbatimDescription.Should().Contain("[REDACTED_PHYSICIAN]");
        dossier.MedicalNarrative.Should().Contain("[REDACTED_PHYSICIAN]");
        dossier.MedicalNarrative.Should().Contain("[REDACTED_SITE]");
        dossier.DiagnosticAttachments.Should().HaveCount(1);
        dossier.DiagnosticAttachments[0].Summary.Should().Contain("[REDACTED_PHYSICIAN]");
        dossier.DiagnosticAttachments[0].KeyFindings[0].Should().Contain("[REDACTED_PATIENT_NAME]");
    }

    [Fact(DisplayName = "SubmitReaderAssessment_WhenConcordant_TransitionsToFinalizedConcordant")]
    [GxPRequirement("PRD-CEC-002")]
    public void SubmitReaderAssessment_WhenConcordant_TransitionsToFinalizedConcordant()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-003",
            eventId: "EVT-003",
            studyId: "STUDY-101",
            subjectId: "SUBJ-003",
            maskedSubjectId: "MASK-003",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var now = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        var r1 = new CecReaderAssessment("A-1", dossier.Id, "reader-01", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Biomarker rise"], 5, "Clear STEMI", now);
        var r2 = new CecReaderAssessment("A-2", dossier.Id, "reader-02", "Dr. Reader 2", "READER_2", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Biomarker rise"], 5, "Concordant STEMI", now);

        dossier.SubmitReaderAssessment(r1).IsSuccess.Should().BeTrue();
        dossier.WorkflowStatus.Should().Be("READER_1_COMPLETED");

        dossier.SubmitReaderAssessment(r2).IsSuccess.Should().BeTrue();
        dossier.WorkflowStatus.Should().Be("FINALIZED_CONCORDANT");
        dossier.IsDiscordant.Should().BeFalse();
        dossier.FinalConsensusClassification.Should().Be("CONFIRMED_MI");
    }

    [Fact(DisplayName = "SubmitReaderAssessment_WhenDiscordant_TransitionsToEscalatedToChair")]
    [GxPRequirement("PRD-CEC-002")]
    public void SubmitReaderAssessment_WhenDiscordant_TransitionsToEscalatedToChair()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-004",
            eventId: "EVT-004",
            studyId: "STUDY-101",
            subjectId: "SUBJ-004",
            maskedSubjectId: "MASK-004",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var now = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        var r1 = new CecReaderAssessment("A-1", dossier.Id, "reader-01", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Biomarker rise"], 5, "Clear STEMI", now);
        var r2 = new CecReaderAssessment("A-2", dossier.Id, "reader-02", "Dr. Reader 2", "READER_2", "UNSTABLE_ANGINA", null, "2026-02-10", [], 3, "No troponin rise", now);

        dossier.SubmitReaderAssessment(r1);
        dossier.SubmitReaderAssessment(r2);

        dossier.WorkflowStatus.Should().Be("ESCALATED_TO_CHAIR");
        dossier.IsDiscordant.Should().BeTrue();
        dossier.DiscordanceReasons.Should().ContainSingle()
            .Which.Should().Contain("Reader 1 assessed [CONFIRMED_MI] vs Reader 2 assessed [UNSTABLE_ANGINA]");
    }

    [Fact(DisplayName = "GetBlindedView_StripsPeerAssessmentWhenInFlight")]
    [GxPRequirement("PRD-CEC-002")]
    public void GetBlindedView_StripsPeerAssessmentWhenInFlight()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-005",
            eventId: "EVT-005",
            studyId: "STUDY-101",
            subjectId: "SUBJ-005",
            maskedSubjectId: "MASK-005",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var now = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        var r1 = new CecReaderAssessment("A-1", dossier.Id, "reader-01", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Biomarker rise"], 5, "Clear STEMI", now);
        dossier.SubmitReaderAssessment(r1);

        // When Reader 2 requests the view while in-flight:
        var r2View = dossier.GetBlindedView("READER_2", "reader-02");
        r2View.Reader1Assessment.Should().BeNull();
        r2View.Reader1Status.Should().Be("BLINDED");

        // When Reader 1 requests the view:
        var r1View = dossier.GetBlindedView("READER_1", "reader-01");
        r1View.Reader1Assessment.Should().NotBeNull();
        r1View.Reader2Assessment.Should().BeNull();
    }

    [Fact(DisplayName = "CdiscSdtmCeCompiler_CompilesFinalizedAdjudicationsIntoValidSdtmCeRecords")]
    [GxPRequirement("PRD-CEC-003")]
    public void CdiscSdtmCeCompiler_CompilesFinalizedAdjudicationsIntoValidSdtmCeRecords()
    {
        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-006",
            eventId: "EVT-006",
            studyId: "STUDY-101",
            subjectId: "SUBJ-006",
            maskedSubjectId: "MASK-006",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Acute Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var now = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        var r1 = new CecReaderAssessment("A-1", dossier.Id, "reader-01", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Biomarker rise"], 5, "Clear STEMI", now);
        var r2 = new CecReaderAssessment("A-2", dossier.Id, "reader-02", "Dr. Reader 2", "READER_2", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Biomarker rise"], 5, "Concordant STEMI", now);

        dossier.SubmitReaderAssessment(r1);
        dossier.SubmitReaderAssessment(r2);

        var dataset = CdiscSdtmCeCompiler.Compile([dossier]);

        dataset.CeRecords.Should().HaveCount(1);
        var record = dataset.CeRecords[0];
        record.STUDYID.Should().Be("STUDY-101");
        record.DOMAIN.Should().Be("CE");
        record.USUBJID.Should().Be("STUDY-101-SUBJ-006");
        record.CESEQ.Should().Be(1);
        record.CETERM.Should().Be("Acute Myocardial Infarction");
        record.CEDECOD.Should().Be("CONFIRMED_MI");
        record.CECAT.Should().Be("CARDIOVASCULAR");
        record.CESCAT.Should().Be("TYPE_1");
        record.CEPRESP.Should().Be("Y");
        record.CEOCCUR.Should().Be("Y");
        record.CESTDTC.Should().Be("2026-02-10");
        record.CECADJ.Should().Be("FINALIZED");
    }

    [Fact(DisplayName = "AdjudicateChair_TransitionsStatusToFinalizedByChairAndAttachesManifest")]
    [GxPRequirement("PRD-CEC-002")]
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
    [GxPRequirement("PRD-CEC-002")]
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
