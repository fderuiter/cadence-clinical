using System;
using System.Collections.Generic;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

internal enum StudyExportFormat
{
    Csv,
    OdmXml,
    DatasetJson,
    SasBundle,
    RBundle,
    StudyArchive,
}

internal enum CasebookExportFormat
{
    Json,
    OdmXml,
    Html,
    Pdf,
}

internal enum StudyLockFilter
{
    All,
    LockedOnly,
    FrozenOrLocked,
    UnlockedOnly,
}

internal sealed record HiddenScopeSummary(bool MoreExist, IReadOnlyList<string> Reasons)
{
    public static HiddenScopeSummary None { get; } = new(false, []);
}

internal sealed record StudyExportVisibility(
    IReadOnlyDictionary<NodeRef, MaskReason> Masks,
    HashSet<EnrollmentId> UnblindedEnrollments);

internal sealed record ProjectedStudyExport(
    StudyExportSnapshot Snapshot,
    string LockFilter,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProjectedObservationRow> Rows,
    StudyExportVisibility Visibility,
    IReadOnlySet<EnrollmentId> VisibleEnrollmentIds,
    KernelHiddenContentResponse Hidden,
    HiddenScopeSummary HiddenScopes);

internal sealed record ProjectedObservationRow(
    string StudyCode,
    string SiteCode,
    string SubjectCode,
    string EnrollmentRef,
    string ObservationRef,
    string? CollectionInstanceRef,
    string CollectionSpecId,
    string? PlannedActivityId,
    string ItemId,
    string ConceptRef,
    string ConceptCode,
    string ConceptLabel,
    DateTimeOffset? ObservedAt,
    int? Revision,
    string ValueKind,
    string ResultText,
    decimal? ResultDecimal,
    string? ResultUnit,
    string? LockState,
    bool Masked,
    string? MaskReason,
    string? RecordedBy,
    DateTimeOffset? RecordedAt,
    ExportObservationSnapshot? SourceObservation,
    CollectionInstanceId? GroupingCollectionInstanceId = null)
{
    public static ProjectedObservationRow CreateBlinded(
        string studyCode,
        ExportEnrollmentSnapshot? enrollment,
        ExportSiteSnapshot? site,
        ExportObservationSnapshot obs) =>
        new(
            StudyCode: studyCode,
            SiteCode: site?.SiteCode ?? enrollment?.SiteCode ?? obs.StudySiteId.ToString(),
            SubjectCode: enrollment?.SubjectCode ?? obs.EnrollmentId.ToString(),
            EnrollmentRef: NodeRef.For(obs.EnrollmentId).ToString(),
            ObservationRef: "masked",
            CollectionInstanceRef: null,
            CollectionSpecId: obs.CollectionSpecId ?? "unscheduled",
            PlannedActivityId: obs.PlannedActivityId,
            ItemId: obs.ItemId ?? obs.ConceptCode,
            ConceptRef: obs.Concept.ToString(),
            ConceptCode: obs.ConceptCode,
            ConceptLabel: obs.ConceptLabel,
            ObservedAt: obs.ObservedAt,
            Revision: null,
            ValueKind: "masked",
            ResultText: "masked",
            ResultDecimal: null,
            ResultUnit: null,
            LockState: obs.EffectiveLock?.ToString(),
            Masked: true,
            MaskReason: Cadence.Kernel.References.MaskReason.Blinded.ToString(),
            RecordedBy: null,
            RecordedAt: obs.CurrentRevision.RecordedAt,
            SourceObservation: null,
            GroupingCollectionInstanceId: obs.CollectionInstanceId);

    public static ProjectedObservationRow CreateVisible(
        string studyCode,
        ExportEnrollmentSnapshot? enrollment,
        ExportSiteSnapshot? site,
        ExportObservationSnapshot obs)
    {
        var current = obs.CurrentRevision.Value;
        decimal? numeric = current is QuantityValue qv ? qv.Value : null;
        string? unit = current is QuantityValue qu ? qu.Unit : null;
        string display = current switch
        {
            QuantityValue quantity => quantity.ToResultString(),
            _ => current.ToDisplayString(),
        };

        return new ProjectedObservationRow(
            StudyCode: studyCode,
            SiteCode: site?.SiteCode ?? enrollment?.SiteCode ?? obs.StudySiteId.ToString(),
            SubjectCode: enrollment?.SubjectCode ?? obs.EnrollmentId.ToString(),
            EnrollmentRef: NodeRef.For(obs.EnrollmentId).ToString(),
            ObservationRef: NodeRef.For(obs.ObservationId, obs.CurrentRevision.Number).ToString(),
            CollectionInstanceRef: obs.CollectionInstanceId is { } cid ? NodeRef.For(cid).ToString() : null,
            CollectionSpecId: obs.CollectionSpecId ?? "unscheduled",
            PlannedActivityId: obs.PlannedActivityId,
            ItemId: obs.ItemId ?? obs.ConceptCode,
            ConceptRef: obs.Concept.ToString(),
            ConceptCode: obs.ConceptCode,
            ConceptLabel: obs.ConceptLabel,
            ObservedAt: obs.ObservedAt,
            Revision: obs.CurrentRevision.Number,
            ValueKind: current.Kind.ToString(),
            ResultText: display,
            ResultDecimal: numeric,
            ResultUnit: unit,
            LockState: obs.EffectiveLock?.ToString(),
            Masked: false,
            MaskReason: null,
            RecordedBy: obs.CurrentRevision.RecordedBy,
            RecordedAt: obs.CurrentRevision.RecordedAt,
            SourceObservation: obs,
            GroupingCollectionInstanceId: obs.CollectionInstanceId);
    }
}
