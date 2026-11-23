using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;

namespace Cadence.Kernel.Execution;

/// <summary>
/// Read-only view of a study's governance artifacts, protocol effectivities, and kernel study sites under the caller's
/// tenant Row-Level Security (ADR-2250). Hosts authorize study visibility with <c>INodeVisibility</c> before returning rows.
/// </summary>
public interface IStudyGovernanceReader
{
    /// <summary>Lists registered artifacts scoped to <paramref name="studyId"/>, newest version metadata attached.</summary>
    Task<IReadOnlyList<StudyArtifactSummary>> ListStudyArtifactsAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all protocol-version effectivities declared in <paramref name="studyId"/>.</summary>
    Task<IReadOnlyList<StudyEffectivitySummary>> ListStudyEffectivitiesAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one effectivity in <paramref name="tenant"/>, or null when it does not exist.</summary>
    Task<StudyEffectivitySummary?> FindEffectivityAsync(
        TenantId tenant,
        EffectivityId effectivityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the study sites linked to <paramref name="studyId"/> with each site's code, country, status, and deciding
    /// effective protocol versions as of <paramref name="validAt"/> / <paramref name="knownAt"/>.
    /// </summary>
    Task<IReadOnlyList<StudySiteGovernanceSummary>> ListStudySitesAsync(
        TenantId tenant,
        StudyId studyId,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one study site in <paramref name="tenant"/> with its effective protocol versions, or null.</summary>
    Task<StudySiteGovernanceSummary?> FindStudySiteAsync(
        TenantId tenant,
        StudySiteId studySiteId,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an existing site in <paramref name="tenant"/> by normalized site code, or null.</summary>
    Task<SiteLookupEntry?> FindSiteByCodeAsync(
        TenantId tenant,
        string siteCode,
        CancellationToken cancellationToken = default);
}

/// <summary>Summary of a study-scoped evidence artifact and its latest immutable version.</summary>
/// <param name="ArtifactId">The artifact identifier.</param>
/// <param name="Kind">The declared artifact kind.</param>
/// <param name="StudyId">The owning study identifier.</param>
/// <param name="Title">Human-readable artifact title.</param>
/// <param name="LatestVersionId">The newest version identifier.</param>
/// <param name="LatestVersionNumber">1-based version number of the newest version.</param>
/// <param name="ContentHash">Canonical <c>sha256:</c> digest of the newest version.</param>
/// <param name="SizeBytes">Byte length of the newest version.</param>
/// <param name="MediaType">MIME type of the newest version.</param>
/// <param name="StorageRef">Object-storage reference of the newest version.</param>
/// <param name="RecordedAt">When the newest version was recorded.</param>
public sealed record StudyArtifactSummary(
    ArtifactId ArtifactId,
    ArtifactKind Kind,
    StudyId StudyId,
    string Title,
    ArtifactVersionId LatestVersionId,
    int LatestVersionNumber,
    string ContentHash,
    long SizeBytes,
    string MediaType,
    string StorageRef,
    DateTimeOffset RecordedAt);

/// <summary>Summary of a protocol version's effectivity in a study, country, or study-site scope.</summary>
/// <param name="EffectivityId">The effectivity identifier.</param>
/// <param name="StudyId">The study identifier.</param>
/// <param name="ProtocolVersionId">The governed protocol version identifier.</param>
/// <param name="ProtocolVersionNumber">1-based protocol version number within the study.</param>
/// <param name="Scope">The study, country, or study-site scope.</param>
/// <param name="Status">Current lifecycle status of the effectivity.</param>
/// <param name="StatusSince">When the current status began in valid time.</param>
/// <param name="Evidence">Artifact version identifiers evidencing the current status.</param>
public sealed record StudyEffectivitySummary(
    EffectivityId EffectivityId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    int ProtocolVersionNumber,
    EffectivityScope Scope,
    EffectivityStatus Status,
    DateTimeOffset StatusSince,
    IReadOnlyList<ArtifactVersionId> Evidence);

/// <summary>Summary of a study site and the protocol versions currently effective there.</summary>
/// <param name="StudySiteId">The study-site identifier.</param>
/// <param name="StudyId">The study identifier.</param>
/// <param name="SiteId">The underlying site identifier.</param>
/// <param name="SiteCode">Human-facing site code.</param>
/// <param name="SiteName">Site display name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
/// <param name="Status">Current study-site status (<c>Selected</c> or <c>Active</c>).</param>
/// <param name="EffectiveVersions">Protocol versions currently effective at this study site, highest number first.</param>
public sealed record StudySiteGovernanceSummary(
    StudySiteId StudySiteId,
    StudyId StudyId,
    SiteId SiteId,
    string SiteCode,
    string SiteName,
    string Country,
    string Status,
    IReadOnlyList<EffectiveVersion> EffectiveVersions);

/// <summary>Lookup summary for an existing kernel <c>Site</c> row in a tenant.</summary>
/// <param name="SiteId">The site identifier.</param>
/// <param name="InstitutionId">The owning institution organization identifier.</param>
/// <param name="Code">Site code.</param>
/// <param name="Name">Site name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
public sealed record SiteLookupEntry(
    SiteId SiteId,
    OrganizationId InstitutionId,
    string Code,
    string Name,
    string Country);
