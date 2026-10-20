using System;
using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cec;

/// <summary>
/// Domain entity / aggregate root representing an Adjudication Committee Chair tie-breaker determination record
/// sealed with a 21 CFR Part 11 electronic signature manifest.
/// @req:PRD-CEC-002
/// @req:PRD-SYS-124
/// </summary>
public sealed class CecChairAdjudication : AggregateRoot<string>
{
    public string DossierId { get; private set; } = string.Empty;
    public string ChairId { get; private set; } = string.Empty;
    public string ChairName { get; private set; } = string.Empty;
    public string FinalClassification { get; private set; } = string.Empty;
    public string? FinalSubtype { get; private set; }
    public string FinalAdjudicatedOnsetDate { get; private set; } = string.Empty;
    public string BindingReader { get; private set; } = "CHAIR_CUSTOM_DETERMINATION";
    public List<string> CriteriaConfirmed { get; private set; } = [];
    public string AdjudicationRationale { get; private set; } = string.Empty;
    public SignatureManifest SignatureManifest { get; private set; } = null!;
    public DateTime FinalizedAt { get; private set; }

    private CecChairAdjudication() { } // EF Core

    public static CecChairAdjudication Create(
        string determinationId,
        string dossierId,
        string chairId,
        string chairName,
        string finalClassification,
        string? finalSubtype,
        string finalAdjudicatedOnsetDate,
        string bindingReader,
        List<string> criteriaConfirmed,
        string adjudicationRationale,
        SignatureManifest signatureManifest,
        DateTime finalizedAt,
        string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(determinationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chairId);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalClassification);
        ArgumentNullException.ThrowIfNull(signatureManifest);

        return new CecChairAdjudication
        {
            Id = determinationId,
            DossierId = dossierId,
            ChairId = chairId,
            ChairName = chairName,
            FinalClassification = finalClassification,
            FinalSubtype = finalSubtype,
            FinalAdjudicatedOnsetDate = finalAdjudicatedOnsetDate,
            BindingReader = string.IsNullOrWhiteSpace(bindingReader) ? "CHAIR_CUSTOM_DETERMINATION" : bindingReader,
            CriteriaConfirmed = criteriaConfirmed ?? [],
            AdjudicationRationale = adjudicationRationale,
            SignatureManifest = signatureManifest,
            FinalizedAt = finalizedAt,
            CreatedBy = createdBy,
            CreatedAt = finalizedAt,
            ReasonForChange = signatureManifest.SigningReason,
            VersionIndex = 1
        };
    }
}
