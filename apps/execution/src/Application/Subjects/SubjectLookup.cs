using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.References;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Resolves a <see cref="ClinicalSubject"/> by canonical <c>enrollment:&lt;uuid&gt;</c> or legacy
/// <c>participant:&lt;uuid&gt;</c> <see cref="NodeRef"/>, surrogate UUID, persisted kernel enrollment UUID,
/// or study-local subject code.
/// </summary>
internal static class SubjectLookup
{
    public static async Task<ClinicalSubject?> ResolveAsync(
        ISubjectRepository subjects,
        string subjectIdOrCode,
        CancellationToken ct)
    {
        if (NodeRef.TryParse(subjectIdOrCode, out var nodeRef) && nodeRef is not null)
        {
            return nodeRef.Kind switch
            {
                NodeKinds.Enrollment => await subjects.GetByKernelEnrollmentIdAsync(nodeRef.Id, ct).ConfigureAwait(false),
                NodeKinds.Participant => await subjects.GetByIdAsync(nodeRef.Id.ToString("D"), ct).ConfigureAwait(false),
                _ => null,
            };
        }

        var byId = await subjects.GetByIdAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (byId is not null)
        {
            return byId;
        }

        if (Guid.TryParse(subjectIdOrCode, out var maybeEnrollmentId))
        {
            var byEnrollment = await subjects.GetByKernelEnrollmentIdAsync(maybeEnrollmentId, ct).ConfigureAwait(false);
            if (byEnrollment is not null)
            {
                return byEnrollment;
            }
        }

        return await subjects.GetByCodeAsync(subjectIdOrCode, ct).ConfigureAwait(false);
    }
}
