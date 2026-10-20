using System;
using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cec;

/// <summary>
/// Aggregate root representing a Clinical Endpoint Adjudication Committee (CEC) dossier.
/// Governs blinded dual-reader assessments, concordance/discordance state transitions,
/// and Part 11 Chair tie-breaker finalization.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
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
    public string WorkflowStatus { get; private set; } = "PENDING_PRIMARY_REVIEWS";
    public bool IsDiscordant { get; private set; }
    public List<string> DiscordanceReasons { get; private set; } = [];
    public string? FinalConsensusClassification { get; private set; }
    public string? FinalConsensusSubtype { get; private set; }
    public string? FinalAdjudicatedOnsetDate { get; private set; }
    public string? AdjudicationRationale { get; private set; }
    public string Reader1Status { get; private set; } = "PENDING";
    public string Reader2Status { get; private set; } = "PENDING";
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
        List<string>? discordanceReasons = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        return new CecDossier
        {
            Id = dossierId,
            EventId = eventId,
            StudyId = studyId,
            SubjectId = subjectId,
            MaskedSubjectId = maskedSubjectId,
            SiteId = siteId,
            MaskedSiteId = maskedSiteId,
            CandidateEventType = candidateEventType,
            ReportedTerm = reportedTerm,
            ReportedOnsetDate = reportedOnsetDate,
            WorkflowStatus = workflowStatus,
            IsDiscordant = isDiscordant,
            DiscordanceReasons = discordanceReasons ?? [],
            Reader1Status = "PENDING",
            Reader2Status = "PENDING"
        };
    }

    /// <summary>
    /// Finalizes the dossier via Chair tie-breaker adjudication and attaches the Part 11 e-signature manifest.
    /// Rejects re-finalization attempts to prevent race conditions.
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
}
