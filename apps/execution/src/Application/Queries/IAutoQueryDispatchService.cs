using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Service abstraction for automated query dispatch and auto-resolution.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public interface IAutoQueryDispatchService
{
    Task<IReadOnlyList<ClinicalQuery>> ProcessViolationsAndAutoResolutionAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        SiteCode? siteId,
        string? visitId,
        string? formId,
        IReadOnlyList<ClinicalObservation> observations,
        IReadOnlyList<EditCheckViolation> violations,
        CancellationToken cancellationToken);
}
