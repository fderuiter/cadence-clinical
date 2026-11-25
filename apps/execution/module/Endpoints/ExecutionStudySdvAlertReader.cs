using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Modules.Execution.Infrastructure;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// Execution-module adapter implementing <see cref="IStudySdvAlertReader"/> over the shared batched SDV alert query
/// pipeline so <c>GET /api/v1/kernel/studies/{studyRef}/attention</c> embeds active SDV alerts in Wave 1 without
/// crossing module boundaries (<c>K-XS-1</c>, <c>K-XS-2</c>, <c>K-MOD-1</c>).
/// </summary>
internal sealed class ExecutionStudySdvAlertReader(
    IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
    IObservationRecords observations,
    ICollectionInstanceRecords forms,
    IKernelNavigator navigator,
    IKernelReadAccess? access = null) : IStudySdvAlertReader
{
    public Task<IReadOnlyList<StudySdvAlertView>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        string? knownStudyCode = null,
        CancellationToken cancellationToken = default) =>
        access is null
            ? Task.FromResult<IReadOnlyList<StudySdvAlertView>>([])
            : FormSdvAlertEndpoints.ReadVisibleStudyAlertsAsync(
                tenant,
                study,
                knownStudyCode,
                contexts,
                observations,
                forms,
                access,
                navigator,
                cancellationToken);
}
