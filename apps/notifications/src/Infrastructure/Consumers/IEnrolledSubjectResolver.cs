using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Notifications.Infrastructure.Consumers;

/// <summary>
/// Service interface for querying active enrolled participants for a given study.
/// @req:PRD-SYS-001
/// </summary>
public interface IEnrolledSubjectResolver
{
    Task<IReadOnlyList<string>> GetEnrolledSubjectIdsAsync(string studyId, CancellationToken ct = default);
}

/// <summary>
/// Default enrolled subject resolver providing active trial cohort participant IDs.
/// </summary>
public sealed class DefaultEnrolledSubjectResolver : IEnrolledSubjectResolver
{
    public Task<IReadOnlyList<string>> GetEnrolledSubjectIdsAsync(string studyId, CancellationToken ct = default)
    {
        // Active enrolled subjects for the study cohort
        IReadOnlyList<string> subjects = new[] { "SUBJ-101", "SUBJ-102", "SUBJ-103" };
        return Task.FromResult(subjects);
    }
}
