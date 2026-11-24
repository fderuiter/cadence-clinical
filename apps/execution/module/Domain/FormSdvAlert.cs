using System;
using System.Security.Cryptography;
using System.Text;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Time;

namespace Cadence.Modules.Execution.Domain;

/// <summary>
/// Insert-only CRA source data verification modification alert (<c>PRD-QRY-006</c>, ADR-2620, ADR-2621): emitted when an
/// observation revision that held an active <c>Verified</c> assertion is superseded by a correction.
/// </summary>
public sealed class FormSdvAlert : AggregateRoot<Guid>
{
    private FormSdvAlert()
    {
    }

    private FormSdvAlert(
        Guid id,
        TenantId tenantId,
        StudyId studyId,
        StudySiteId? studySiteId,
        EnrollmentId enrollmentId,
        CollectionInstanceId? formId,
        string? itemId,
        ObservationId observationId,
        int supersededRevision,
        int newRevision,
        string verifiedBy,
        DateTimeOffset verifiedAt,
        string modifiedBy,
        DateTimeOffset modifiedAt,
        string modifiedReason)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        StudySiteId = studySiteId;
        EnrollmentId = enrollmentId;
        FormId = formId;
        ItemId = itemId;
        ObservationId = observationId;
        SupersededRevision = supersededRevision;
        NewRevision = newRevision;
        VerifiedBy = verifiedBy;
        VerifiedAt = KernelTimestamps.ToStoredPrecision(verifiedAt);
        ModifiedBy = modifiedBy;
        ModifiedAt = KernelTimestamps.ToStoredPrecision(modifiedAt);
        ModifiedReason = modifiedReason;
    }

    /// <summary>The tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The study the modified observation belongs to (<c>K-MOD-1</c>).</summary>
    public StudyId StudyId { get; private set; }

    /// <summary>Optional study site of the enrollment when known (<c>K-MOD-1</c>).</summary>
    public StudySiteId? StudySiteId { get; private set; }

    /// <summary>The participant enrollment the observation belongs to.</summary>
    public EnrollmentId EnrollmentId { get; private set; }

    /// <summary>Optional eCRF form instance containing the observation.</summary>
    public CollectionInstanceId? FormId { get; private set; }

    /// <summary>Optional eCRF collection item identifier.</summary>
    public string? ItemId { get; private set; }

    /// <summary>The modified observation.</summary>
    public ObservationId ObservationId { get; private set; }

    /// <summary>The verified revision superseded by the correction.</summary>
    public int SupersededRevision { get; private set; }

    /// <summary>The new revision recorded by the correction.</summary>
    public int NewRevision { get; private set; }

    /// <summary>Monitor who verified the superseded revision.</summary>
    public string VerifiedBy { get; private set; } = string.Empty;

    /// <summary>When the superseded revision was verified.</summary>
    public DateTimeOffset VerifiedAt { get; private set; }

    /// <summary>Actor who recorded the superseding correction.</summary>
    public string ModifiedBy { get; private set; } = string.Empty;

    /// <summary>When the superseding correction was recorded.</summary>
    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>Verbatim GxP reason for change recorded on the correction.</summary>
    public string ModifiedReason { get; private set; } = string.Empty;

    /// <summary>
    /// Deterministic alert identifier for one <c>(tenant, observation, newRevision)</c> episode so redelivered events
    /// converge on the same aggregate key.
    /// </summary>
    public static Guid CreateId(TenantId tenantId, ObservationId observationId, int newRevision)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"execution.sdv_alert:{tenantId.Value:D}:{observationId.Value:D}:{newRevision}"));
        Span<byte> bytes = stackalloc byte[16];
        digest.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>Creates a new CRA SDV modification alert record.</summary>
    public static FormSdvAlert Create(
        Guid id,
        TenantId tenantId,
        StudyId studyId,
        StudySiteId? studySiteId,
        EnrollmentId enrollmentId,
        CollectionInstanceId? formId,
        string? itemId,
        ObservationId observationId,
        int supersededRevision,
        int newRevision,
        string verifiedBy,
        DateTimeOffset verifiedAt,
        string modifiedBy,
        DateTimeOffset modifiedAt,
        string modifiedReason)
    {
        if (id == Guid.Empty || tenantId.IsEmpty || studyId.IsEmpty || enrollmentId.IsEmpty || observationId.IsEmpty)
        {
            throw new ArgumentException("An SDV modification alert requires non-empty alert, tenant, study, enrollment, and observation identifiers.");
        }

        if (supersededRevision < 1 || newRevision <= supersededRevision)
        {
            throw new ArgumentOutOfRangeException(nameof(newRevision), "NewRevision must be greater than a positive SupersededRevision.");
        }

        if (string.IsNullOrWhiteSpace(verifiedBy) || string.IsNullOrWhiteSpace(modifiedBy))
        {
            throw new ArgumentException("VerifiedBy and ModifiedBy are required.");
        }

        return new FormSdvAlert(
            id,
            tenantId,
            studyId,
            studySiteId is { IsEmpty: false } site ? site : null,
            enrollmentId,
            formId is { IsEmpty: false } form ? form : null,
            string.IsNullOrWhiteSpace(itemId) ? null : itemId.Trim(),
            observationId,
            supersededRevision,
            newRevision,
            verifiedBy.Trim(),
            verifiedAt,
            modifiedBy.Trim(),
            modifiedAt,
            string.IsNullOrWhiteSpace(modifiedReason) ? "Verified observation modified" : modifiedReason.Trim());
    }
}
