using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// Outbound port synchronizing Execution's <c>SITE_ACTIVATION</c> compliance gate with the Clinical Kernel's
/// <c>ActivateStudySite</c> lifecycle (<c>K-TIME-4</c>, <c>K-XS-1</c>).
/// </summary>
public interface IKernelSiteActivationSynchronizer
{
    /// <summary>
    /// Synchronizes activation of <paramref name="siteCode"/> in <paramref name="studyCode"/> with the Clinical Kernel.
    /// Returns a failed <see cref="Result{T}"/> when the kernel refuses activation (for example, when no protocol
    /// version is effective for the study site under <c>K-TIME-4</c>).
    /// </summary>
    Task<Result<bool>> SynchronizeSiteActivationAsync(
        string studyCode,
        string siteCode,
        string changeReason,
        CancellationToken cancellationToken);
}
