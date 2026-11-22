using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// Synchronizes validated site activation from Execution into the Clinical Kernel's <c>StudySite</c> aggregate
/// (<c>POST /api/v1/kernel/study-sites/activate</c>, PRD-SSU-003).
/// </summary>
public interface IKernelSiteActivationSynchronizer
{
    Task<Result<bool>> SynchronizeSiteActivationAsync(
        string studyCode,
        string siteCode,
        string changeReason,
        CancellationToken cancellationToken);
}
