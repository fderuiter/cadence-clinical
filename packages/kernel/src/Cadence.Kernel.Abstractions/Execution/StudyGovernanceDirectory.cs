using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;

namespace Cadence.Kernel.Execution;

/// <summary>Summary of a registered clinical site in a tenant.</summary>
/// <param name="SiteId">Canonical site identifier.</param>
/// <param name="Version">Current aggregate version.</param>
/// <param name="InstitutionId">Owning institution organization identifier.</param>
/// <param name="InstitutionCode">Owning institution code.</param>
/// <param name="InstitutionName">Owning institution display name.</param>
/// <param name="SiteCode">Site code unique within the institution.</param>
/// <param name="Name">Human-readable site name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
public sealed record SiteDirectoryEntry(
    SiteId SiteId,
    long Version,
    OrganizationId InstitutionId,
    string InstitutionCode,
    string InstitutionName,
    string SiteCode,
    string Name,
    string Country);

/// <summary>Summary of a site linked to a study (<c>StudySite</c>).</summary>
/// <param name="StudySiteId">Canonical study-site identifier.</param>
/// <param name="Version">Current aggregate version.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="StudyCode">Study code.</param>
/// <param name="SiteId">Linked site identifier.</param>
/// <param name="SiteCode">Site code.</param>
/// <param name="SiteName">Site display name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
/// <param name="InstitutionId">Owning institution identifier.</param>
/// <param name="InstitutionName">Owning institution name.</param>
/// <param name="Status">Lifecycle status (<c>Selected</c> or <c>Active</c>).</param>
public sealed record StudySiteDirectoryEntry(
    StudySiteId StudySiteId,
    long Version,
    StudyId StudyId,
    string StudyCode,
    SiteId SiteId,
    string SiteCode,
    string SiteName,
    string Country,
    OrganizationId InstitutionId,
    string InstitutionName,
    string Status);

/// <summary>Summary of one immutable version of an evidence artifact.</summary>
/// <param name="VersionId">Artifact version identifier.</param>
/// <param name="Number">1-based version number.</param>
/// <param name="ContentHash">Canonical <c>sha256:&lt;hex&gt;</c> digest.</param>
/// <param name="SizeBytes">Payload byte length.</param>
/// <param name="MediaType">IANA media type.</param>
/// <param name="StorageRef">Object storage reference.</param>
/// <param name="RecordedAt">Kernel timestamp when the version was recorded.</param>
public sealed record ArtifactVersionDirectoryEntry(
    ArtifactVersionId VersionId,
    int Number,
    string ContentHash,
    long SizeBytes,
    string MediaType,
    string StorageRef,
    DateTimeOffset RecordedAt);

/// <summary>Summary of a registered evidence artifact and its versions.</summary>
/// <param name="ArtifactId">Artifact identifier.</param>
/// <param name="Version">Aggregate version index.</param>
/// <param name="Kind">Evidence artifact kind.</param>
/// <param name="StudyId">Owning study identifier, or null when tenant-wide.</param>
/// <param name="Title">Artifact display title.</param>
/// <param name="LatestVersion">Newest immutable version.</param>
/// <param name="Versions">All immutable versions ordered oldest first.</param>
public sealed record ArtifactDirectoryEntry(
    ArtifactId ArtifactId,
    long Version,
    ArtifactKind Kind,
    StudyId? StudyId,
    string Title,
    ArtifactVersionDirectoryEntry LatestVersion,
    IReadOnlyList<ArtifactVersionDirectoryEntry> Versions);

/// <summary>Summary of a protocol effectivity declaration in a study.</summary>
/// <param name="EffectivityId">Effectivity identifier.</param>
/// <param name="Version">Aggregate version index.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="ProtocolVersionId">Governed protocol version identifier.</param>
/// <param name="ProtocolVersionNumber">1-based protocol version number in the study.</param>
/// <param name="Scope">Effectivity scope (<c>Study</c>, <c>Country</c>, or <c>StudySite</c>).</param>
/// <param name="Status">Current status (<c>Pending</c>, <c>Effective</c>, <c>Superseded</c>, <c>Ended</c>).</param>
/// <param name="StatusSince">Valid-time start of the current status.</param>
/// <param name="Evidence">Artifact version identifiers evidencing the current status.</param>
public sealed record EffectivityDirectoryEntry(
    EffectivityId EffectivityId,
    long Version,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    int ProtocolVersionNumber,
    EffectivityScope Scope,
    EffectivityStatus Status,
    DateTimeOffset StatusSince,
    IReadOnlyList<ArtifactVersionId> Evidence);

/// <summary>Read-only tenant-scoped directory over sites, study sites, evidence artifacts, and protocol effectivities.</summary>
public interface IStudyGovernanceReader
{
    /// <summary>Lists all registered sites in <paramref name="tenant"/>.</summary>
    Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default);

    /// <summary>Finds one site by identifier in <paramref name="tenant"/>.</summary>
    Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default);

    /// <summary>Finds one site by code (case-insensitive) in <paramref name="tenant"/>.</summary>
    Task<SiteDirectoryEntry?> FindSiteByCodeAsync(TenantId tenant, string siteCode, CancellationToken cancellationToken = default);

    /// <summary>Lists all sites linked to <paramref name="studyId"/> in <paramref name="tenant"/>.</summary>
    Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default);

    /// <summary>Finds one study site by identifier in <paramref name="tenant"/>.</summary>
    Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default);

    /// <summary>Finds one study site by study code and site code (case-insensitive) in <paramref name="tenant"/>.</summary>
    Task<StudySiteDirectoryEntry?> FindStudySiteByCodeAsync(
        TenantId tenant,
        string studyCode,
        string siteCode,
        CancellationToken cancellationToken = default);

    /// <summary>Lists artifacts belonging to <paramref name="studyId"/> in <paramref name="tenant"/>.</summary>
    Task<IReadOnlyList<ArtifactDirectoryEntry>> ListStudyArtifactsAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default);

    /// <summary>Finds one artifact by identifier in <paramref name="tenant"/>.</summary>
    Task<ArtifactDirectoryEntry?> FindArtifactAsync(TenantId tenant, ArtifactId artifactId, CancellationToken cancellationToken = default);

    /// <summary>Lists protocol effectivity declarations for <paramref name="studyId"/> in <paramref name="tenant"/>.</summary>
    Task<IReadOnlyList<EffectivityDirectoryEntry>> ListStudyEffectivitiesAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one protocol effectivity declaration by identifier in <paramref name="tenant"/>.</summary>
    Task<EffectivityDirectoryEntry?> FindEffectivityAsync(
        TenantId tenant,
        EffectivityId effectivityId,
        CancellationToken cancellationToken = default);
}
