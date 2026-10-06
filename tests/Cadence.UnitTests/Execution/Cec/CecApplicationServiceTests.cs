using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cec;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Cec;

public sealed class CecApplicationServiceTests
{
    private readonly ICecRepository _cecRepository = Substitute.For<ICecRepository>();
    private readonly ICurrentAuditContextAccessor _auditContextAccessor = Substitute.For<ICurrentAuditContextAccessor>();
    private readonly IClock _clock = SystemClock.Instance;

    [Fact(DisplayName = "ChairAdjudicateAsync_GeneratesSignatureManifestWithSha256HashAndSavesDetermination")]
    [GxPRequirement("PRD-CEC-002")]
    public async Task ChairAdjudicateAsync_GeneratesSignatureManifestWithSha256HashAndSavesDetermination()
    {
        _auditContextAccessor.UserId.Returns("dr_chair_user");

        var service = new CecApplicationService(_cecRepository, _auditContextAccessor, _clock);

        var existingDossier = CecDossier.Initialize(
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

        _cecRepository.GetDossierForUpdateAsync("DOSSIER-MACE-001", Arg.Any<CancellationToken>())
            .Returns(existingDossier);

        var request = new ChairAdjudicateRequest(
            FinalClassification: "ISCHEMIC_STROKE",
            FinalSubtype: "THROMBOTIC",
            FinalAdjudicatedOnsetDate: "2026-02-10",
            BindingReader: "CHAIR_CUSTOM_DETERMINATION",
            CriteriaConfirmed: ["CT_SCAN_CONFIRMED"],
            AdjudicationRationale: "Definitive imaging evidence.",
            SignatureToken: "test-token",
            SignerName: "Dr. Adjudication Chair",
            Password: "Password123!",
            SignatureMeaning: "I confirm this clinical endpoint adjudication determination is final");

        var signer = new AuthenticatedCaller("dr_chair_user", ["CEC_CHAIR"]);

        var result = await service.ChairAdjudicateAsync("DOSSIER-MACE-001", request, signer, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;

        response.DossierId.Should().Be("DOSSIER-MACE-001");
        response.FinalClassification.Should().Be("ISCHEMIC_STROKE");
        response.ElectronicSignature.Should().NotBeNull();
        response.ElectronicSignature.SignerId.Should().Be("dr_chair_user");
        response.ElectronicSignature.SignerName.Should().Be("Dr. Adjudication Chair");
        response.ElectronicSignature.PayloadHash.Should().NotBeNullOrWhiteSpace();
        response.ElectronicSignature.PayloadHash.Length.Should().Be(64); // SHA-256 hex string length

        await _cecRepository.Received(1).AddChairAdjudicationAsync(
            Arg.Is<CecChairAdjudication>(a => a.DossierId == "DOSSIER-MACE-001" && a.FinalClassification == "ISCHEMIC_STROKE"),
            Arg.Any<CancellationToken>());

        await _cecRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ChairAdjudicateAsync_WhenDossierAlreadyFinalized_ReturnsConflict")]
    [GxPRequirement("PRD-CEC-002")]
    public async Task ChairAdjudicateAsync_WhenDossierAlreadyFinalized_ReturnsConflict()
    {
        _auditContextAccessor.UserId.Returns("dr_chair_user");

        var service = new CecApplicationService(_cecRepository, _auditContextAccessor, _clock);

        var finalizedDossier = CecDossier.Initialize(
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

        _cecRepository.GetDossierForUpdateAsync("DOSSIER-MACE-001", Arg.Any<CancellationToken>())
            .Returns(finalizedDossier);

        var request = new ChairAdjudicateRequest(
            FinalClassification: "ISCHEMIC_STROKE",
            FinalSubtype: null,
            FinalAdjudicatedOnsetDate: "2026-02-10");

        var result = await service.ChairAdjudicateAsync("DOSSIER-MACE-001", request, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CEC_DOSSIER_ALREADY_FINALIZED");
    }

    [Fact(DisplayName = "SubmitReaderAssessmentAsync_Concordant_FinalizesAndSaves")]
    [GxPRequirement("PRD-CEC-002")]
    public async Task SubmitReaderAssessmentAsync_Concordant_FinalizesAndSaves()
    {
        _auditContextAccessor.UserId.Returns("reader_02_id");

        var service = new CecApplicationService(_cecRepository, _auditContextAccessor, _clock);

        var existingDossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-002",
            eventId: "EVT-002",
            studyId: "STUDY-101",
            subjectId: "SUBJ-002",
            maskedSubjectId: "MASK-002",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var r1 = new CecReaderAssessment("A-1", existingDossier.Id, "reader_01_id", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Troponin elevated"], 5, "Clear STEMI", new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc));
        existingDossier.SubmitReaderAssessment(r1);

        _cecRepository.GetDossierForUpdateAsync("DOSSIER-MACE-002", Arg.Any<CancellationToken>())
            .Returns(existingDossier);

        var request = new SubmitReaderAssessmentRequest(
            ReaderRole: "READER_2",
            Classification: "CONFIRMED_MI",
            Subtype: "TYPE_1",
            AdjudicatedOnsetDate: "2026-02-10",
            CriteriaMet: ["Troponin elevated"],
            ConfidenceScore: 5,
            ClinicalRationale: "Concordant finding",
            ReaderName: "Dr. Reader 2");

        var caller = new AuthenticatedCaller("reader_02_id", ["CEC_READER"]);

        var result = await service.SubmitReaderAssessmentAsync("DOSSIER-MACE-002", request, caller, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkflowStatus.Should().Be("FINALIZED_CONCORDANT");
        result.Value.FinalConsensusClassification.Should().Be("CONFIRMED_MI");
        await _cecRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "GetBlindedDossierAsync_StripsPeerAssessmentsWhenInFlight")]
    [GxPRequirement("PRD-CEC-002")]
    public async Task GetBlindedDossierAsync_StripsPeerAssessmentsWhenInFlight()
    {
        var service = new CecApplicationService(_cecRepository, _auditContextAccessor, _clock);

        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-003",
            eventId: "EVT-003",
            studyId: "STUDY-101",
            subjectId: "SUBJ-003",
            maskedSubjectId: "MASK-003",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var r1 = new CecReaderAssessment("A-1", dossier.Id, "reader_01_id", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Troponin elevated"], 5, "Clear STEMI", new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc));
        dossier.SubmitReaderAssessment(r1);

        _cecRepository.GetDossierByIdAsync("DOSSIER-MACE-003", Arg.Any<CancellationToken>())
            .Returns(dossier);

        var caller = new AuthenticatedCaller("reader_02_id", ["CEC_READER"]);

        var result = await service.GetBlindedDossierAsync("DOSSIER-MACE-003", caller, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Reader1Assessment.Should().BeNull();
        result.Value.Reader1Status.Should().Be("BLINDED");
    }

    [Fact(DisplayName = "ExportSdtmCeAsync_ReturnsCompiledDataset")]
    [GxPRequirement("PRD-CEC-003")]
    public async Task ExportSdtmCeAsync_ReturnsCompiledDataset()
    {
        var service = new CecApplicationService(_cecRepository, _auditContextAccessor, _clock);

        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-004",
            eventId: "EVT-004",
            studyId: "STUDY-101",
            subjectId: "SUBJ-004",
            maskedSubjectId: "MASK-004",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        var r1 = new CecReaderAssessment("A-1", dossier.Id, "reader_01_id", "Dr. Reader 1", "READER_1", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Troponin elevated"], 5, "Clear STEMI", new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc));
        var r2 = new CecReaderAssessment("A-2", dossier.Id, "reader_02_id", "Dr. Reader 2", "READER_2", "CONFIRMED_MI", "TYPE_1", "2026-02-10", ["Troponin elevated"], 5, "Concordant", new DateTime(2026, 2, 10, 11, 0, 0, DateTimeKind.Utc));
        dossier.SubmitReaderAssessment(r1);
        dossier.SubmitReaderAssessment(r2);

        _cecRepository.GetDossiersAsync("STUDY-101", null, Arg.Any<CancellationToken>())
            .Returns([dossier]);

        var result = await service.ExportSdtmCeAsync("STUDY-101", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CeRecords.Should().HaveCount(1);
        result.Value.CeRecords[0].CEDECOD.Should().Be("CONFIRMED_MI");
    }

    [Fact(DisplayName = "CompileDossierAsync_AnonymizesAndCreatesDossier")]
    [GxPRequirement("PRD-CEC-001")]
    public async Task CompileDossierAsync_AnonymizesAndCreatesDossier()
    {
        var service = new CecApplicationService(_cecRepository, _auditContextAccessor, _clock);

        _cecRepository.GetDossierByIdAsync("DOSSIER-NEW-1", Arg.Any<CancellationToken>())
            .Returns((CecDossier?)null);

        var request = new CompileDossierRequest(
            DossierId: "DOSSIER-NEW-1",
            EventId: "EVT-100",
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-100",
            MaskedSubjectId: "MASK-100",
            SiteId: "SITE-01",
            MaskedSiteId: "MASK-SITE-01",
            CandidateEventType: "CARDIOVASCULAR",
            ReportedTerm: "Chest Pain",
            ReportedOnsetDate: "2026-02-10",
            VerbatimDescription: "Patient John Doe visited Dr. Alice Smith",
            MedicalNarrative: "Admitted to Hospital SITE-01 under Dr. Bob Vance",
            LabPanels: [],
            EcgRecords: [],
            DiagnosticAttachments: []);

        var caller = new AuthenticatedCaller("manager_01", ["CEC_MANAGER"]);

        var result = await service.CompileDossierAsync(request, caller, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VerbatimDescription.Should().Contain("[REDACTED_PHYSICIAN]");
        await _cecRepository.Received(1).AddDossierAsync(Arg.Any<CecDossier>(), Arg.Any<CancellationToken>());
    }
}
