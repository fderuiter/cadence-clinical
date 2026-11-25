using System;
using System.Collections.Generic;
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

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Pre-resolved site and participant summary headers for a visible study enrollment when querying active SDV alerts.
/// </summary>
internal sealed record SdvAlertEnrollmentContext(
    StudySiteId? StudySiteId,
    string? SiteCode,
    string? SubjectCode);

/// <summary>
/// Batched read-side query service for active CRA source data verification modification alerts (<c>PRD-QRY-006</c>),
/// shared by <c>GET /api/v1/execution/forms/sdv-alerts</c> and the kernel study-attention <see cref="IStudySdvAlertReader"/> port.
/// </summary>
internal interface IFormSdvAlertQueryService
{
    Task<IReadOnlyList<StudySdvAlertView>> QueryActiveAlertsAsync(
        TenantId tenant,
        StudyId? targetStudy,
        EnrollmentId? targetEnrollment,
        string? knownStudyCode,
        IReadOnlyDictionary<EnrollmentId, SdvAlertEnrollmentContext>? studyEnrollmentHeaders,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Collapses multi-episode <see cref="FormSdvAlert"/> rows per observation in SQL, pre-filters re-verified revisions and
/// evaluates blinding via <see cref="IObservationRecords.FindUnverifiedSdvTargetsAsync"/>, and enriches active alerts with
/// form and navigation metadata in bounded batches (<c>PRD-QRY-006</c>, <c>K-XS-1</c>, <c>K-XS-2</c>, <c>K-MOD-1</c>).
/// </summary>
internal sealed class FormSdvAlertQueryService(
    IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
    IObservationRecords observations,
    ICollectionInstanceRecords forms,
    IKernelNavigator navigator,
    IKernelReadAccess? access = null) : IFormSdvAlertQueryService, IStudySdvAlertReader
{
    public async Task<IReadOnlyList<StudySdvAlertView>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        string? knownStudyCode = null,
        CancellationToken cancellationToken = default)
    {
        if (access is null)
        {
            return [];
        }

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

        var studyEnrollmentHeaders = new Dictionary<EnrollmentId, SdvAlertEnrollmentContext>();
        foreach (var member in studyEnrollments)
        {
            if (!visibleNodes.Contains(NodeRef.For(member.EnrollmentId)))
            {
                continue;
            }

            studyEnrollmentHeaders[member.EnrollmentId] = new SdvAlertEnrollmentContext(
                member.StudySiteId,
                member.SiteCode,
                member.SubjectCode);
        }

        if (studyEnrollmentHeaders.Count == 0)
        {
            return [];
        }

        return await QueryActiveAlertsAsync(
            tenant,
            study,
            targetEnrollment: null,
            knownStudyCode,
            studyEnrollmentHeaders,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StudySdvAlertView>> QueryActiveAlertsAsync(
        TenantId tenant,
        StudyId? targetStudy,
        EnrollmentId? targetEnrollment,
        string? knownStudyCode,
        IReadOnlyDictionary<EnrollmentId, SdvAlertEnrollmentContext>? studyEnrollmentHeaders,
        CancellationToken cancellationToken = default)
    {
        if (access is null)
        {
            return [];
        }

        if (targetEnrollment is null && studyEnrollmentHeaders is { Count: 0 })
        {
            return [];
        }

        var latestByObservation = await LoadLatestAlertsByObservationAsync(
            tenant,
            targetStudy,
            targetEnrollment,
            studyEnrollmentHeaders?.Keys,
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

        return activeCandidates
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

                StudySiteId? resolvedStudySiteId = null;
                if (alert.StudySiteId is { IsEmpty: false } alertSite)
                {
                    resolvedStudySiteId = alertSite;
                }
                else if (formHeader is { StudySiteId.IsEmpty: false })
                {
                    resolvedStudySiteId = formHeader.StudySiteId;
                }
                string? siteCode = null;
                string? subjectCode = null;

                if (studyEnrollmentHeaders is not null && studyEnrollmentHeaders.TryGetValue(alert.EnrollmentId, out var header))
                {
                    resolvedStudySiteId ??= header.StudySiteId is { IsEmpty: false } headerSite ? headerSite : null;
                    siteCode = header.SiteCode;
                    subjectCode = header.SubjectCode;
                }
                else
                {
                    if (resolvedStudySiteId is { IsEmpty: false } siteId)
                    {
                        var siteSummary = summaries.GetValueOrDefault(NodeRef.For(siteId));
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
                    resolvedStudySiteId,
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

    private async Task<List<FormSdvAlert>> LoadLatestAlertsByObservationAsync(
        TenantId tenant,
        StudyId? targetStudy,
        EnrollmentId? targetEnrollment,
        IEnumerable<EnrollmentId>? allowedEnrollments,
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
                            ORDER BY observation_id, new_revision DESC, modified_at DESC, id DESC
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
                            ORDER BY observation_id, new_revision DESC, modified_at DESC, id DESC
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
                            ORDER BY observation_id, new_revision DESC, modified_at DESC, id DESC
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
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rawAlerts
            .GroupBy(a => a.ObservationId)
            .Select(g => g
                .OrderByDescending(a => a.NewRevision)
                .ThenByDescending(a => a.ModifiedAt)
                .ThenByDescending(a => a.Id)
                .First())
            .ToList();
    }
}
