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
