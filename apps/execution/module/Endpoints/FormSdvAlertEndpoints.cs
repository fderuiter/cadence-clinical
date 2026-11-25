using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
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
    private const int MaxStudyCodeLength = 128;

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
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IObservationRecords observations,
        ICollectionInstanceRecords forms,
        IStudyDirectory studies,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        bool hasStudyId = studyId is { } sGuid && sGuid != Guid.Empty;
        bool hasEnrollment = enrollmentId is { } eGuid && eGuid != Guid.Empty;
        string? normalizedStudyCode = studyCode?.Trim();
        bool hasStudyCode = normalizedStudyCode is not null;

        if ((studyId.HasValue && !hasStudyId)
            || (enrollmentId.HasValue && !hasEnrollment)
            || (hasStudyCode && (normalizedStudyCode!.Length == 0
                || normalizedStudyCode.Length > MaxStudyCodeLength
                || normalizedStudyCode.Any(char.IsControl))))
        {
            return TypedResults.Problem(
                title: "Invalid query",
                detail: "Specify study_id, study_code, or enrollment_id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!hasStudyId && !hasStudyCode && !hasEnrollment)
        {
            return TypedResults.Problem(
                title: "Invalid query",
                detail: "Specify study_id, study_code, or enrollment_id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenant = KernelRequestTenant.Get(context);
        StudyId? targetStudy = hasStudyId ? StudyId.FromGuid(studyId!.Value) : null;
        string? knownStudyCode = null;

        if (hasStudyCode)
        {
            var resolvedByCode = await studies.FindStudyByCodeAsync(tenant, normalizedStudyCode!, cancellationToken).ConfigureAwait(false);
            if (resolvedByCode is null)
            {
                return TypedResults.Problem(
                    title: "Not found",
                    detail: $"Study '{normalizedStudyCode}' does not exist.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (targetStudy is { } explicitStudy && explicitStudy != resolvedByCode.StudyId)
            {
                return TypedResults.Problem(
                    title: "Invalid query",
                    detail: "study_id and study_code refer to different studies.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            targetStudy = resolvedByCode.StudyId;
            knownStudyCode = resolvedByCode.StudyCode;
        }

        Dictionary<EnrollmentId, EnrollmentContext>? studyEnrollmentHeaders = null;
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
            var enrollmentNode = NodeRef.For(enrollment);
            var visibleEnrollmentNodes = await access.FilterVisibleAsync(tenant, [enrollmentNode], cancellationToken).ConfigureAwait(false);
            if (!visibleEnrollmentNodes.Contains(enrollmentNode))
            {
                return NotVisible($"enrollment {enrollment.Value:D}");
            }

            if (studyEnrollmentHeaders is not null && !studyEnrollmentHeaders.ContainsKey(enrollment))
            {
                return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([]);
            }
        }

        var views = await QueryActiveAlertsCoreAsync(
            tenant,
            targetStudy,
            targetEnrollment,
            knownStudyCode,
            studyEnrollmentHeaders,
            contexts,
            observations,
            forms,
            access,
            navigator,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([.. views.Select(FormSdvAlertResponse.From)]);
    }

    internal static async Task<IReadOnlyList<StudySdvAlertView>> ReadVisibleStudyAlertsAsync(
        TenantId tenant,
        StudyId study,
        string? knownStudyCode,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IObservationRecords observations,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var studyEnrollments = await navigator.GetStudyEnrollmentsAsync(tenant, study, cancellationToken).ConfigureAwait(false);
        if (studyEnrollments is null || studyEnrollments.Count == 0)
        {
            return [];
        }

        var studyNode = NodeRef.For(study);
        List<NodeRef> nodesToCheck = [studyNode, .. studyEnrollments.Select(e => NodeRef.For(e.EnrollmentId))];
        var visibleNodes = await access.FilterVisibleAsync(tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
        if (!visibleNodes.Contains(studyNode))
        {
            return [];
        }

        var studyEnrollmentHeaders = new Dictionary<EnrollmentId, EnrollmentContext>();
        foreach (var member in studyEnrollments)
        {
            if (!visibleNodes.Contains(NodeRef.For(member.EnrollmentId)))
            {
                continue;
            }

            studyEnrollmentHeaders[member.EnrollmentId] = new EnrollmentContext(
                member.StudySiteId?.Value,
                member.SiteCode,
                member.SubjectCode);
        }

        if (studyEnrollmentHeaders.Count == 0)
        {
            return [];
        }

        return await QueryActiveAlertsCoreAsync(
            tenant,
            study,
            targetEnrollment: null,
            knownStudyCode,
            studyEnrollmentHeaders,
            contexts,
            observations,
            forms,
            access,
            navigator,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StudySdvAlertView>> QueryActiveAlertsCoreAsync(
        TenantId tenant,
        StudyId? targetStudy,
        EnrollmentId? targetEnrollment,
        string? knownStudyCode,
        Dictionary<EnrollmentId, EnrollmentContext>? studyEnrollmentHeaders,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IObservationRecords observations,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        if (targetEnrollment is null && studyEnrollmentHeaders is { Count: 0 })
        {
            return [];
        }

        var latestByObservation = await LoadLatestAlertsByObservationAsync(
            tenant,
            targetStudy,
            targetEnrollment,
            studyEnrollmentHeaders?.Keys,
            contexts,
            cancellationToken).ConfigureAwait(false);
        if (latestByObservation.Count == 0)
        {
            return [];
        }

        var unblindCache = new Dictionary<EnrollmentId, bool>();
        async ValueTask<bool> CanUnblindOncePerEnrollmentAsync(EnrollmentId enrollmentId, CancellationToken ct)
        {
            if (unblindCache.TryGetValue(enrollmentId, out var cached))
            {
                return cached;
            }

            var permitted = await access.CanUnblindAsync(tenant, enrollmentId, ct).ConfigureAwait(false);
            unblindCache[enrollmentId] = permitted;
            return permitted;
        }

        var candidateObservationIds = latestByObservation.Select(a => a.ObservationId).ToList();
        var activeTargetsByObservation = await observations.FindUnverifiedSdvTargetsAsync(
            tenant,
            candidateObservationIds,
            CanUnblindOncePerEnrollmentAsync,
            cancellationToken).ConfigureAwait(false);
        if (activeTargetsByObservation.Count == 0)
        {
            return [];
        }

        var activeCandidates = new List<(FormSdvAlert Alert, ObservationSdvAlertTarget Observation)>(activeTargetsByObservation.Count);
        foreach (var alert in latestByObservation)
        {
            if (activeTargetsByObservation.TryGetValue(alert.ObservationId, out var target))
            {
                activeCandidates.Add((alert, target));
            }
        }

        if (activeCandidates.Count == 0)
        {
            return [];
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
        foreach (var (alert, obs) in activeCandidates)
        {
            if (string.IsNullOrWhiteSpace(knownStudyCode) || targetStudy != alert.StudyId)
            {
                nodesToResolve.Add(NodeRef.For(alert.StudyId));
            }

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

        var views = activeCandidates
            .Select(candidate =>
            {
                var (alert, obs) = candidate;
                var currentRevision = obs.CurrentRevision;
                var resolvedFormId = alert.FormId ?? obs.CollectionInstanceId;
                var formHeader = resolvedFormId is { IsEmpty: false } fId ? formsById.GetValueOrDefault(fId) : null;
                var itemId = alert.ItemId ?? obs.ItemId;

                string? studyCode;
                if (!string.IsNullOrWhiteSpace(knownStudyCode) && targetStudy == alert.StudyId)
                {
                    studyCode = knownStudyCode;
                }
                else
                {
                    var studySummary = summaries.GetValueOrDefault(NodeRef.For(alert.StudyId));
                    studyCode = studySummary?.Code ?? studySummary?.Title;
                }

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

                var modifiedBy = currentRevision > alert.NewRevision && !string.IsNullOrWhiteSpace(obs.LatestRecordedBy)
                    ? obs.LatestRecordedBy
                    : alert.ModifiedBy;
                var modifiedAt = currentRevision > alert.NewRevision
                    ? obs.LatestRecordedAt
                    : alert.ModifiedAt;
                var reason = currentRevision > alert.NewRevision && !string.IsNullOrWhiteSpace(obs.LatestReasonForChange)
                    ? obs.LatestReasonForChange
                    : alert.ModifiedReason;

                var message = FormatAlertMessage(
                    subjectCode,
                    alert.EnrollmentId,
                    formHeader?.PlannedActivityId,
                    formHeader?.CollectionSpecId,
                    itemId);

                return new StudySdvAlertView(
                    alert.Id,
                    alert.StudyId,
                    studySiteGuid is { } resolvedSiteGuid ? StudySiteId.FromGuid(resolvedSiteGuid) : null,
                    alert.EnrollmentId,
                    resolvedFormId is { IsEmpty: false } nonEmptyForm ? nonEmptyForm : null,
                    alert.ObservationId,
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

        return views;
    }

    private static async Task<List<FormSdvAlert>> LoadLatestAlertsByObservationAsync(
        TenantId tenant,
        StudyId? targetStudy,
        EnrollmentId? targetEnrollment,
        IReadOnlyCollection<EnrollmentId>? allowedEnrollments,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        if (db.Database.IsRelational())
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (targetStudy is { } study && targetEnrollment is { } enrollment)
                {
                    return await db.FormSdvAlerts
                        .FromSqlInterpolated($"""
                            SELECT DISTINCT ON (observation_id) *
                            FROM execution.form_sdv_alerts
                            WHERE tenant_id = {tenant.Value}
                              AND study_id = {study.Value}
                              AND enrollment_id = {enrollment.Value}
                            ORDER BY observation_id, new_revision DESC, modified_at DESC
                            """)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (targetStudy is { } studyOnly)
                {
                    var enrollmentGuids = allowedEnrollments is not null
                        ? allowedEnrollments.Select(e => e.Value).ToArray()
                        : [];
                    if (enrollmentGuids.Length == 0)
                    {
                        return [];
                    }

                    return await db.FormSdvAlerts
                        .FromSqlInterpolated($"""
                            SELECT DISTINCT ON (observation_id) *
                            FROM execution.form_sdv_alerts
                            WHERE tenant_id = {tenant.Value}
                              AND study_id = {studyOnly.Value}
                              AND enrollment_id = ANY({enrollmentGuids})
                            ORDER BY observation_id, new_revision DESC, modified_at DESC
                            """)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (targetEnrollment is { } enrollmentOnly)
                {
                    return await db.FormSdvAlerts
                        .FromSqlInterpolated($"""
                            SELECT DISTINCT ON (observation_id) *
                            FROM execution.form_sdv_alerts
                            WHERE tenant_id = {tenant.Value}
                              AND enrollment_id = {enrollmentOnly.Value}
                            ORDER BY observation_id, new_revision DESC, modified_at DESC
                            """)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                return [];
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        IQueryable<FormSdvAlert> fallbackQuery = db.FormSdvAlerts.AsNoTracking();
        if (targetStudy is { } s)
        {
            fallbackQuery = fallbackQuery.Where(a => a.StudyId == s);
        }

        if (targetEnrollment is { } e)
        {
            fallbackQuery = fallbackQuery.Where(a => a.EnrollmentId == e);
        }
        else if (allowedEnrollments is not null)
        {
            var allowedList = allowedEnrollments.ToList();
            if (allowedList.Count == 0)
            {
                return [];
            }

            fallbackQuery = fallbackQuery.Where(a => allowedList.Contains(a.EnrollmentId));
        }

        var rawAlerts = await fallbackQuery
            .OrderByDescending(a => a.ModifiedAt)
            .ThenByDescending(a => a.NewRevision)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rawAlerts
            .GroupBy(a => a.ObservationId)
            .Select(g => g.OrderByDescending(a => a.NewRevision).ThenByDescending(a => a.ModifiedAt).First())
            .ToList();
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
public sealed record FormSdvAlertResponse : StudySdvAlertPayload
{
    /// <summary>Initializes an empty response instance for JSON deserialization.</summary>
    public FormSdvAlertResponse()
    {
    }

    [SetsRequiredMembers]
    private FormSdvAlertResponse(StudySdvAlertView view)
        : base(view)
    {
    }

    internal static FormSdvAlertResponse From(StudySdvAlertView view) => new(view);
}
