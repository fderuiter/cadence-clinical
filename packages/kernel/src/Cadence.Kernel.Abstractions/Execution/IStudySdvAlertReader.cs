using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Execution;

/// <summary>
/// Narrow read-side port exposing active CRA source data verification (SDV) modification alerts for a study so the
/// kernel's unified <c>GET /api/v1/kernel/studies/{studyRef}/attention</c> endpoint can include them in Wave 1 without
/// referencing the Execution module directly (<c>K-XS-1</c>, <c>K-XS-2</c>, <c>K-MOD-1</c>, <c>PRD-QRY-006</c>).
/// </summary>
public interface IStudySdvAlertReader
{
    /// <summary>
    /// Lists active (not yet re-verified) SDV modification alerts visible to the current caller in <paramref name="study"/>,
    /// omitting blinded treatment observations when the caller lacks <c>enrollment.unblind</c> (<c>K-AUTH-4</c>).
    /// </summary>
    /// <param name="tenant">The caller's tenant.</param>
    /// <param name="study">The study identifier.</param>
    /// <param name="knownStudyCode">Optional already-resolved study code to avoid a redundant study summary lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<StudySdvAlertView>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        string? knownStudyCode = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Narrow read projection of an active CRA SDV modification alert for a previously verified observation whose latest
/// revision has not yet been re-verified against source (<c>PRD-QRY-006</c>).
/// </summary>
/// <param name="AlertId">Unique identifier of the recorded alert episode.</param>
/// <param name="StudyId">Kernel study identifier.</param>
/// <param name="StudySiteId">Optional kernel study-site identifier.</param>
/// <param name="EnrollmentId">Kernel enrollment identifier.</param>
/// <param name="FormId">Optional kernel form (collection instance) identifier.</param>
/// <param name="ObservationId">Kernel observation identifier.</param>
/// <param name="StudyCode">Resolved study code.</param>
/// <param name="SiteCode">Resolved site code.</param>
/// <param name="SubjectCode">Resolved participant/subject code.</param>
/// <param name="CollectionSpecId">Protocol collection specification identifier.</param>
/// <param name="PlannedActivityId">Protocol planned activity (visit) identifier.</param>
/// <param name="ItemId">Protocol form item identifier.</param>
/// <param name="SupersededRevision">Revision number that previously carried an active <c>Verified</c> assertion.</param>
/// <param name="CurrentRevision">Latest observation revision number awaiting re-verification.</param>
/// <param name="VerifiedBy">Actor who verified <paramref name="SupersededRevision"/>.</param>
/// <param name="VerifiedAt">UTC timestamp when <paramref name="SupersededRevision"/> was verified.</param>
/// <param name="ModifiedBy">Actor who recorded <paramref name="CurrentRevision"/>.</param>
/// <param name="ModifiedAt">UTC timestamp when <paramref name="CurrentRevision"/> was recorded.</param>
/// <param name="ReasonForChange">Reason for change recorded on <paramref name="CurrentRevision"/>.</param>
/// <param name="Message">Formatted monitor-facing alert summary.</param>
public sealed record StudySdvAlertView(
    Guid AlertId,
    StudyId StudyId,
    StudySiteId? StudySiteId,
    EnrollmentId EnrollmentId,
    CollectionInstanceId? FormId,
    ObservationId ObservationId,
    string? StudyCode,
    string? SiteCode,
    string? SubjectCode,
    string? CollectionSpecId,
    string? PlannedActivityId,
    string? ItemId,
    int SupersededRevision,
    int CurrentRevision,
    string VerifiedBy,
    DateTimeOffset VerifiedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    string ReasonForChange,
    string Message);

/// <summary>
/// Shared wire payload base record for an active CRA SDV modification alert (<c>PRD-QRY-006</c>), projected from
/// <see cref="StudySdvAlertView"/> into JSON-serializable primitive identifiers (<c>K-XS-1</c>).
/// </summary>
public abstract record StudySdvAlertPayload
{
    /// <summary>Initializes an empty payload instance for JSON deserialization.</summary>
    protected StudySdvAlertPayload()
    {
    }

    /// <summary>Projects a <see cref="StudySdvAlertView"/> into wire primitives.</summary>
    /// <param name="view">Active SDV alert projection.</param>
    [SetsRequiredMembers]
    protected StudySdvAlertPayload(StudySdvAlertView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        AlertId = view.AlertId;
        StudyId = view.StudyId.Value;
        StudySiteId = view.StudySiteId?.Value;
        EnrollmentId = view.EnrollmentId.Value;
        FormId = view.FormId?.Value;
        ObservationId = view.ObservationId.Value;
        StudyCode = view.StudyCode;
        SiteCode = view.SiteCode;
        SubjectCode = view.SubjectCode;
        CollectionSpecId = view.CollectionSpecId;
        PlannedActivityId = view.PlannedActivityId;
        ItemId = view.ItemId;
        SupersededRevision = view.SupersededRevision;
        CurrentRevision = view.CurrentRevision;
        VerifiedBy = view.VerifiedBy;
        VerifiedAt = view.VerifiedAt;
        ModifiedBy = view.ModifiedBy;
        ModifiedAt = view.ModifiedAt;
        ReasonForChange = view.ReasonForChange;
        Message = view.Message;
    }

    /// <summary>Unique identifier of the recorded alert episode.</summary>
    public required Guid AlertId { get; init; }

    /// <summary>Kernel study UUID.</summary>
    public required Guid StudyId { get; init; }

    /// <summary>Optional kernel study-site UUID.</summary>
    public required Guid? StudySiteId { get; init; }

    /// <summary>Kernel enrollment UUID.</summary>
    public required Guid EnrollmentId { get; init; }

    /// <summary>Optional kernel form (collection instance) UUID.</summary>
    public required Guid? FormId { get; init; }

    /// <summary>Kernel observation UUID.</summary>
    public required Guid ObservationId { get; init; }

    /// <summary>Resolved study code.</summary>
    public required string? StudyCode { get; init; }

    /// <summary>Resolved site code.</summary>
    public required string? SiteCode { get; init; }

    /// <summary>Resolved participant/subject code.</summary>
    public required string? SubjectCode { get; init; }

    /// <summary>Protocol collection specification identifier.</summary>
    public required string? CollectionSpecId { get; init; }

    /// <summary>Protocol planned activity (visit) identifier.</summary>
    public required string? PlannedActivityId { get; init; }

    /// <summary>Protocol form item identifier.</summary>
    public required string? ItemId { get; init; }

    /// <summary>Revision number that previously carried an active <c>Verified</c> assertion.</summary>
    public required int SupersededRevision { get; init; }

    /// <summary>Latest observation revision number awaiting re-verification.</summary>
    public required int CurrentRevision { get; init; }

    /// <summary>Actor who verified <see cref="SupersededRevision"/>.</summary>
    public required string VerifiedBy { get; init; }

    /// <summary>UTC timestamp when <see cref="SupersededRevision"/> was verified.</summary>
    public required DateTimeOffset VerifiedAt { get; init; }

    /// <summary>Actor who recorded <see cref="CurrentRevision"/>.</summary>
    public required string ModifiedBy { get; init; }

    /// <summary>UTC timestamp when <see cref="CurrentRevision"/> was recorded.</summary>
    public required DateTimeOffset ModifiedAt { get; init; }

    /// <summary>Reason for change recorded on <see cref="CurrentRevision"/>.</summary>
    public required string ReasonForChange { get; init; }

    /// <summary>Formatted monitor-facing alert summary.</summary>
    public required string Message { get; init; }
}
