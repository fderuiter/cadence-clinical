using System;
using System.Collections.Generic;

namespace Cadence.Kernel.Api;

internal enum PdfFontKind
{
    Heading,
    Subheading,
    Body,
    Mono,
}

internal sealed record PdfStyledLine(PdfFontKind Font, int FontSize, string Text);

internal sealed record CasebookOdmFormBucket(
    string EventOid,
    string EventName,
    bool IsScheduled,
    string SpecId,
    string FormOid,
    string ItemGroupOid,
    string FormName,
    string? FormRepeatKey,
    bool FormMasked,
    string? FormMaskReason,
    List<KernelCasebookObservationView> Observations);

internal sealed record CollectionTableSlice(
    string TableName,
    string SpecId,
    string Label,
    IReadOnlyList<ProjectedObservationRow> Rows);

internal sealed record BundleHiddenScopesView(
    bool MoreExist,
    IReadOnlyList<string> Reasons);

internal sealed record StudyExportBundleManifest(
    string Producer,
    string Format,
    string StudyRef,
    string StudyCode,
    string StudyTitle,
    string LockFilter,
    string ValidAt,
    string KnownAt,
    string GeneratedAt,
    string RequestedBy,
    KernelHiddenContentResponse Hidden,
    BundleHiddenScopesView HiddenScopes,
    IReadOnlyList<BundleManifestFileEntry> Files,
    IReadOnlyList<BundleManifestCasebookEntry> Casebooks);

internal sealed record BundleManifestFileEntry(
    string Path,
    string ContentType,
    long SizeBytes,
    string Sha256);

internal sealed record BundleManifestCasebookEntry(
    string EnrollmentRef,
    string SiteCode,
    string SubjectCode,
    bool Masked,
    string? Mask,
    string CasebookDigestSha256,
    string PdfPath,
    string PdfSha256,
    string OdmXmlPath,
    string OdmXmlSha256);

internal sealed record BundleAuditTrailPayload(
    KernelHiddenContentResponse Hidden,
    IReadOnlyList<BundleAuditEntryView> Entries);

internal sealed record BundleAuditEntryView(
    long Sequence,
    string EventKind,
    string? ObservationRef,
    int? Revision,
    DateTimeOffset RecordedAt,
    string? ActorId,
    string Summary,
    bool Masked,
    string? MaskReason);
