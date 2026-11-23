using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// eCRF forms (ADR-2622): open a form for a participant's visit, list an enrollment's opened forms, read a form with its
/// values, locks, assertions and signatures, and save a page of values. Every write is a kernel command sent through
/// <see cref="IKernelCommandSender"/>, so the kernel authorizes it, audits it and keeps the values as observations; the
/// reason comes from the signed identity, the idempotency key from the <c>Idempotency-Key</c> header, and the tenant from
/// <see cref="KernelTenantFilter"/>.
/// </summary>
internal static class FormEndpoints
{
    /// <summary>The route prefix of the form endpoints, which the gateway sends to the kernel host.</summary>
    public const string RoutePrefix = "/api/v1/execution/forms";

    /// <summary>The header carrying the command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly Capability SignFormCapability = new("collection.sign", VerbClass.Execute);
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(RoutePrefix)
            .RequireSignedInCaller("Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution forms");

        group.MapGet(string.Empty, ListForEnrollmentAsync)
            .WithName("ExecutionListEnrollmentForms")
            .WithSummary("Lists the forms opened for an enrollment, oldest first.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost(string.Empty, OpenAsync)
            .WithName("ExecutionOpenForm")
            .WithSummary("Opens a form for a participant's visit under the protocol version in force (collection.open).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<OpenCollectionInstance>();

        group.MapGet("/{formId:guid}", GetAsync)
            .WithName("ExecutionGetForm")
            .WithSummary("Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{formId:guid}/values", SaveAsync)
            .WithName("ExecutionSaveFormValues")
            .WithSummary("Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<RecordCollectedValues>();

        group.MapPost("/{formId:guid}/preview", PreviewAsync)
            .WithName("ExecutionPreviewFormValues")
            .WithSummary("Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
                KernelErrors.ActorUnresolved,
                KernelErrors.AuthorizationDenied,
                KernelErrors.NotFound);

        return endpoints;
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormSummaryResponse>>, ProblemHttpResult>> ListForEnrollmentAsync(
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        CancellationToken cancellationToken,
        [FromServices] IModuleTenantContextFactory<ExecutionModuleDbContext>? contexts = null)
    {
        if (enrollmentId is not { } id || id == Guid.Empty)
        {
            return BadRequest("Invalid request", "Name the enrollment with enrollment_id.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var enrollment = EnrollmentId.FromGuid(id);

        // An enrollment's forms are seen only by callers who may see the enrollment itself (ADR-2457).
        if (!await access.CanSeeAsync(tenant, NodeRef.For(enrollment), cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Not visible", detail: $"You may not see enrollment {id:D}.", statusCode: StatusCodes.Status403Forbidden);
        }

        var summaries = await forms.ListForEnrollmentAsync(tenant, enrollment, cancellationToken).ConfigureAwait(false);
        if (summaries.Count == 0 || contexts is null)
        {
            return TypedResults.Ok<IReadOnlyList<FormSummaryResponse>>([.. summaries.Select(s => FormSummaryResponse.From(s))]);
        }

        var records = await forms.ListRecordsForEnrollmentAsync(tenant, enrollment, cancellationToken).ConfigureAwait(false);
        var assertionsByForm = records.ToDictionary(r => r.CollectionInstanceId, r => r.Assertions);

        Dictionary<CollectionInstanceId, List<(Guid KernelQueryId, FormQueryStatus Status)>> queriesByForm;
        await using (var db = contexts.Create(tenant))
        {
            var allFormQueries = await db.FormQueries.AsNoTracking()
                .Where(q => q.EnrollmentId == enrollment)
                .Select(q => new { q.FormId, q.KernelQueryId, q.Status })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            queriesByForm = allFormQueries
                .GroupBy(q => q.FormId)
                .ToDictionary(g => g.Key, g => g.Select(x => (x.KernelQueryId.Value, x.Status)).ToList());
        }

        return TypedResults.Ok<IReadOnlyList<FormSummaryResponse>>([
            .. summaries.Select(s =>
            {
                var execQueries = queriesByForm.GetValueOrDefault(s.CollectionInstanceId) ?? [];
                if (assertionsByForm.TryGetValue(s.CollectionInstanceId, out var kernelAssertions))
                {
                    var (openCount, answeredCount) = ExecutionQueryMerger.Merge(kernelAssertions, execQueries);
                    return FormSummaryResponse.From(s, openCount, answeredCount);
                }

                var fallbackOpen = Math.Max(s.OpenQueryCount, execQueries.Count(q => q.Status == FormQueryStatus.Open));
                var fallbackAnswered = Math.Max(s.AnsweredQueryCount, execQueries.Count(q => q.Status == FormQueryStatus.Answered));
                return FormSummaryResponse.From(s, fallbackOpen, fallbackAnswered);
            }),
        ]);
    }

    internal static async Task<Results<Created<FormStateResponse>, ProblemHttpResult>> OpenAsync(
        OpenFormRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null || request.EnrollmentId == Guid.Empty || string.IsNullOrWhiteSpace(request.CollectionSpecId))
        {
            return BadRequest("Invalid form", "Name the enrollment and the CollectionSpec the form collects.");
        }

        var form = request.FormId is { } given && given != Guid.Empty ? CollectionInstanceId.FromGuid(given) : CollectionInstanceId.New();
        var result = await sender.SendAsync(
            new OpenCollectionInstance(
                KernelRequestTenant.Get(context),
                form,
                EnrollmentId.FromGuid(request.EnrollmentId),
                request.CollectionSpecId,
                request.PlannedActivityId,
                request.CollectedAt,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Created($"{RoutePrefix}/{state.CollectionInstanceId.Value:D}", FormStateResponse.From(state))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<FormResponse>, ProblemHttpResult>> GetAsync(
        Guid formId,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        if (await forms.FindAsync(tenant, CollectionInstanceId.FromGuid(formId), cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return NotFound(formId);
        }

        var enrollmentNode = NodeRef.For(record.EnrollmentId);
        var nodesToCheck = CollectFormNodes(record);

        // A form is seen by whoever may see its enrollment (ADR-2457); blinded observations are masked in place (K-AUTH-4).
        var masks = await access.MaskAsync(tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
        if (masks.ContainsKey(enrollmentNode))
        {
            return TypedResults.Problem(
                title: "Not visible", detail: $"You may not see form {formId:D}.", statusCode: StatusCodes.Status403Forbidden);
        }

        // The layout the module snapshotted when the kernel opened the form; until that event is delivered, the
        // specification's own order, which is the same.
        string[]? layout;
        List<(Guid KernelQueryId, FormQueryStatus Status)> execQueries;
        await using (var db = contexts.Create(tenant))
        {
            layout = await db.FormInstances
                .Where(f => f.Id == formId)
                .Select(f => EF.Property<string[]>(f, ExecutionModuleDbContext.ItemOrderField))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var rawQueries = await db.FormQueries.AsNoTracking()
                .Where(q => q.FormId == record.CollectionInstanceId)
                .Select(q => new { q.KernelQueryId, q.Status })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            execQueries = [.. rawQueries.Select(q => (q.KernelQueryId.Value, q.Status))];
        }

        var (openQueryCount, answeredQueryCount) = ExecutionQueryMerger.Merge(record.Assertions, execQueries);
        var canSign = await access.CanExerciseAsync(
            tenant,
            SignFormCapability,
            enrollmentNode,
            cancellationToken).ConfigureAwait(false);
        var canUnblind = await access.CanExerciseAsync(
            tenant,
            EnrollmentUnblindCapability,
            enrollmentNode,
            cancellationToken).ConfigureAwait(false);
        var (blindedItemIds, blindedObservationIds) = await ResolveBlindedFormTargetsAsync(
            tenant,
            record,
            masks,
            canUnblind,
            context.RequestServices,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(FormResponse.From(record, layout, canSign, blindedItemIds, blindedObservationIds, openQueryCount, answeredQueryCount));
    }

    internal static async Task<Results<Ok<FormPreviewResponse>, ProblemHttpResult>> PreviewAsync(
        Guid formId,
        PreviewFormValuesRequest? request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request?.Values is null || request.Values.Any(v => v is null || string.IsNullOrWhiteSpace(v.ItemId)))
        {
            return BadRequest("Invalid preview", "Provide values as an array of draft items naming item_id.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = CollectionInstanceId.FromGuid(formId);
        if (await forms.FindAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return NotFound(formId);
        }

        var enrollmentNode = NodeRef.For(record.EnrollmentId);
        var previewNodes = new List<NodeRef>(1 + record.Values.Count) { enrollmentNode };
        previewNodes.AddRange(record.Values.Select(v => NodeRef.For(v.ObservationId)));
        var masks = await access.MaskAsync(tenant, previewNodes, cancellationToken).ConfigureAwait(false);
        if (masks.ContainsKey(enrollmentNode))
        {
            return TypedResults.Problem(
                title: "Not visible", detail: $"You may not see form {formId:D}.", statusCode: StatusCodes.Status403Forbidden);
        }

        var validation = context.RequestServices?.GetService<ICollectionValidationService>();
        if (validation is null)
        {
            return TypedResults.Ok(new FormPreviewResponse(formId, []));
        }

        var canUnblind = await access.CanExerciseAsync(
            tenant,
            EnrollmentUnblindCapability,
            enrollmentNode,
            cancellationToken).ConfigureAwait(false);
        var unblinded = canUnblind && !masks.Values.Any(reason => reason == MaskReason.Blinded);

        var overlay = new Dictionary<string, ObservationValue?>(StringComparer.Ordinal);
        foreach (var entry in request.Values)
        {
            overlay[entry.ItemId.Trim()] = entry.Value;
        }

        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var warnings = await validation.PreviewFormAsync(tenant, record, overlay, asOf, unblinded, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new FormPreviewResponse(
            formId,
            [.. warnings.Select(FormPreviewWarningResponse.From)]));
    }

    internal static async Task<Results<Ok<FormSavedResponse>, ProblemHttpResult>> SaveAsync(
        Guid formId,
        SaveFormValuesRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken,
        [FromServices] IModuleTenantContextFactory<ExecutionModuleDbContext>? contexts = null,
        [FromServices] IKernelReadAccess? access = null)
    {
        if (request?.Values is not { Count: > 0 } values
            || values.Any(v => v is null || string.IsNullOrWhiteSpace(v.ItemId) || v.Value is null || v.ExpectedRevision < 0))
        {
            return BadRequest("Invalid save", "Send at least one value, each naming its item, its value and the revision it was edited from.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = CollectionInstanceId.FromGuid(formId);

        // The save is authorized against the form's enrollment, which the client does not send.
        if (await forms.FindHeaderAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } header)
        {
            return NotFound(formId);
        }

        ProtocolDesign? governingDesign = null;
        CommandResult<CollectedValuesState> result;
        using (ProtocolDesignDocument.OnRead(design => governingDesign ??= design))
        {
            result = await sender.SendAsync(
                new RecordCollectedValues(
                    tenant,
                    id,
                    header.EnrollmentId,
                    [.. values.Select(v => new CollectedValue(
                        v.ItemId,
                        v.Value!,
                        v.ExpectedRevision,
                        v.ObservationId is { } observation ? ObservationId.FromGuid(observation) : null,
                        v.ObservedAt))],
                    IdempotencyKey(context),
                    changeReason.Read(context) ?? string.Empty),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (result.Value is not { } state)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        FormResponse? hydrated = null;
        HashSet<string>? blindedItemIds = null;
        HashSet<ObservationId>? blindedObservationIds = null;
        if (await forms.FindAsync(tenant, id, governingDesign, cancellationToken).ConfigureAwait(false) is { } updated)
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
                    SignFormCapability,
                    enrollmentNode,
                    cancellationToken).ConfigureAwait(false);
            if (access is not null)
            {
                var canUnblind = await access.CanExerciseAsync(
                    tenant,
                    EnrollmentUnblindCapability,
                    enrollmentNode,
                    cancellationToken).ConfigureAwait(false);
                var classifier = context.RequestServices?.GetService<IBlindedTreatmentClassifier>()
                    ?? BlindedTreatmentClassifier.Instance;
                if (ReferenceEquals(classifier, BlindedTreatmentClassifier.Instance)
                    && updated.BlindedItemIds is not null
                    && updated.BlindedObservationIds is not null)
                {
                    if (canUnblind)
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
                    var masks = await access.MaskAsync(tenant, CollectFormNodes(updated), cancellationToken).ConfigureAwait(false);
                    (blindedItemIds, blindedObservationIds) = await ResolveBlindedFormTargetsAsync(
                        tenant,
                        updated,
                        masks,
                        canUnblind,
                        context.RequestServices,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            hydrated = FormResponse.From(updated, layout, canSign, blindedItemIds, blindedObservationIds, openQueryCount, answeredQueryCount);
        }

        return TypedResults.Ok(FormSavedResponse.From(state, hydrated, blindedItemIds, blindedObservationIds));
    }

    internal static string IdempotencyKey(HttpContext context) => context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();

    private static List<NodeRef> CollectFormNodes(CollectionInstanceRecord record)
    {
        var nodes = new List<NodeRef> { NodeRef.For(record.EnrollmentId) };
        nodes.AddRange(record.Values.Select(v => NodeRef.For(v.ObservationId)));
        nodes.AddRange(record.Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!)
            .Select(sv => NodeRef.For(sv.ObservationId)));
        nodes.AddRange(record.Assertions.Where(a => a.Target.Kind == NodeKinds.Observation).Select(a => a.Target));
        nodes.AddRange(record.Locks.Where(l => l.Scope.Kind == NodeKinds.Observation).Select(l => l.Scope));
        return nodes;
    }

    private static async Task<(HashSet<string> BlindedItemIds, HashSet<ObservationId> BlindedObservationIds)> ResolveBlindedFormTargetsAsync(
        TenantId tenant,
        CollectionInstanceRecord record,
        IReadOnlyDictionary<NodeRef, MaskReason> masks,
        bool canUnblind,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var blindedObservationIds = new HashSet<ObservationId>(
            masks.Keys
                .Where(n => n.Kind == NodeKinds.Observation)
                .Select(n => ObservationId.FromGuid(n.Id)));
        var blindedItemIds = record.Values
            .Where(recorded => blindedObservationIds.Contains(recorded.ObservationId))
            .Select(recorded => recorded.ItemId)
            .ToHashSet(StringComparer.Ordinal);

        if (!canUnblind)
        {
            var classifier = services?.GetService<IBlindedTreatmentClassifier>()
                ?? BlindedTreatmentClassifier.Instance;
            var designs = await LoadStudyDesignsAsync(tenant, record.StudyId, record.ProtocolVersionId, services, cancellationToken).ConfigureAwait(false);
            var concepts = await LoadItemConceptsAsync(tenant, record.Items, services, cancellationToken).ConfigureAwait(false);
            var specById = record.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
            var valuesById = record.Values.ToDictionary(v => v.ItemId, StringComparer.Ordinal);

            foreach (var itemId in specById.Keys.Concat(valuesById.Keys).Distinct(StringComparer.Ordinal))
            {
                specById.TryGetValue(itemId, out var specItem);
                valuesById.TryGetValue(itemId, out var recordedValue);
                BlindedConceptMetadata? conceptMeta = specItem is not null && concepts.TryGetValue(specItem.Concept.ConceptId, out var m)
                    ? m
                    : null;
                if (classifier.IsBlindedCollectionItem(
                        record.CollectionSpecId,
                        record.CollectionTitle,
                        specItem,
                        recordedValue?.Value,
                        conceptMeta,
                        designs))
                {
                    blindedItemIds.Add(itemId);
                    if (recordedValue is not null)
                    {
                        blindedObservationIds.Add(recordedValue.ObservationId);
                    }
                }
            }

            foreach (var signedValue in record.Attestations
                .Where(signature => signature.SignedValues is not null)
                .SelectMany(signature => signature.SignedValues!))
            {
                specById.TryGetValue(signedValue.ItemId, out var specItem);
                BlindedConceptMetadata? conceptMeta = specItem is not null && concepts.TryGetValue(specItem.Concept.ConceptId, out var m)
                    ? m
                    : null;
                if (blindedItemIds.Contains(signedValue.ItemId)
                    || classifier.IsBlindedCollectionItem(
                        record.CollectionSpecId,
                        record.CollectionTitle,
                        specItem,
                        signedValue.Value,
                        conceptMeta,
                        designs))
                {
                    blindedItemIds.Add(signedValue.ItemId);
                    blindedObservationIds.Add(signedValue.ObservationId);
                }
            }
        }

        return (blindedItemIds, blindedObservationIds);
    }

    private static async Task<IReadOnlyList<ProtocolDesign>> LoadStudyDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        ProtocolVersionId governingVersionId,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var reader = services?.GetService<IProtocolVersionReader>();
        if (reader is null)
        {
            return [];
        }

        var designs = new List<ProtocolDesign>();
        if (await reader.GetDesignAsync(tenant, governingVersionId, cancellationToken).ConfigureAwait(false) is { } governing)
        {
            designs.Add(governing);
            return designs;
        }

        var summaries = await reader.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        foreach (var summaryId in summaries.Select(summary => summary.Id))
        {
            if (await reader.GetDesignAsync(tenant, summaryId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                designs.Add(design);
            }
        }

        return designs;
    }

    private static async Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadItemConceptsAsync(
        TenantId tenant,
        IReadOnlyList<CollectionItem> items,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var concepts = services?.GetService<IConceptRegistry>();
        if (concepts is null || items.Count == 0)
        {
            return new Dictionary<ConceptId, BlindedConceptMetadata>();
        }

        var resolved = new Dictionary<ConceptId, BlindedConceptMetadata>();
        foreach (var conceptRef in items.Select(i => i.Concept).Distinct())
        {
            var view = await concepts.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false)
                ?? await concepts.GetCurrentAsync(tenant, conceptRef.ConceptId, cancellationToken).ConfigureAwait(false);
            if (view is not null)
            {
                resolved[conceptRef.ConceptId] = new BlindedConceptMetadata(view.ExternalCode, view.PreferredLabel, view.Definition);
            }
        }

        return resolved;
    }

    private static ProblemHttpResult BadRequest(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(Guid formId) =>
        TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
}
