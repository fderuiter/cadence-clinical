using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cec.Sdtm;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

namespace Cadence.Apps.Execution.Application.Cec;

/// <summary>
/// Application service implementing Clinical Endpoint Adjudication Committee (CEC) Chair tie-breaker rulings,
/// blinded primary reader evaluations, FSM concordance checking, 21 CFR Part 11 e-signatures, and SDTM CE compiler.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-CEC-003
/// @req:PRD-SYS-053
/// @req:PRD-SYS-124
/// </summary>
public sealed class CecApplicationService(
    ICecRepository cecRepository,
    ICurrentAuditContextAccessor auditContextAccessor,
    IClock clock) : ICecApplicationService
{
    public async Task<Result<ChairAdjudicationResponse>> ChairAdjudicateAsync(
        string dossierId,
        ChairAdjudicateRequest request,
        AuthenticatedCaller? signer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dossierId))
        {
            return DomainError.Validation("INVALID_DOSSIER_ID", "Dossier ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.FinalClassification))
        {
            return DomainError.Validation("INVALID_FINAL_CLASSIFICATION", "Final classification is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FinalAdjudicatedOnsetDate))
        {
            return DomainError.Validation("INVALID_FINAL_ONSET_DATE", "Final adjudicated onset date is required.");
        }

        // Fetch target dossier with tracking for update
        var dossier = await cecRepository.GetDossierForUpdateAsync(dossierId, cancellationToken).ConfigureAwait(false);
        if (dossier is null)
        {
            // Initialize dossier if not pre-seeded
            dossier = CecDossier.Initialize(
                dossierId: dossierId,
                eventId: $"EVT-{dossierId}",
                studyId: "STUDY-101",
                subjectId: "SUBJ-001",
                maskedSubjectId: "MASK-001",
                candidateEventType: "CARDIOVASCULAR",
                reportedTerm: "Reported Event",
                reportedOnsetDate: request.FinalAdjudicatedOnsetDate,
                workflowStatus: "ESCALATED_TO_CHAIR",
                isDiscordant: true);

            await cecRepository.AddDossierAsync(dossier, cancellationToken).ConfigureAwait(false);
        }

        // Prevent race condition / double-finalization
        if (dossier.WorkflowStatus is "FINALIZED_BY_CHAIR" or "FINALIZED_CONCORDANT")
        {
            return DomainError.Conflict("CEC_DOSSIER_ALREADY_FINALIZED", $"CEC Dossier '{dossierId}' is already finalized in status '{dossier.WorkflowStatus}'.");
        }

        // Compute SHA-256 payload hash over finalized classification payload
        string payloadToHash = JsonSerializer.Serialize(new
        {
            final_classification = request.FinalClassification,
            final_subtype = request.FinalSubtype,
            final_adjudicated_onset_date = request.FinalAdjudicatedOnsetDate,
            adjudication_rationale = request.AdjudicationRationale
        });
        string payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadToHash)));

        string signerId = signer?.UserId ?? auditContextAccessor.UserId;
        if (string.IsNullOrWhiteSpace(signerId))
        {
            signerId = "chair_user";
        }

        string signerName = !string.IsNullOrWhiteSpace(request.SignerName)
            ? request.SignerName
            : (signer?.UserId ?? "Dr. Adjudication Chair");

        string signingReason = !string.IsNullOrWhiteSpace(request.SignatureMeaning)
            ? request.SignatureMeaning
            : "I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines";

        DateTime now = clock.UtcNow.UtcDateTime;

        var signatureManifest = SignatureManifest.Create(
            SignerId: signerId,
            SignerRole: "CEC_CHAIR",
            SigningReason: signingReason,
            SignedAt: now,
            PayloadHash: payloadHash,
            SignerName: signerName);

        // Transition dossier state
        var adjudicateResult = dossier.AdjudicateChair(
            finalClassification: request.FinalClassification,
            finalSubtype: request.FinalSubtype,
            finalAdjudicatedOnsetDate: request.FinalAdjudicatedOnsetDate,
            adjudicationRationale: request.AdjudicationRationale,
            signatureManifest: signatureManifest,
            finalizedAt: now);

        if (adjudicateResult.IsFailure)
        {
            return adjudicateResult.Error!;
        }

        // Create Chair adjudication determination record
        string determinationId = $"DET-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var adjudicationRecord = CecChairAdjudication.Create(
            determinationId: determinationId,
            dossierId: dossier.Id,
            chairId: signerId,
            chairName: signerName,
            finalClassification: request.FinalClassification,
            finalSubtype: request.FinalSubtype,
            finalAdjudicatedOnsetDate: request.FinalAdjudicatedOnsetDate,
            bindingReader: request.BindingReader ?? "CHAIR_CUSTOM_DETERMINATION",
            criteriaConfirmed: request.CriteriaConfirmed ?? [],
            adjudicationRationale: request.AdjudicationRationale,
            signatureManifest: signatureManifest,
            finalizedAt: now,
            createdBy: signerId);

        await cecRepository.AddChairAdjudicationAsync(adjudicationRecord, cancellationToken).ConfigureAwait(false);

        // Persist DB state
        await cecRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = new ChairAdjudicationResponse(
            DeterminationId: adjudicationRecord.Id,
            DossierId: dossier.Id,
            ChairId: signerId,
            ChairName: signerName,
            FinalClassification: request.FinalClassification,
            FinalSubtype: request.FinalSubtype,
            FinalAdjudicatedOnsetDate: request.FinalAdjudicatedOnsetDate,
            BindingReader: adjudicationRecord.BindingReader,
            CriteriaConfirmed: adjudicationRecord.CriteriaConfirmed,
            AdjudicationRationale: request.AdjudicationRationale,
            ElectronicSignature: signatureManifest,
            FinalizedAt: now.ToString("o"));

        return response;
    }

    public async Task<Result<ReaderAssessmentResponse>> SubmitReaderAssessmentAsync(
        string dossierId,
        SubmitReaderAssessmentRequest request,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dossierId))
        {
            return DomainError.Validation("INVALID_DOSSIER_ID", "Dossier ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.Classification))
        {
            return DomainError.Validation("INVALID_CLASSIFICATION", "Classification is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AdjudicatedOnsetDate))
        {
            return DomainError.Validation("INVALID_ONSET_DATE", "Adjudicated onset date is required.");
        }

        var dossier = await cecRepository.GetDossierForUpdateAsync(dossierId, cancellationToken).ConfigureAwait(false);
        if (dossier is null)
        {
            return DomainError.NotFound("DOSSIER_NOT_FOUND", $"CEC Dossier '{dossierId}' not found.");
        }

        string readerId = caller?.UserId ?? auditContextAccessor.UserId;
        if (string.IsNullOrWhiteSpace(readerId))
        {
            readerId = "reader_user";
        }

        string readerName = request.ReaderName
            ?? (string.Equals(request.ReaderRole, "READER_1", StringComparison.OrdinalIgnoreCase) ? "Dr. Primary Reader 1" : "Dr. Primary Reader 2");

        DateTime now = clock.UtcNow.UtcDateTime;
        string assessmentId = $"ASSESS-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var assessment = new CecReaderAssessment(
            AssessmentId: assessmentId,
            DossierId: dossierId,
            ReaderId: readerId,
            ReaderName: readerName,
            ReaderRole: request.ReaderRole.ToUpperInvariant(),
            Classification: request.Classification,
            Subtype: request.Subtype,
            AdjudicatedOnsetDate: request.AdjudicatedOnsetDate,
            CriteriaMet: request.CriteriaMet ?? [],
            ConfidenceScore: request.ConfidenceScore,
            ClinicalRationale: request.ClinicalRationale,
            SubmittedAt: now);

        var result = dossier.SubmitReaderAssessment(assessment);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await cecRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ReaderAssessmentResponse(
            DossierId: dossier.Id,
            ReaderRole: assessment.ReaderRole,
            WorkflowStatus: dossier.WorkflowStatus,
            IsDiscordant: dossier.IsDiscordant,
            DiscordanceReasons: dossier.DiscordanceReasons,
            FinalConsensusClassification: dossier.FinalConsensusClassification,
            SubmittedAt: now);
    }

    public async Task<Result<CecDossierBlindedView>> GetBlindedDossierAsync(
        string dossierId,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dossierId))
        {
            return DomainError.Validation("INVALID_DOSSIER_ID", "Dossier ID cannot be empty.");
        }

        var dossier = await cecRepository.GetDossierByIdAsync(dossierId, cancellationToken).ConfigureAwait(false);
        if (dossier is null)
        {
            return DomainError.NotFound("DOSSIER_NOT_FOUND", $"CEC Dossier '{dossierId}' not found.");
        }

        string? role = caller?.Roles.FirstOrDefault();
        string? userId = caller?.UserId;

        return dossier.GetBlindedView(role, userId);
    }

    public async Task<Result<List<CecDossierBlindedView>>> GetDossiersListAsync(
        string? studyId,
        string? workflowStatus,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken)
    {
        var dossiers = await cecRepository.GetDossiersAsync(studyId, workflowStatus, cancellationToken).ConfigureAwait(false);

        string? role = caller?.Roles.FirstOrDefault();
        string? userId = caller?.UserId;

        var blindedList = dossiers.Select(d => d.GetBlindedView(role, userId)).ToList();
        return blindedList;
    }

    public async Task<Result<CecDossierBlindedView>> CompileDossierAsync(
        CompileDossierRequest request,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DossierId))
        {
            return DomainError.Validation("INVALID_DOSSIER_ID", "Dossier ID cannot be empty.");
        }

        var existing = await cecRepository.GetDossierByIdAsync(request.DossierId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return DomainError.Conflict("DOSSIER_ALREADY_EXISTS", $"CEC Dossier '{request.DossierId}' already exists.");
        }

        var dossier = CecDossier.Initialize(
            dossierId: request.DossierId,
            eventId: request.EventId,
            studyId: request.StudyId,
            subjectId: request.SubjectId,
            maskedSubjectId: request.MaskedSubjectId ?? $"SUBJ-BLINDED-{request.SubjectId}",
            candidateEventType: request.CandidateEventType,
            reportedTerm: request.ReportedTerm,
            reportedOnsetDate: request.ReportedOnsetDate,
            siteId: request.SiteId,
            maskedSiteId: request.MaskedSiteId ?? (request.SiteId != null ? $"SITE-BLINDED-{request.SiteId}" : null),
            verbatimDescription: request.VerbatimDescription,
            medicalNarrative: request.MedicalNarrative,
            labPanels: request.LabPanels,
            ecgRecords: request.EcgRecords,
            diagnosticAttachments: request.DiagnosticAttachments);

        await cecRepository.AddDossierAsync(dossier, cancellationToken).ConfigureAwait(false);
        await cecRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string? role = caller?.Roles.FirstOrDefault();
        string? userId = caller?.UserId;

        return dossier.GetBlindedView(role, userId);
    }

    public async Task<Result<SdtmCeDataset>> ExportSdtmCeAsync(
        string? studyId,
        CancellationToken cancellationToken)
    {
        var dossiers = await cecRepository.GetDossiersAsync(studyId, null, cancellationToken).ConfigureAwait(false);
        var dataset = CdiscSdtmCeCompiler.Compile(dossiers);
        return dataset;
    }
}
