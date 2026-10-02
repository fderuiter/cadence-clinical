using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Sentinels;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// Cache service interface for study statistical baseline parameters (Benford digit frequencies and field variances) with short TTL.
/// @req:PRD-SYS-174
/// </summary>
public interface IFraudBaselineCache
{
    Task<StatisticalBaseline> GetOrFetchBaselineAsync(StudyCode studyId, CancellationToken cancellationToken);

    void InvalidateBaseline(StudyCode studyId);
}
