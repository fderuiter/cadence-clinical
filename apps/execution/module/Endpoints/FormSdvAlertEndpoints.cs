using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// CRA source data verification modification alerts (<c>PRD-QRY-006</c>): lists observations whose previously verified
/// revision was superseded by a correction and whose latest revision has not yet been re-verified against source.
/// </summary>
internal static class FormSdvAlertEndpoints
{
    public static IEndpointRouteBuilder MapFormSdvAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(FormEndpoints.RoutePrefix)
            .RequireSignedInCaller("Answers only when the caller may see the requested study or enrollment and filters out blinded observations without unblinding authority.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution queries");

        group.MapGet("/sdv-alerts", ListSdvAlertsAsync)
            .WithName("ExecutionListFormSdvAlerts")
            .WithSummary("Lists active CRA SDV modification alerts for a study or enrollment, omitting observations once monitors verify the latest revision.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormSdvAlertResponse>>, ProblemHttpResult>> ListSdvAlertsAsync(
        [FromQuery(Name = "study_id")] Guid? studyId,
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IObservationRecords observations,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        bool hasStudy = studyId is { } sGuid && sGuid != Guid.Empty;
        bool hasEnrollment = enrollmentId is { } eGuid && eGuid != Guid.Empty;
        if ((studyId.HasValue && !hasStudy) || (enrollmentId.HasValue && !hasEnrollment) || (!hasStudy && !hasEnrollment))
        {
            return TypedResults.Problem(
                title: "Invalid query",
                detail: "Specify study_id or enrollment_id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenant = KernelRequestTenant.Get(context);
        Dictionary<EnrollmentId, EnrollmentContext>? studyEnrollmentHeaders = null;
        StudyId? targetStudy = hasStudy ? StudyId.FromGuid(studyId!.Value) : null;
        EnrollmentId? targetEnrollment = hasEnrollment ? EnrollmentId.FromGuid(enrollmentId!.Value) : null;

        if (targetStudy is { } study)
        {
            var studyEnrollments = await navigator.GetStudyEnrollmentsAsync(tenant, study, cancellationToken).ConfigureAwait(false);
            if (studyEnrollments is null)
            {
                return TypedResults.Problem(
                    title: "Not found",
                    detail: $"Study {study.Value:D} does not exist.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var studyNode = NodeRef.For(study);
            List<NodeRef> nodesToCheck = [studyNode, .. studyEnrollments.Select(e => NodeRef.For(e.EnrollmentId))];
            var visibleNodes = await access.FilterVisibleAsync(tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
            if (!visibleNodes.Contains(studyNode))
            {
                return NotVisible($"study {study.Value:D}");
            }

            studyEnrollmentHeaders = new Dictionary<EnrollmentId, EnrollmentContext>();
            foreach (var member in studyEnrollments)
            {
                if (!visibleNodes.Contains(NodeRef.For(member.EnrollmentId)))
                {
                    continue;
                }

                if (targetEnrollment is { } onlyEnrollment && member.EnrollmentId != onlyEnrollment)
                {
                    continue;
                }

                studyEnrollmentHeaders[member.EnrollmentId] = new EnrollmentContext(
                    member.StudySiteId?.Value,
                    member.SiteCode,
                    member.SubjectCode);
            }
        }

        if (targetEnrollment is { } enrollment)
        {
            if (!await access.CanSeeAsync(tenant, NodeRef.For(enrollment), cancellationToken).ConfigureAwait(false))
            {
                return NotVisible($"enrollment {enrollment.Value:D}");
            }

            if (studyEnrollmentHeaders is not null && !studyEnrollmentHeaders.ContainsKey(enrollment))
            {
                return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([]);
            }
        }

        List<FormSdvAlert> rawAlerts;
        await using (var db = contexts.Create(tenant))
        {
            IQueryable<FormSdvAlert> query = db.FormSdvAlerts.AsNoTracking();
            if (targetStudy is { } s)
            {
                query = query.Where(a => a.StudyId == s);
            }

            if (targetEnrollment is { } e)
            {
                query = query.Where(a => a.EnrollmentId == e);
            }
            else if (studyEnrollmentHeaders is not null)
            {
                var allowedEnrollments = studyEnrollmentHeaders.Keys.ToList();
                if (allowedEnrollments.Count == 0)
                {
                    return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([]);
                }

                query = query.Where(a => allowedEnrollments.Contains(a.EnrollmentId));
            }

            rawAlerts = await query
                .OrderByDescending(a => a.ModifiedAt)
                .ThenByDescending(a => a.NewRevision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (rawAlerts.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([]);
        }

        // Collapse multiple historical modifications of the same observation to the latest recorded alert episode.
        var latestByObservation = rawAlerts
            .GroupBy(a => a.ObservationId)
            .Select(g => g.OrderByDescending(a => a.NewRevision).ThenByDescending(a => a.ModifiedAt).First())
            .ToList();

        // Filter out observations masked by scope or blinding (K-AUTH-4).
        var observationNodes = latestByObservation.Select(a => NodeRef.For(a.ObservationId)).ToList();
        var visibleObservations = await access.FilterVisibleAsync(tenant, observationNodes, cancellationToken).ConfigureAwait(false);

        var activeCandidates = new List<(FormSdvAlert Alert, ObservationRecord Observation, int CurrentRevision)>();
        foreach (var alert in latestByObservation)
        {
            if (!visibleObservations.Contains(NodeRef.For(alert.ObservationId)))
            {
                continue;
            }

            var obs = await observations.FindForReaderAsync(
                tenant,
                alert.ObservationId,
                (eId, ct) => access.CanUnblindAsync(tenant, eId, ct),
                cancellationToken).ConfigureAwait(false);
            if (obs is null || (obs.IsBlindedTreatment && obs.ObservationId.IsEmpty))
            {
                continue;
            }

            int currentRevision = obs.Revisions.Count > 0 ? obs.Revisions[^1].Number : (int)obs.Version;
            bool latestVerified = obs.Assertions.Any(a =>
                a.Kind == AssertionKind.Verified
                && a.Revision == currentRevision
                && a.RetractedBy is null);
            if (latestVerified)
            {
                continue;
            }

            activeCandidates.Add((alert, obs, currentRevision));
        }

        if (activeCandidates.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([]);
        }

        var formIds = activeCandidates
            .Select(c => c.Alert.FormId ?? c.Observation.CollectionInstanceId)
            .Where(id => id is { IsEmpty: false })
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var formsById = formIds.Count > 0
            ? await forms.FindHeadersAsync(tenant, formIds, cancellationToken).ConfigureAwait(false)
            : new Dictionary<CollectionInstanceId, CollectionInstanceHeader>();

        var nodesToResolve = new List<NodeRef>();
        foreach (var (alert, obs, _) in activeCandidates)
        {
            nodesToResolve.Add(NodeRef.For(alert.StudyId));
            if (studyEnrollmentHeaders is null || !studyEnrollmentHeaders.ContainsKey(alert.EnrollmentId))
            {
                nodesToResolve.Add(NodeRef.For(alert.EnrollmentId));
                var siteId = alert.StudySiteId
                    ?? (obs.CollectionInstanceId is { } fId && formsById.TryGetValue(fId, out var rec) && !rec.StudySiteId.IsEmpty
                        ? rec.StudySiteId
                        : (StudySiteId?)null);
                if (siteId is { IsEmpty: false } sId)
                {
                    nodesToResolve.Add(NodeRef.For(sId));
                }
            }
        }

        var summaries = nodesToResolve.Count > 0
            ? await navigator.ResolveManyAsync(tenant, nodesToResolve.Distinct().ToList(), cancellationToken).ConfigureAwait(false)
            : new Dictionary<NodeRef, NodeSummary>();

        var responses = activeCandidates
            .Select(candidate =>
            {
                var (alert, obs, currentRevision) = candidate;
                var resolvedFormId = alert.FormId ?? obs.CollectionInstanceId;
                var formHeader = resolvedFormId is { IsEmpty: false } fId ? formsById.GetValueOrDefault(fId) : null;
                var itemId = alert.ItemId ?? obs.ItemId;

                var studySummary = summaries.GetValueOrDefault(NodeRef.For(alert.StudyId));
                var studyCode = studySummary?.Code ?? studySummary?.Title;

                Guid? studySiteGuid = alert.StudySiteId?.Value
                    ?? (formHeader is { StudySiteId.IsEmpty: false } ? formHeader.StudySiteId.Value : null);
                string? siteCode = null;
                string? subjectCode = null;

                if (studyEnrollmentHeaders is not null && studyEnrollmentHeaders.TryGetValue(alert.EnrollmentId, out var header))
                {
                    studySiteGuid ??= header.StudySiteId;
                    siteCode = header.SiteCode;
                    subjectCode = header.SubjectCode;
                }
                else
                {
                    if (studySiteGuid is { } siteGuid)
                    {
                        var siteSummary = summaries.GetValueOrDefault(new NodeRef(NodeKinds.StudySite, siteGuid));
                        siteCode = siteSummary?.Code ?? siteSummary?.Title;
                    }

                    var enrollmentSummary = summaries.GetValueOrDefault(NodeRef.For(alert.EnrollmentId));
                    subjectCode = enrollmentSummary?.Code ?? enrollmentSummary?.Title;
                }

                var latestRev = obs.Revisions.Count > 0 ? obs.Revisions[^1] : null;
                var modifiedBy = currentRevision > alert.NewRevision && !string.IsNullOrWhiteSpace(latestRev?.RecordedBy)
                    ? latestRev.RecordedBy
                    : alert.ModifiedBy;
                var modifiedAt = currentRevision > alert.NewRevision && latestRev is not null
                    ? latestRev.RecordedAt
                    : alert.ModifiedAt;
                var reason = currentRevision > alert.NewRevision && !string.IsNullOrWhiteSpace(latestRev?.ReasonForChange)
                    ? latestRev.ReasonForChange
                    : alert.ModifiedReason;

                var message = FormatAlertMessage(
                    subjectCode,
                    alert.EnrollmentId,
                    formHeader?.PlannedActivityId,
                    formHeader?.CollectionSpecId,
                    itemId);

                return new FormSdvAlertResponse(
                    alert.Id,
                    alert.StudyId.Value,
                    studySiteGuid,
                    alert.EnrollmentId.Value,
                    resolvedFormId?.Value,
                    alert.ObservationId.Value,
                    studyCode,
                    siteCode,
                    subjectCode,
                    formHeader?.CollectionSpecId,
                    formHeader?.PlannedActivityId,
                    itemId,
                    alert.SupersededRevision,
                    currentRevision,
                    alert.VerifiedBy,
                    alert.VerifiedAt,
                    modifiedBy,
                    modifiedAt,
                    reason,
                    message);
            })
            .OrderByDescending(r => r.ModifiedAt)
            .ThenBy(r => r.AlertId)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>(responses);
    }

    internal static string FormatAlertMessage(
        string? subjectCode,
        EnrollmentId enrollmentId,
        string? plannedActivityId,
        string? collectionSpecId,
        string? itemId)
    {
        var subjectLabel = !string.IsNullOrWhiteSpace(subjectCode)
            ? $"Subject {subjectCode}"
            : $"Subject {enrollmentId.Value.ToString("D")[..8]}";
        string visitSuffix = string.Empty;
        if (!string.IsNullOrWhiteSpace(plannedActivityId))
        {
            visitSuffix = $" - Visit {plannedActivityId}";
        }
        else if (!string.IsNullOrWhiteSpace(collectionSpecId))
        {
            visitSuffix = $" - Form {collectionSpecId}";
        }

        var itemSuffix = !string.IsNullOrWhiteSpace(itemId) ? $" ({itemId})" : string.Empty;
        return $"Previously verified field modified on {subjectLabel}{visitSuffix}{itemSuffix}";
    }

    private static ProblemHttpResult NotVisible(string what) =>
        TypedResults.Problem(
            title: "Not visible",
            detail: $"You may not see {what}.",
            statusCode: StatusCodes.Status403Forbidden);

    private sealed record EnrollmentContext(Guid? StudySiteId, string? SiteCode, string? SubjectCode);
}

/// <summary>
/// HTTP response DTO for an active CRA source data verification modification alert (<c>PRD-QRY-006</c>, <c>K-XS-1</c>).
/// </summary>
public sealed record FormSdvAlertResponse(
    Guid AlertId,
    Guid StudyId,
    Guid? StudySiteId,
    Guid EnrollmentId,
    Guid? FormId,
    Guid ObservationId,
    string? StudyCode,
    string? SiteCode,
    string? SubjectCode,
    string? CollectionSpecId,
    string? PlannedActivityId,
    string? ItemId,
    int SupersededRevision,
    int CurrentRevision,
    string VerifiedBy,
    DateTimeOffset VerifiedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    string ReasonForChange,
    string Message);
