using System;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations.Errors;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Aggregate root representing a discrete clinical trial measurement or observation (e.g. vital sign, lab test).
/// Supports dynamic JSONB extension attributes under ADR-2332 and 21 CFR Part 11 immutable audit trails.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class ClinicalObservation : AggregateRoot<ObservationId>
{
    public SubjectCode SubjectId { get; private set; }
    public StudyCode StudyId { get; private set; }
    public SiteCode? SiteId { get; private set; }
    public string? VisitId { get; private set; }
    public DomainCode Domain { get; private set; }
    public DateTime ObservationDate { get; private set; }
    public TestCode TestCode { get; private set; }
    public string TestName { get; private set; } = string.Empty;
    public double? Value { get; private set; }
    public string? ValueString { get; private set; }
    public string? Unit { get; private set; }
    public double? NormalizedValue { get; private set; }
    public string? NormalizedUnit { get; private set; }
    public bool IsOutlier { get; private set; }
    public bool IsSdvVerified { get; private set; }
    public string? SdvVerifiedBy { get; private set; }
    public DateTimeOffset? SdvVerifiedAt { get; private set; }
    public string? PageId { get; private set; }
    public bool IsSdvFlagged { get; private set; }
    public string? SdvFlagReason { get; private set; }
    public string? ProtocolVersionTag { get; private set; }
    public int? ProtocolVersionIndex { get; private set; }
    public FormSubmissionId? FormSubmissionId { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// ADR-2332 dynamic property bag mapped to PostgreSQL jsonb.
    /// </summary>
    public ObservationAdditionalProperties AdditionalProperties { get; private set; } = new();

    // ADR-2332 strongly-typed accessor projections
    public string? LabSource
    {
        get => AdditionalProperties.LabSource;
        set => AdditionalProperties.LabSource = value;
    }

    public string? LabSiteId
    {
        get => AdditionalProperties.LabSiteId;
        set => AdditionalProperties.LabSiteId = value;
    }

    public string? LabIndicator
    {
        get => AdditionalProperties.LabIndicator;
        set => AdditionalProperties.LabIndicator = value;
    }

    public bool? LabOutOfRange
    {
        get => AdditionalProperties.LabOutOfRange;
        set => AdditionalProperties.LabOutOfRange = value;
    }

    public string? MatchedNormalBounds
    {
        get => AdditionalProperties.MatchedNormalBounds;
        set => AdditionalProperties.MatchedNormalBounds = value;
    }

    public string? RangeIndicator
    {
        get => AdditionalProperties.RangeIndicator;
        set => AdditionalProperties.RangeIndicator = value;
    }

    public bool? IsOutOfRange
    {
        get => AdditionalProperties.IsOutOfRange;
        set => AdditionalProperties.IsOutOfRange = value;
    }

    public double? ReferenceRangeLow
    {
        get => AdditionalProperties.ReferenceRangeLow;
        set => AdditionalProperties.ReferenceRangeLow = value;
    }

    public double? ReferenceRangeHigh
    {
        get => AdditionalProperties.ReferenceRangeHigh;
        set => AdditionalProperties.ReferenceRangeHigh = value;
    }

    private ClinicalObservation() { } // EF Core

    public static Result<ClinicalObservation> Create(
        StudyCode studyId,
        SubjectCode subjectId,
        DomainCode domain,
        TestCode testCode,
        string testName,
        DateTime observationDate,
        double? value = null,
        string? valueString = null,
        string? unit = null,
        SiteCode? siteId = null,
        string? visitId = null,
        string? pageId = null,
        string? protocolVersionTag = null,
        int? protocolVersionIndex = null,
        double? normalizedValue = null,
        string? normalizedUnit = null,
        ObservationAdditionalProperties? additionalProperties = null,
        FormSubmissionId? formSubmissionId = null,
        bool isActive = true)
    {
        if (value is null && string.IsNullOrWhiteSpace(valueString))
        {
            return ObservationErrors.InvalidValue("Observation must have either a numeric value or a textual value_string.");
        }

        if (string.IsNullOrWhiteSpace(testName))
        {
            return ObservationErrors.InvalidValue("Test name cannot be empty or whitespace.");
        }

        // Valid time is when the observation was made, supplied by the caller. It is never defaulted to the
        // time the server records it (#4769, kernel §4.1).
        if (observationDate == default)
        {
            return ObservationErrors.ObservationDateRequired();
        }

        var observation = new ClinicalObservation
        {
            Id = ObservationId.New(),
            StudyId = studyId,
            SubjectId = subjectId,
            Domain = domain,
            TestCode = testCode,
            TestName = testName.Trim(),
            Value = value,
            ValueString = valueString,
            Unit = unit,
            // timestamptz holds UTC only. A time without an offset is read as UTC, as the form path reads it.
            ObservationDate = observationDate.Kind switch
            {
                DateTimeKind.Local => observationDate.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(observationDate, DateTimeKind.Utc),
                _ => observationDate,
            },
            SiteId = siteId,
            VisitId = visitId,
            PageId = pageId,
            ProtocolVersionTag = protocolVersionTag,
            ProtocolVersionIndex = protocolVersionIndex,
            NormalizedValue = normalizedValue,
            NormalizedUnit = normalizedUnit,
            FormSubmissionId = formSubmissionId,
            IsActive = isActive,
            IsOutlier = false,
            IsSdvVerified = false,
            IsSdvFlagged = false,
            AdditionalProperties = additionalProperties?.Clone() ?? new()
        };

        return observation;
    }

    public Result<bool> VerifySdv(string verifierUserId, DateTimeOffset verifiedAt)
    {
        if (string.IsNullOrWhiteSpace(verifierUserId))
        {
            return DomainError.Validation("INVALID_VERIFIER", "Verifier user ID cannot be empty.");
        }

        if (IsSdvVerified)
        {
            return ObservationErrors.AlreadyVerified(Id);
        }

        IsSdvVerified = true;
        SdvVerifiedBy = verifierUserId;
        SdvVerifiedAt = verifiedAt.ToUniversalTime();
        return true;
    }

    public Result<bool> FlagSdv(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return DomainError.Validation("INVALID_FLAG_REASON", "SDV flag reason cannot be empty.");
        }

        IsSdvFlagged = true;
        SdvFlagReason = reason.Trim();
        IsSdvVerified = false;
        SdvVerifiedBy = null;
        SdvVerifiedAt = null;
        ReasonForChange = reason.Trim();
        return true;
    }

    public void ClearSdvFlag()
    {
        IsSdvFlagged = false;
        SdvFlagReason = null;
    }

    public void UpdateNormalizedValue(double? normalizedValue, string? normalizedUnit)
    {
        NormalizedValue = normalizedValue;
        NormalizedUnit = normalizedUnit;
    }

    public void UpdateValue(double? value, string? valueString, string? unit, double? normalizedValue, string? normalizedUnit, string? reasonForChange = null)
    {
        Value = value;
        ValueString = valueString;
        Unit = unit;
        NormalizedValue = normalizedValue;
        NormalizedUnit = normalizedUnit;
        if (!string.IsNullOrWhiteSpace(reasonForChange))
        {
            ReasonForChange = reasonForChange.Trim();
        }
    }

    public void SetOutlier(bool isOutlier)
    {
        IsOutlier = isOutlier;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public bool MatchesCoordinates(SubjectCode subjectId, string? visitId, DomainCode domain, SiteCode? siteId) =>
        SubjectId == subjectId &&
        VisitId == visitId &&
        Domain == domain &&
        SiteId == siteId;
}
