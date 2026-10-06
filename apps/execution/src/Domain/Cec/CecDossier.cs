using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cec;

/// <summary>
/// DTO projection representing a role-blinded view of a CEC Dossier.
/// Prevents client data leaks by masking peer assessments during active 2-reader evaluations.
/// @req:PRD-CEC-002
/// </summary>
public sealed record CecDossierBlindedView(
    string DossierId,
    string EventId,
    string StudyId,
    string SubjectId,
    string MaskedSubjectId,
    string? SiteId,
    string? MaskedSiteId,
    string CandidateEventType,
    string ReportedTerm,
    string ReportedOnsetDate,
    string? VerbatimDescription,
    string? MedicalNarrative,
    bool IsDeidentified,
    string WorkflowStatus,
    bool IsDiscordant,
    List<string> DiscordanceReasons,
    string? FinalConsensusClassification,
    string? FinalConsensusSubtype,
    string? FinalAdjudicatedOnsetDate,
    string? AdjudicationRationale,
    string Reader1Status,
    string Reader2Status,
    CecReaderAssessment? Reader1Assessment,
    CecReaderAssessment? Reader2Assessment,
    List<CecLabPanel> LabPanels,
    List<CecEcgRecord> EcgRecords,
    List<CecDiagnosticAttachment> DiagnosticAttachments,
    SignatureManifest? SignatureManifest,
    DateTime? FinalizedAt);

/// <summary>
/// Aggregate root representing a Clinical Endpoint Adjudication Committee (CEC) dossier.
/// Governs blinded dual-reader assessments, concordance/discordance state transitions,
/// and Part 11 Chair tie-breaker finalization.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// @req:PRD-SYS-124
/// </summary>
public sealed class CecDossier : AggregateRoot<string>
{
    public string EventId { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string SubjectId { get; private set; } = string.Empty;
    public string MaskedSubjectId { get; private set; } = string.Empty;
    public string? SiteId { get; private set; }
    public string? MaskedSiteId { get; private set; }
    public string CandidateEventType { get; private set; } = string.Empty;
    public string ReportedTerm { get; private set; } = string.Empty;
    public string ReportedOnsetDate { get; private set; } = string.Empty;
    public string? VerbatimDescription { get; private set; }
    public string? MedicalNarrative { get; private set; }
    public bool IsDeidentified { get; private set; } = true;
    public string? DossierDigest { get; private set; }
    public string WorkflowStatus { get; private set; } = "PENDING_PRIMARY_REVIEWS";
    public bool IsDiscordant { get; private set; }
    public List<string> DiscordanceReasons { get; private set; } = [];
    public string? FinalConsensusClassification { get; private set; }
    public string? FinalConsensusSubtype { get; private set; }
    public string? FinalAdjudicatedOnsetDate { get; private set; }
    public string? AdjudicationRationale { get; private set; }

    public string Reader1Status { get; private set; } = "PENDING";
    public string? Reader1Id { get; private set; }
    public string? Reader1Name { get; private set; }
    public CecReaderAssessment? Reader1Assessment { get; private set; }

    public string Reader2Status { get; private set; } = "PENDING";
    public string? Reader2Id { get; private set; }
    public string? Reader2Name { get; private set; }
    public CecReaderAssessment? Reader2Assessment { get; private set; }

    public List<CecLabPanel> LabPanels { get; private set; } = [];
    public List<CecEcgRecord> EcgRecords { get; private set; } = [];
    public List<CecDiagnosticAttachment> DiagnosticAttachments { get; private set; } = [];

    public SignatureManifest? SignatureManifest { get; private set; }
    public DateTime? FinalizedAt { get; private set; }

    private CecDossier() { } // EF Core

    public static CecDossier Initialize(
        string dossierId,
        string eventId,
        string studyId,
        string subjectId,
        string maskedSubjectId,
        string candidateEventType,
        string reportedTerm,
        string reportedOnsetDate,
        string? siteId = null,
        string? maskedSiteId = null,
        string workflowStatus = "PENDING_PRIMARY_REVIEWS",
        bool isDiscordant = false,
        List<string>? discordanceReasons = null,
        string? verbatimDescription = null,
        string? medicalNarrative = null,
        List<CecLabPanel>? labPanels = null,
        List<CecEcgRecord>? ecgRecords = null,
        List<CecDiagnosticAttachment>? diagnosticAttachments = null,
        CecReaderAssessment? reader1Assessment = null,
        CecReaderAssessment? reader2Assessment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        string computedDigest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{dossierId}:{eventId}:{studyId}:{subjectId}:{candidateEventType}:{reportedTerm}:{reportedOnsetDate}")));

        string? maskedSite = maskedSiteId;
        if (string.IsNullOrWhiteSpace(maskedSite) && siteId != null)
        {
            maskedSite = $"SITE-BLINDED-{siteId}";
        }

        var dossier = new CecDossier
        {
            Id = dossierId,
            EventId = eventId,
            StudyId = studyId,
            SubjectId = subjectId,
            MaskedSubjectId = string.IsNullOrWhiteSpace(maskedSubjectId) ? $"SUBJ-BLINDED-{subjectId}" : maskedSubjectId,
            SiteId = siteId,
            MaskedSiteId = maskedSite,
            CandidateEventType = candidateEventType,
            ReportedTerm = reportedTerm,
            ReportedOnsetDate = reportedOnsetDate,
            VerbatimDescription = RedactPhiText(verbatimDescription),
            MedicalNarrative = RedactPhiText(medicalNarrative),
            IsDeidentified = true,
            DossierDigest = computedDigest,
            WorkflowStatus = workflowStatus,
            IsDiscordant = isDiscordant,
            DiscordanceReasons = discordanceReasons ?? [],
            LabPanels = labPanels ?? [],
            EcgRecords = ecgRecords ?? [],
            DiagnosticAttachments = (diagnosticAttachments ?? []).Select(AnonymizeAttachment).ToList(),
            Reader1Status = reader1Assessment != null ? "COMPLETED" : "PENDING",
            Reader1Id = reader1Assessment?.ReaderId,
            Reader1Name = reader1Assessment?.ReaderName,
            Reader1Assessment = reader1Assessment,
            Reader2Status = reader2Assessment != null ? "COMPLETED" : "PENDING",
            Reader2Id = reader2Assessment?.ReaderId,
            Reader2Name = reader2Assessment?.ReaderName,
            Reader2Assessment = reader2Assessment
        };

        // If initialized with two completed reader assessments, check concordance
        if (dossier.Reader1Assessment != null && dossier.Reader2Assessment != null)
        {
            dossier.EvaluateConcordance();
        }

        return dossier;
    }

    /// <summary>
    /// Processes a primary reader assessment submission (Reader 1 or Reader 2) and evaluates concordance.
    /// @req:PRD-CEC-002
    /// </summary>
    public Result<bool> SubmitReaderAssessment(CecReaderAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        if (WorkflowStatus is "FINALIZED_CONCORDANT" or "FINALIZED_BY_CHAIR")
        {
            return DomainError.Conflict("CEC_DOSSIER_ALREADY_FINALIZED", $"CEC Dossier '{Id}' is already finalized in status '{WorkflowStatus}'.");
        }

        bool isReader1 = string.Equals(assessment.ReaderRole, "READER_1", StringComparison.OrdinalIgnoreCase);
        bool isReader2 = string.Equals(assessment.ReaderRole, "READER_2", StringComparison.OrdinalIgnoreCase);

        if (!isReader1 && !isReader2)
        {
            return DomainError.Validation("INVALID_READER_ROLE", "Reader role must be either 'READER_1' or 'READER_2'.");
        }

        if (isReader1)
        {
            if (Reader1Status == "COMPLETED")
            {
                return DomainError.Conflict("READER_1_ALREADY_SUBMITTED", $"Reader 1 has already submitted an assessment for dossier '{Id}'.");
            }

            Reader1Status = "COMPLETED";
            Reader1Id = assessment.ReaderId;
            Reader1Name = assessment.ReaderName;
            Reader1Assessment = assessment;

            if (Reader2Status == "PENDING")
            {
                WorkflowStatus = "READER_1_COMPLETED";
            }
        }
        else if (isReader2)
        {
            if (Reader2Status == "COMPLETED")
            {
                return DomainError.Conflict("READER_2_ALREADY_SUBMITTED", $"Reader 2 has already submitted an assessment for dossier '{Id}'.");
            }

            Reader2Status = "COMPLETED";
            Reader2Id = assessment.ReaderId;
            Reader2Name = assessment.ReaderName;
            Reader2Assessment = assessment;

            if (Reader1Status == "PENDING")
            {
                WorkflowStatus = "READER_2_COMPLETED";
            }
        }

        if (Reader1Status == "COMPLETED" && Reader2Status == "COMPLETED")
        {
            EvaluateConcordance();
        }

        return true;
    }

    private void EvaluateConcordance()
    {
        if (Reader1Assessment is null || Reader2Assessment is null)
        {
            return;
        }

        bool classificationMatch = string.Equals(Reader1Assessment.Classification, Reader2Assessment.Classification, StringComparison.OrdinalIgnoreCase);

        if (classificationMatch)
        {
            WorkflowStatus = "FINALIZED_CONCORDANT";
            IsDiscordant = false;
            FinalConsensusClassification = Reader1Assessment.Classification;
            FinalConsensusSubtype = Reader1Assessment.Subtype ?? Reader2Assessment.Subtype;
            FinalAdjudicatedOnsetDate = Reader1Assessment.AdjudicatedOnsetDate;
            FinalizedAt = Reader1Assessment.SubmittedAt > Reader2Assessment.SubmittedAt ? Reader1Assessment.SubmittedAt : Reader2Assessment.SubmittedAt;
        }
        else
        {
            WorkflowStatus = "ESCALATED_TO_CHAIR";
            IsDiscordant = true;
            DiscordanceReasons =
            [
                $"Primary classification discordance: Reader 1 assessed [{Reader1Assessment.Classification}] vs Reader 2 assessed [{Reader2Assessment.Classification}]"
            ];
        }
    }

    /// <summary>
    /// Finalizes the dossier via Chair tie-breaker adjudication and attaches the Part 11 e-signature manifest.
    /// Rejects re-finalization attempts to prevent race conditions.
    /// @req:PRD-CEC-002
    /// </summary>
    public Result<bool> AdjudicateChair(
        string finalClassification,
        string? finalSubtype,
        string finalAdjudicatedOnsetDate,
        string adjudicationRationale,
        SignatureManifest signatureManifest,
        DateTime finalizedAt)
    {
        if (WorkflowStatus is "FINALIZED_BY_CHAIR" or "FINALIZED_CONCORDANT")
        {
            return DomainError.Conflict("CEC_DOSSIER_ALREADY_FINALIZED", $"CEC Dossier '{Id}' is already finalized in status '{WorkflowStatus}'.");
        }

        if (signatureManifest is null)
        {
            return DomainError.Validation("SIGNATURE_MANIFEST_REQUIRED", "A valid electronic signature manifest is required for chair adjudication.");
        }

        WorkflowStatus = "FINALIZED_BY_CHAIR";
        IsDiscordant = false;
        FinalConsensusClassification = finalClassification;
        FinalConsensusSubtype = finalSubtype;
        FinalAdjudicatedOnsetDate = finalAdjudicatedOnsetDate;
        AdjudicationRationale = adjudicationRationale;
        SignatureManifest = signatureManifest;
        FinalizedAt = finalizedAt;
        ReasonForChange = signatureManifest.SigningReason;

        return true;
    }

    /// <summary>
    /// Creates a role-blinded projection of the dossier.
    /// Peer primary assessments are hidden when in-flight to preserve reader blinding integrity.
    /// @req:PRD-CEC-002
    /// </summary>
    public CecDossierBlindedView GetBlindedView(string? requestingRole, string? requestingUserId)
    {
        bool isUnblindedByStatus = WorkflowStatus is "FINALIZED_CONCORDANT" or "FINALIZED_BY_CHAIR" or "ESCALATED_TO_CHAIR" or "DISCORDANCE_DETECTED";
        bool isChairOrManager = string.Equals(requestingRole, "CHAIR", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(requestingRole, "DATA_MANAGER", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(requestingRole, "AUDITOR", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(requestingRole, "CEC_CHAIR", StringComparison.OrdinalIgnoreCase);

        bool isReader1Role = string.Equals(requestingRole, "READER_1", StringComparison.OrdinalIgnoreCase);
        bool isReader1User = !string.IsNullOrEmpty(requestingUserId) && string.Equals(requestingUserId, Reader1Id, StringComparison.OrdinalIgnoreCase);
        bool showReader1 = isUnblindedByStatus || isChairOrManager || isReader1Role || isReader1User;

        bool isReader2Role = string.Equals(requestingRole, "READER_2", StringComparison.OrdinalIgnoreCase);
        bool isReader2User = !string.IsNullOrEmpty(requestingUserId) && string.Equals(requestingUserId, Reader2Id, StringComparison.OrdinalIgnoreCase);
        bool showReader2 = isUnblindedByStatus || isChairOrManager || isReader2Role || isReader2User;

        return new CecDossierBlindedView(
            DossierId: Id,
            EventId: EventId,
            StudyId: StudyId,
            SubjectId: SubjectId,
            MaskedSubjectId: MaskedSubjectId,
            SiteId: SiteId,
            MaskedSiteId: MaskedSiteId,
            CandidateEventType: CandidateEventType,
            ReportedTerm: ReportedTerm,
            ReportedOnsetDate: ReportedOnsetDate,
            VerbatimDescription: VerbatimDescription,
            MedicalNarrative: MedicalNarrative,
            IsDeidentified: IsDeidentified,
            WorkflowStatus: WorkflowStatus,
            IsDiscordant: IsDiscordant,
            DiscordanceReasons: DiscordanceReasons,
            FinalConsensusClassification: FinalConsensusClassification,
            FinalConsensusSubtype: FinalConsensusSubtype,
            FinalAdjudicatedOnsetDate: FinalAdjudicatedOnsetDate,
            AdjudicationRationale: AdjudicationRationale,
            Reader1Status: showReader1 ? Reader1Status : "BLINDED",
            Reader2Status: showReader2 ? Reader2Status : "BLINDED",
            Reader1Assessment: showReader1 ? Reader1Assessment : null,
            Reader2Assessment: showReader2 ? Reader2Assessment : null,
            LabPanels: LabPanels,
            EcgRecords: EcgRecords,
            DiagnosticAttachments: DiagnosticAttachments,
            SignatureManifest: SignatureManifest,
            FinalizedAt: FinalizedAt);
    }

    private static string? RedactPhiText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        string result = input;
        result = Regex.Replace(result, @"\b(?:Dr\.|Doctor|Prof\.|Professor)\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b", "[REDACTED_PHYSICIAN]");
        result = Regex.Replace(result, @"\b(?:Patient|Subject)\s+([A-Z][a-z]+\s+[A-Z][a-z]+)\b", "[REDACTED_PATIENT_NAME]");
        result = Regex.Replace(result, @"\b(?:Site|Hospital|Clinic|Center)\s+([A-Z0-9\-_]+)\b", "[REDACTED_SITE]");

        return result;
    }

    private static CecDiagnosticAttachment AnonymizeAttachment(CecDiagnosticAttachment attachment)
    {
        return new CecDiagnosticAttachment(
            AttachmentId: attachment.AttachmentId,
            DocumentType: attachment.DocumentType,
            Title: attachment.Title,
            Summary: RedactPhiText(attachment.Summary) ?? string.Empty,
            KeyFindings: attachment.KeyFindings?.Select(f => RedactPhiText(f) ?? string.Empty).ToList() ?? []);
    }
}
