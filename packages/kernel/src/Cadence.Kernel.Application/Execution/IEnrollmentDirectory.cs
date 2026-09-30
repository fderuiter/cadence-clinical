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
    /// The next enrollment number in a study: one more than the highest assigned. Gap-free only when read under the
    /// study's lock by the command that assigns it.
    /// </summary>
    Task<int> NextEnrollmentNumberAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken);
}
