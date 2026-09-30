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
}
