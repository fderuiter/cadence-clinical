using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

internal static class FormQueryMutationEndpoints
{
    private static readonly Capability QueryManageCapability = new("execution.query.manage", VerbClass.Execute);

    public static async Task<Results<Ok<FormQueryEscalationPassResponse>, ProblemHttpResult>> EscalateOverdueQueriesAsync(
        Guid? studyId,
        HttpContext context,
        IFormQueryEscalationService escalation,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelChangeReason changeReason,
        IKernelNavigator navigator,
        IClock clock,
        CancellationToken cancellationToken,
        ITenantEntitlementReader? entitlements = null)
    {
        if (studyId is not { } studyGuid || studyGuid == Guid.Empty)
        {
            return FormQueryFilterParser.BadRequest("Specify study_id to run query escalation.");
        }

        var idempotencyKey = FormEndpoints.IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var reason = changeReason.Read(context);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        var study = StudyId.FromGuid(studyGuid);
        var studyNode = NodeRef.For(study);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return TypedResults.Problem(title: "Not found", detail: $"Study {studyGuid:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await access.CanSeeAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false))
        {
            return FormQueryFilterParser.NotVisible($"study {studyGuid:D}");
        }

        if (!await access.CanExerciseAsync(tenant, QueryManageCapability, studyNode, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        if (entitlements is not null)
        {
            var snapshot = await entitlements.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (snapshot.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!snapshot.ProductFamilies.Contains(ProductFamily.Capture))
            {
                return KernelErrorEndpoints.Problem(
                    CommercialErrors.ProductFamilyNotEntitled.WithMessage(
                        $"{CommercialErrors.ProductFamilyNotEntitled.Message} Required product family: {ProductFamily.Capture}."));
            }
        }

        var result = await escalation.RunStudyPassAsync(tenant, study, reason, cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } outcome)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var hasStudyWideVisibility = await access.CoversScopeAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false);
        var siteNodes = outcome.Digests
            .Where(d => d.StudySiteId.HasValue)
            .Select(d => NodeRef.For(d.StudySiteId!.Value))
            .Distinct()
            .ToList();
        var visibleSites = siteNodes.Count > 0
            ? await access.FilterVisibleAsync(tenant, siteNodes, cancellationToken).ConfigureAwait(false)
            : new HashSet<NodeRef>();

        var scopedDigests = outcome.Digests
            .Where(d => d.StudySiteId is { } siteId
                ? visibleSites.Contains(NodeRef.For(siteId))
                : hasStudyWideVisibility)
            .ToList();
        var redactedEntryLists = await FormQueryDigestBlinding.RedactBatchAsync(
                tenant,
                [.. scopedDigests.Select(static d => d.Entries)],
                forms,
                contexts,
                access,
                alwaysMaskBlinded: false,
                cancellationToken)
            .ConfigureAwait(false);
        var shapedDigests = new List<FormQueryAgingDigestResponse>(scopedDigests.Count);
        for (var i = 0; i < scopedDigests.Count; i++)
        {
            shapedDigests.Add(FormQueryAgingDigestResponse.From(scopedDigests[i], redactedEntryLists[i]));
        }

        return TypedResults.Ok(new FormQueryEscalationPassResponse(outcome.EscalatedQueries, shapedDigests));
    }

    public static async Task<Results<Created<FormQueryResponse>, ProblemHttpResult>> RaiseAsync(
        Guid formId,
        RaiseFormQueryRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);

        if (!FormQueryFilterParser.TryParseOwnerRole(request?.OwnerRole, out var ownerRole))
        {
            return FormQueryFilterParser.BadRequest($"'{request?.OwnerRole}' is not an owner role: site, monitor or data_manager.");
        }

        if (!FormQueryFilterParser.TryParsePriority(request?.Priority, out var priority))
        {
            return FormQueryFilterParser.BadRequest($"'{request?.Priority}' is not a priority: low, normal or high.");
        }

        var key = FormEndpoints.IdempotencyKey(context);
        var reason = changeReason.Read(context) ?? string.Empty;
        CollectionInstanceHeader resolvedHeader;
        ObservationId targetObservation;
        int targetRevision;

        if ((request?.ObservationId is null || request.ObservationId == Guid.Empty) && !string.IsNullOrWhiteSpace(request?.ItemId))
        {
            var itemId = request.ItemId.Trim();
            if (await forms.FindQueryTargetAsync(
                tenant,
                form,
                itemId: itemId,
                checkLocks: false,
                cancellationToken: cancellationToken).ConfigureAwait(false) is not { } itemTarget)
            {
                return FormQueryFilterParser.FormNotFound(formId);
            }

            if (itemTarget.ObservationId is { } existingObsId && itemTarget.Revision is { } existingRevision)
            {
                if (!itemTarget.IsUnaskedPlaceholder)
                {
                    return KernelErrorEndpoints.Problem(
                        FormQueryErrors.RevisionStale.WithMessage(
                            $"Item {itemId} now holds a value at revision {existingRevision}; reload the form and query the current value."));
                }

                targetObservation = existingObsId;
                targetRevision = existingRevision;
            }
            else
            {
                var unaskedKey = string.IsNullOrWhiteSpace(key) ? string.Empty : $"{key}:unasked";
                var unasked = await sender.SendAsync(
                    new RecordUnaskedCollectionItem(tenant, form, itemTarget.EnrollmentId, itemId, ObservationId: null, unaskedKey, reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (unasked.Value is not { } seeded)
                {
                    return KernelErrorEndpoints.Problem(unasked.Error!);
                }

                targetObservation = seeded.ObservationId;
                targetRevision = seeded.Revision;
            }

            resolvedHeader = new CollectionInstanceHeader(
                itemTarget.CollectionInstanceId,
                itemTarget.StudyId,
                itemTarget.StudySiteId,
                itemTarget.EnrollmentId,
                itemTarget.CollectionSpecId,
                itemTarget.PlannedActivityId);
        }
        else
        {
            if (await forms.FindHeaderAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } header)
            {
                return FormQueryFilterParser.FormNotFound(formId);
            }

            resolvedHeader = header;
            targetObservation = ObservationId.FromGuid(request?.ObservationId ?? Guid.Empty);
            targetRevision = request?.Revision ?? 0;
        }

        var queryGuid = request?.QueryId is { } given && given != Guid.Empty ? given : Guid.CreateVersion7();
        var result = await sender.SendAsync(
            new RaiseFormQuery(
                tenant,
                FormQueryId.FromGuid(queryGuid),
                form,
                resolvedHeader.EnrollmentId,
                targetObservation,
                targetRevision,
                request?.Text ?? string.Empty,
                key,
                reason,
                Origin: FormQueryOrigin.Manual,
                OwnerRole: ownerRole ?? FormQueryOwnerRole.Site,
                Priority: priority ?? FormQueryPriority.Normal,
                Assignee: request?.Assignee,
                ResolvedHeader: resolvedHeader),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } state)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await RespondAsync(forms, navigator, access, tenant, state, result.Replayed, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"{FormEndpoints.RoutePrefix}/{formId:D}/queries/{state.QueryId.Value:D}", response);
    }

    public static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> AnswerAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new AnswerFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, request?.Text ?? string.Empty, key, reason, request?.Assignee), cancellationToken);

    public static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReopenAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new ReopenFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, request?.Text ?? string.Empty, key, reason, request?.Assignee), cancellationToken);

    public static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReassignAsync(
        Guid formId, Guid queryId, ReassignFormQueryRequest request, HttpContext context, FormQueryCommandServices services, CancellationToken cancellationToken)
    {
        if (!FormQueryFilterParser.TryParseOwnerRole(request?.OwnerRole, out var ownerRole))
        {
            return Task.FromResult<Results<Ok<FormQueryResponse>, ProblemHttpResult>>(
                FormQueryFilterParser.BadRequest($"'{request?.OwnerRole}' is not an owner role: site, monitor or data_manager."));
        }

        return StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new ReassignFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, request?.Assignee, key, reason, ownerRole, request?.Text), cancellationToken);
    }

    public static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CloseAsync(
        Guid formId, Guid queryId, HttpContext context, FormQueryCommandServices services, CancellationToken cancellationToken)
    {
        var note = await ReadOptionalNoteAsync(context, cancellationToken).ConfigureAwait(false);
        return await StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CloseFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, key, reason, note?.Text), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CancelAsync(
        Guid formId, Guid queryId, HttpContext context, FormQueryCommandServices services, CancellationToken cancellationToken)
    {
        var note = await ReadOptionalNoteAsync(context, cancellationToken).ConfigureAwait(false);
        return await StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CancelFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, key, reason, note?.Text), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FormQueryNoteRequest?> ReadOptionalNoteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength == 0)
        {
            return null;
        }

        return await context.Request.ReadFromJsonAsync<FormQueryNoteRequest>(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> StepAsync(
        Guid formId,
        Guid queryId,
        HttpContext context,
        FormQueryCommandServices services,
        Func<TenantId, EnrollmentId, string, string, ICommand<FormQueryState>> command,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);
        var typedQueryId = FormQueryId.FromGuid(queryId);
        EnrollmentId? enrollmentId;
        await using (var db = services.Contexts.Create(tenant))
        {
            enrollmentId = await db.FormQueries.AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(q => q.Id == typedQueryId && q.FormId == form)
                .Select(q => (EnrollmentId?)q.EnrollmentId)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (enrollmentId is not { } resolvedEnrollment)
        {
            return TypedResults.Problem(
                title: "Not found", detail: $"Form {formId:D} has no query {queryId:D}.", statusCode: StatusCodes.Status404NotFound);
        }

        var result = await services.Sender.SendAsync(
            command(tenant, resolvedEnrollment, FormEndpoints.IdempotencyKey(context), services.ChangeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Ok(await RespondAsync(services.Forms, services.Navigator, services.Access, tenant, state, result.Replayed, cancellationToken).ConfigureAwait(false))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    private static async Task<FormQueryResponse> RespondAsync(
        ICollectionInstanceRecords forms,
        IKernelNavigator navigator,
        IKernelReadAccess access,
        TenantId tenant,
        FormQueryState state,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var canUnblind = await access.CanUnblindAsync(tenant, state.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (!replayed && state.Scope is { } scope)
        {
            var maskFromScope = false;
            if (!canUnblind)
            {
                var projections = await forms.FindObservationProjectionsAsync(
                    tenant,
                    [state.ObservationId],
                    [state.FormId],
                    cancellationToken).ConfigureAwait(false);
                var obsBlinded = projections.TryGetValue(state.ObservationId, out var proj) && proj.IsBlinded;
                maskFromScope = obsBlinded
                    || BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(scope.CollectionSpecId)
                    || BlindedTreatmentClassifier.MatchesMarkers(state.ItemId);
            }

            var contextFromScope = await FormQueryStoreReader.BuildFormContextAsync(
                navigator,
                tenant,
                scope.StudyId,
                scope.StudySiteId,
                state.EnrollmentId,
                scope.CollectionSpecId,
                scope.PlannedActivityId,
                cancellationToken).ConfigureAwait(false);
            return FormQueryResponse.From(
                state,
                new Dictionary<ObservationId, int>(),
                contextFromScope,
                maskBlinded: maskFromScope,
                currentRevisionOverride: scope.CurrentRevision);
        }

        IReadOnlySet<EnrollmentId> unblindedSet = canUnblind
            ? new HashSet<EnrollmentId> { state.EnrollmentId }
            : FrozenSet<EnrollmentId>.Empty;
        var targets = await forms.FindQueryTargetsAsync(tenant, [state.FormId], unblindedSet, cancellationToken).ConfigureAwait(false);
        targets.TryGetValue(state.FormId, out var target);
        var queryContext = target is null
            ? null
            : await FormQueryStoreReader.BuildFormContextAsync(
                navigator,
                tenant,
                target.StudyId,
                target.StudySiteId,
                target.EnrollmentId,
                target.CollectionSpecId,
                target.PlannedActivityId,
                cancellationToken).ConfigureAwait(false);
        var maskBlinded = target is not null
            && !canUnblind
            && target.IsQueryBlinded(state.ItemId, state.ObservationId);
        int? itemRevision = target is not null && target.ItemRevisions.TryGetValue(state.ItemId, out var foundRev)
            ? foundRev
            : null;
        return FormQueryResponse.From(
            state,
            target?.ObservationRevisions ?? new Dictionary<ObservationId, int>(),
            queryContext,
            maskBlinded,
            itemRevision);
    }
}
