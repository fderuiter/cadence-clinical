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
    /// Resolves a study code and site code to the linked <c>StudySite</c> summary including its operational status
    /// (<c>Selected</c>, <c>Active</c>, etc.), or null when either does not exist or is not linked.
    /// </summary>
    Task<StudySiteLookup?> FindStudySiteInfoAsync(
        TenantId tenant, string studyCode, string siteCode, CancellationToken cancellationToken);

    /// <summary>Reads a <c>StudySite</c> summary by id, or null when it does not exist.</summary>
    Task<StudySiteLookup?> FindStudySiteByIdAsync(
        TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken);

    /// <summary>Resolves a study code in a tenant to its <see cref="StudyId"/>, or null when not found.</summary>
    Task<StudyId?> FindStudyByCodeAsync(
        TenantId tenant, string studyCode, CancellationToken cancellationToken);

    /// <summary>
    /// The next enrollment number in a study: one more than the highest assigned. Gap-free only when read under the
    /// study's lock by the command that assigns it.
    /// </summary>
    Task<int> NextEnrollmentNumberAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken);
}

/// <summary>Read-side summary of a linked <c>StudySite</c>.</summary>
public sealed record StudySiteLookup(
    StudyId StudyId,
    StudySiteId StudySiteId,
    SiteId SiteId,
    string Status);
