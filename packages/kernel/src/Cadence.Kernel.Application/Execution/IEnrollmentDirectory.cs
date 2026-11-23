using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;

namespace Cadence.Kernel.Application.Execution;

/// <summary>
/// Read-side queries over participants and enrollments (spec §3.2, §7.4). Reads are tenant-scoped by Row-Level
/// Security and never change state. Kernel persistence implements it (<c>AddKernelPersistence</c>).
/// </summary>
public interface IEnrollmentDirectory
{
    /// <summary>
    /// Resolves a subject code in a study to its enrollment, or null when the study has no such code (K-ID-5). This is
    /// the one place a code becomes an id; commands and events carry ids only.
    /// </summary>
    Task<EnrollmentState?> ResolveParticipantAsync(TenantId tenant, StudyId study, SubjectCode code, CancellationToken cancellationToken);

    /// <summary>An enrollment's current state, or null when it does not exist.</summary>
    Task<EnrollmentState?> FindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken);

    /// <summary>
    /// The enrollment's status at <paramref name="validAt"/> as Cadence knew it at <paramref name="knownAt"/>
    /// (spec §4.1), or null when it had none then (before screening, or not yet recorded).
    /// </summary>
    Task<EnrollmentStatus?> StatusAsOfAsync(
        TenantId tenant, EnrollmentId enrollment, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a study code and a site code in a tenant to the study and its active linked study site, or null when
    /// either does not exist or the site is not linked to the study.
    /// </summary>
    Task<(StudyId StudyId, StudySiteId StudySiteId)?> FindStudySiteAsync(
        TenantId tenant, string studyCode, string siteCode, CancellationToken cancellationToken);

    /// <summary>
    /// The next enrollment number in a study: one more than the highest assigned. Gap-free only when read under the
    /// study's lock by the command that assigns it.
    /// </summary>
    Task<int> NextEnrollmentNumberAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a study code, subject code, and optional site code in <paramref name="tenant"/> to pinned versioned
    /// study, linked study site, and enrollment summaries over the unique <c>(tenant_id, study_id, subject_code)</c>
    /// index, or null when absent or when <paramref name="siteCode"/> does not match the enrollment's linked site.
    /// </summary>
    Task<EnrollmentCodeLookupResult?> LookupByCodeAsync(
        TenantId tenant,
        string studyCode,
        SubjectCode subjectCode,
        string? siteCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pinned study, linked study site, and enrollment summaries resolved by code in a tenant before caller visibility
/// masking (<c>INodeVisibility</c>) is applied.
/// </summary>
/// <param name="StudyId">The study's kernel identifier.</param>
/// <param name="StudyVersion">The study's current version index.</param>
/// <param name="StudyCode">The study's human-facing code.</param>
/// <param name="StudyTitle">The study's display title.</param>
/// <param name="StudySiteId">The enrollment's linked study site identifier.</param>
/// <param name="StudySiteVersion">The linked study site's current version index.</param>
/// <param name="SiteCode">The linked site's human-facing code.</param>
/// <param name="SiteTitle">The linked site's display name.</param>
/// <param name="EnrollmentId">The enrollment's kernel identifier.</param>
/// <param name="EnrollmentVersion">The enrollment's current version index.</param>
/// <param name="SubjectCode">The study-scoped subject code.</param>
/// <param name="Status">The enrollment's current lifecycle status.</param>
/// <param name="EnrollmentNumber">The study-scoped sequential enrollment number once enrolled, or null in screening/screen failure.</param>
public sealed record EnrollmentCodeLookupResult(
    StudyId StudyId,
    long StudyVersion,
    string StudyCode,
    string StudyTitle,
    StudySiteId StudySiteId,
    long StudySiteVersion,
    string SiteCode,
    string SiteTitle,
    EnrollmentId EnrollmentId,
    long EnrollmentVersion,
    SubjectCode SubjectCode,
    EnrollmentStatus Status,
    int? EnrollmentNumber = null);
