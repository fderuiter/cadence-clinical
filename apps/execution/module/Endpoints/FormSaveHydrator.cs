using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Execution.Endpoints;

internal static class FormSaveHydrator
{
    public static async Task<(FormResponse? Hydrated, HashSet<string>? BlindedItemIds, HashSet<ObservationId>? BlindedObservationIds)> HydrateAfterSaveAsync(
        TenantId tenant,
        Guid formId,
        CollectionInstanceId id,
        ProtocolDesign? governingDesign,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext>? contexts,
        IKernelReadAccess? access,
        IServiceProvider? requestServices,
        CancellationToken cancellationToken)
    {
        FormResponse? hydrated = null;
        HashSet<string>? blindedItemIds = null;
        HashSet<ObservationId>? blindedObservationIds = null;
        var canUnblindSave = false;
        var evaluatedSaveUnblind = false;
        var updatedRecord = access is null
            ? await forms.FindForReaderAsync(
                tenant,
                id,
                governingDesign,
                (_, _) => ValueTask.FromResult(false),
                cancellationToken).ConfigureAwait(false)
            : await forms.FindForReaderAsync(
                tenant,
                id,
                governingDesign,
                async (enrollmentId, ct) =>
                {
                    canUnblindSave = await access.CanUnblindAsync(tenant, enrollmentId, ct).ConfigureAwait(false);
                    evaluatedSaveUnblind = true;
                    return canUnblindSave;
                },
                cancellationToken).ConfigureAwait(false);

        if (updatedRecord is { } updated)
        {
            string[]? layout = null;
            int? openQueryCount = null;
            int? answeredQueryCount = null;
            if (contexts is not null)
            {
                await using var db = contexts.Create(tenant);
                layout = await db.FormInstances
                    .Where(f => f.Id == formId)
                    .Select(f => EF.Property<string[]>(f, ExecutionModuleDbContext.ItemOrderField))
                    .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                var rawQueries = await db.FormQueries.AsNoTracking()
                    .Where(q => q.FormId == updated.CollectionInstanceId)
                    .Select(q => new { q.KernelQueryId, q.Status })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var execQueries = rawQueries.Select(q => (q.KernelQueryId.Value, q.Status)).ToList();
                (openQueryCount, answeredQueryCount) = ExecutionQueryMerger.Merge(updated.Assertions, execQueries);
            }

            var enrollmentNode = NodeRef.For(updated.EnrollmentId);
            var canSign = access is not null
                && await access.CanExerciseAsync(
                    tenant,
                    FormEndpoints.SignFormCapability,
                    enrollmentNode,
                    cancellationToken).ConfigureAwait(false);
            if (access is not null)
            {
                if (!evaluatedSaveUnblind)
                {
                    canUnblindSave = await access.CanUnblindAsync(tenant, updated.EnrollmentId, cancellationToken).ConfigureAwait(false);
                }

                var classifier = requestServices?.GetService<IBlindedTreatmentClassifier>()
                    ?? BlindedTreatmentClassifier.Instance;
                if (ReferenceEquals(classifier, BlindedTreatmentClassifier.Instance)
                    && updated.BlindedItemIds is not null
                    && updated.BlindedObservationIds is not null)
                {
                    if (canUnblindSave)
                    {
                        blindedItemIds = [];
                        blindedObservationIds = [];
                    }
                    else
                    {
                        blindedItemIds = new HashSet<string>(updated.BlindedItemIds, StringComparer.Ordinal);
                        blindedObservationIds = new HashSet<ObservationId>(updated.BlindedObservationIds);
                    }
                }
                else
                {
                    var masks = await access.MaskAsync(tenant, FormBlindingAndTimingHelper.CollectFormNodes(updated), cancellationToken).ConfigureAwait(false);
                    (blindedItemIds, blindedObservationIds) = await FormBlindingAndTimingHelper.ResolveBlindedFormTargetsAsync(
                        tenant,
                        updated,
                        masks,
                        canUnblindSave,
                        requestServices,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else if (updated.BlindedItemIds is { Count: > 0 })
            {
                blindedItemIds = new HashSet<string>(updated.BlindedItemIds, StringComparer.Ordinal);
            }

            var saveAssessment = await FormBlindingAndTimingHelper.ResolveVisitTimingAsync(
                tenant,
                updated,
                updated.ProtocolVersionId,
                updated.PlannedActivityId,
                updated.CollectedAt,
                governingDesign,
                requestServices,
                cancellationToken).ConfigureAwait(false);

            hydrated = FormResponse.From(updated, layout, canSign, blindedItemIds, blindedObservationIds, openQueryCount, answeredQueryCount, saveAssessment);
        }

        return (hydrated, blindedItemIds, blindedObservationIds);
    }
}
