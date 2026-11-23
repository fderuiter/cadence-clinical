using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Execution;

/// <summary>
/// Read-only access to a tenant's studies and organizations for workspace onboarding and self-service study creation.
/// Reads run under the tenant's Row-Level Security; the caller checks node visibility (<c>INodeVisibility</c>) before
/// exposing entries to an untrusted reader.
/// </summary>
public interface IStudyDirectory
{
    /// <summary>Lists all studies in <paramref name="tenant"/>, ordered by study code.</summary>
    Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default);

    /// <summary>Finds one study in <paramref name="tenant"/> by its identifier, or null when absent.</summary>
    Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default);

    /// <summary>Finds one study in <paramref name="tenant"/> by its study code (case-insensitive), or null when absent.</summary>
    Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default);

    /// <summary>Lists all organizations in <paramref name="tenant"/>, ordered by organization code.</summary>
    Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default);

    /// <summary>Finds one organization in <paramref name="tenant"/> by its identifier, or null when absent.</summary>
    Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default);

    /// <summary>Finds one organization in <paramref name="tenant"/> by its code (case-insensitive), or null when absent.</summary>
    Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default);

    /// <summary>Lists all investigational sites in <paramref name="tenant"/>, ordered by site code.</summary>
    Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default);

    /// <summary>Finds one site in <paramref name="tenant"/> by its identifier, or null when absent.</summary>
    Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one site in <paramref name="tenant"/> by its site code (case-insensitive), optionally narrowed to
    /// <paramref name="institutionId"/>, or null when absent.
    /// </summary>
    Task<SiteDirectoryEntry?> FindSiteByCodeAsync(
        TenantId tenant,
        string siteCode,
        OrganizationId? institutionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all study-site links for <paramref name="studyId"/> in <paramref name="tenant"/>, ordered by site code.</summary>
    Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one study-site link in <paramref name="tenant"/> by its identifier, or null when absent.</summary>
    Task<StudySiteDirectoryEntry?> FindStudySiteAsync(
        TenantId tenant,
        StudySiteId studySiteId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the study-site link connecting <paramref name="siteId"/> to <paramref name="studyId"/>, or null when unlinked.</summary>
    Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(
        TenantId tenant,
        StudyId studyId,
        SiteId siteId,
        CancellationToken cancellationToken = default);
}

/// <summary>A study row in a tenant's study directory.</summary>
/// <param name="StudyId">The study's kernel identifier.</param>
/// <param name="VersionIndex">The study's current version index.</param>
/// <param name="StudyCode">Unique human-facing study code within the tenant.</param>
/// <param name="Title">Display title of the study.</param>
/// <param name="Phase">Clinical phase label.</param>
/// <param name="SponsorId">Sponsor organization identifier.</param>
/// <param name="SponsorVersionIndex">Sponsor organization's current version index.</param>
/// <param name="SponsorCode">Sponsor organization's code.</param>
/// <param name="SponsorName">Sponsor organization's display name.</param>
/// <param name="SiteCount">Number of linked study sites.</param>
public sealed record StudyDirectoryEntry(
    StudyId StudyId,
    long VersionIndex,
    string StudyCode,
    string Title,
    string Phase,
    OrganizationId SponsorId,
    long SponsorVersionIndex,
    string SponsorCode,
    string SponsorName,
    int SiteCount);

/// <summary>An organization row in a tenant's organization directory.</summary>
/// <param name="OrganizationId">The organization's kernel identifier.</param>
/// <param name="VersionIndex">The organization's current version index.</param>
/// <param name="Code">Unique human-facing organization code within the tenant.</param>
/// <param name="Name">Display name of the organization.</param>
/// <param name="Kind">Whether the organization is a sponsor, CRO, or site institution.</param>
public sealed record OrganizationDirectoryEntry(
    OrganizationId OrganizationId,
    long VersionIndex,
    string Code,
    string Name,
    OrganizationKind Kind);

/// <summary>An investigational site row in a tenant's site directory.</summary>
/// <param name="SiteId">The site's kernel identifier.</param>
/// <param name="VersionIndex">The site's current version index.</param>
/// <param name="InstitutionId">Owning site institution organization identifier.</param>
/// <param name="InstitutionVersionIndex">Owning site institution's current version index.</param>
/// <param name="InstitutionCode">Owning site institution's code.</param>
/// <param name="InstitutionName">Owning site institution's display name.</param>
/// <param name="SiteCode">Unique site code within the institution/tenant.</param>
/// <param name="Name">Display name of the investigational site.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
/// <param name="IsActive">Whether the site is active.</param>
/// <param name="LinkedStudyCount">Number of studies linked to this site.</param>
public sealed record SiteDirectoryEntry(
    SiteId SiteId,
    long VersionIndex,
    OrganizationId InstitutionId,
    long InstitutionVersionIndex,
    string InstitutionCode,
    string InstitutionName,
    string SiteCode,
    string Name,
    string Country,
    bool IsActive = true,
    int LinkedStudyCount = 0);

/// <summary>A study-site association row in a study's linked-site directory.</summary>
/// <param name="StudySiteId">The study-site's kernel identifier.</param>
/// <param name="VersionIndex">The study-site's current version index.</param>
/// <param name="StudyId">Linked study identifier.</param>
/// <param name="SiteId">Linked site identifier.</param>
/// <param name="SiteVersionIndex">Linked site's current version index.</param>
/// <param name="SiteCode">Linked site's human-facing code.</param>
/// <param name="SiteName">Linked site's display name.</param>
/// <param name="Country">Linked site's ISO 3166-1 alpha-2 country code.</param>
/// <param name="InstitutionId">Owning site institution identifier.</param>
/// <param name="InstitutionVersionIndex">Owning site institution's current version index.</param>
/// <param name="InstitutionCode">Owning site institution's code.</param>
/// <param name="InstitutionName">Owning site institution's display name.</param>
/// <param name="Status">Current lifecycle status (<c>Selected</c> or <c>Active</c>).</param>
/// <param name="StudyCode">Linked study human-facing code.</param>
/// <param name="StudyVersionIndex">Linked study's current version index.</param>
/// <param name="IsActive">Whether the study-site link is active.</param>
public sealed record StudySiteDirectoryEntry(
    StudySiteId StudySiteId,
    long VersionIndex,
    StudyId StudyId,
    SiteId SiteId,
    long SiteVersionIndex,
    string SiteCode,
    string SiteName,
    string Country,
    OrganizationId InstitutionId,
    long InstitutionVersionIndex,
    string InstitutionCode,
    string InstitutionName,
    string Status,
    string StudyCode = "",
    long StudyVersionIndex = 1,
    bool IsActive = true);
