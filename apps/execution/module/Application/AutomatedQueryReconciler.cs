using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Application;

internal static class AutomatedQueryReconciler
{
    public static async Task ReconcileAutomatedQueriesAsync(
        TenantId tenant,
        IReadOnlyList<ValidationFindingView> findings,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelCommandSender sender,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var candidateFindings = findings
            .Where(f => f.Stage == ValidationStage.Collection && f.Target.Kind == FindingTargetKind.DatasetRecord)
            .ToList();
        if (candidateFindings.Count == 0)
        {
            return;
        }

        var formIds = new HashSet<CollectionInstanceId>();
        foreach (var finding in candidateFindings)
        {
            if (Guid.TryParse(finding.Target.Key, out var parsed) && parsed != Guid.Empty)
            {
                formIds.Add(CollectionInstanceId.FromGuid(parsed));
            }
        }

        if (formIds.Count == 0)
        {
            return;
        }

        Dictionary<CollectionInstanceId, List<FormQuery>> queriesByForm;
        await using (var db = contexts.Create(tenant))
        {
            var existingQueries = await db.FormQueries.AsNoTracking()
                .Where(q => formIds.Contains(q.FormId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            queriesByForm = existingQueries
                .GroupBy(q => q.FormId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        var formRecordCache = new Dictionary<CollectionInstanceId, CollectionInstanceRecord?>();
        var recordFormIds = new HashSet<CollectionInstanceId>();
        foreach (var finding in candidateFindings)
        {
            if (Guid.TryParse(finding.Target.Key, out var candidateFormGuid)
                && candidateFormGuid != Guid.Empty
                && NeedsFormRecord(tenant, finding, CollectionInstanceId.FromGuid(candidateFormGuid), queriesByForm))
            {
                recordFormIds.Add(CollectionInstanceId.FromGuid(candidateFormGuid));
            }
        }

        if (recordFormIds.Count > 1)
        {
            var batchedRecords = await forms.FindManyAsync(tenant, recordFormIds, cancellationToken).ConfigureAwait(false);
            foreach (var candidateFormId in recordFormIds)
            {
                formRecordCache[candidateFormId] = batchedRecords.GetValueOrDefault(candidateFormId);
            }
        }

        var ruleInputsByEnrollment = new Dictionary<EnrollmentId, IReadOnlyList<CollectionRuleInput>>();
        foreach (var finding in candidateFindings)
        {
            if (!Guid.TryParse(finding.Target.Key, out var formGuid) || formGuid == Guid.Empty)
            {
                continue;
            }

            var formId = CollectionInstanceId.FromGuid(formGuid);
            queriesByForm.TryGetValue(formId, out var formQueries);
            formQueries ??= [];

            if (finding.Status is FindingStatus.Open or FindingStatus.Investigating)
            {
                await EnsureOpenAutoQueryAsync(tenant, finding, formId, formQueries, formRecordCache, forms, sender, metadata, cancellationToken).ConfigureAwait(false);
            }
            else if (finding.Status == FindingStatus.Resolved)
            {
                await CloseResolvedAutoQueriesAsync(tenant, finding, formQueries, sender, metadata, cancellationToken).ConfigureAwait(false);
            }
            else if (finding.Status is FindingStatus.Accepted or FindingStatus.Explained)
            {
                if (!formRecordCache.TryGetValue(formId, out var record))
                {
                    record = await forms.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
                    formRecordCache[formId] = record;
                }

                if (record is not null)
                {
                    if (finding.Decision?.DecidedOnRun == finding.LastDetectedRun)
                    {
                        if (!ruleInputsByEnrollment.TryGetValue(record.EnrollmentId, out var enrollmentForms))
                        {
                            enrollmentForms = await forms.ListRuleInputsForEnrollmentAsync(tenant, record.EnrollmentId, cancellationToken).ConfigureAwait(false);
                            ruleInputsByEnrollment[record.EnrollmentId] = enrollmentForms;
                        }

                        if (ObservedValueComparator.AreObservedValuesUnchanged(finding, CollectionRuleInput.From(record), enrollmentForms))
                        {
                            await CloseConcludedAutoQueriesOnUnchangedValuesAsync(
                                tenant,
                                finding,
                                record.Values.ToDictionary(v => v.ObservationId, v => v.Revision),
                                formQueries,
                                finding.Decision?.DecidedOnRun ?? finding.LastDetectedRun,
                                finding.Status,
                                sender,
                                metadata,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var firstEpisodeId = DeriveAutoQueryId(tenant, finding.Id, 0);
                        if (formQueries.Any(q => q.Id == firstEpisodeId))
                        {
                            await EnsureOpenAutoQueryAsync(
                                tenant,
                                finding,
                                formId,
                                formQueries,
                                formRecordCache,
                                forms,
                                sender,
                                metadata,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
    }

    public static async Task CloseConcludedAutoQueriesOnUnchangedValuesAsync(
        TenantId tenant,
        ValidationFindingView finding,
        IReadOnlyDictionary<ObservationId, int> currentRevisions,
        List<FormQuery> formQueries,
        ValidationRunId decidedOnRun,
        FindingStatus decisionStatus,
        IKernelCommandSender sender,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (formQueries.Count == 0)
        {
            return;
        }

        var autoIds = new HashSet<FormQueryId>();
        for (var episode = 0; episode <= formQueries.Count; episode++)
        {
            autoIds.Add(DeriveAutoQueryId(tenant, finding.Id, episode));
        }

        var activeAutoQueries = formQueries
            .Where(q => autoIds.Contains(q.Id)
                && q.Origin == FormQueryOrigin.System
                && q.Status is FormQueryStatus.Open or FormQueryStatus.Answered)
            .Where(q => finding.Observed.Count > 0
                ? currentRevisions.ContainsKey(q.ObservationId) && (finding.Decision?.DecidedOnRun == finding.LastDetectedRun || currentRevisions[q.ObservationId] == q.Revision)
                : currentRevisions.TryGetValue(q.ObservationId, out var rev) && rev == q.Revision)
            .ToList();

        var verb = decisionStatus == FindingStatus.Accepted ? "accepted" : "explained";
        foreach (var query in activeAutoQueries)
        {
            var closeReason = TrimText(
                $"Automated closure: validation finding {finding.PackKey}/{finding.RuleId} was {verb} on unchanged values.",
                AssertionText.MaxLength);
            var closeResult = await sender.SendAsync(
                new CloseFormQuery(
                    tenant,
                    query.Id,
                    query.EnrollmentId,
                    $"auto-query:close:{query.Id.Value:D}:{verb}:{decidedOnRun.Value:D}",
                    closeReason),
                metadata,
                cancellationToken).ConfigureAwait(false);

            if (closeResult.Error is { } error
                && error.Code != KernelErrors.AlreadyExists.Code
                && error.Code != FormQueryErrors.InvalidTransition.Code
                && error.Code != AssertionErrors.Locked.Code)
            {
                throw new InvalidOperationException(
                    $"Closing automated query {query.Id.Value:D} after finding {verb} failed: {error.Code} ({error.Message}).");
            }
        }
    }

    public static FormQueryId DeriveAutoQueryId(TenantId tenant, ValidationFindingId findingId, int episode)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-auto-discrepancy-query:{tenant}:{findingId}:{episode}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return FormQueryId.FromGuid(new Guid(bytes, bigEndian: true));
    }

    private static async Task EnsureOpenAutoQueryAsync(
        TenantId tenant,
        ValidationFindingView finding,
        CollectionInstanceId formId,
        List<FormQuery> formQueries,
        Dictionary<CollectionInstanceId, CollectionInstanceRecord?> formRecordCache,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!formRecordCache.TryGetValue(formId, out var record))
        {
            record = await forms.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
            formRecordCache[formId] = record;
        }

        if (record is null)
        {
            return;
        }

        var itemId = !string.IsNullOrWhiteSpace(finding.Target.Variable)
            ? finding.Target.Variable
            : record.Items.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(itemId)
            || !record.Items.Any(i => string.Equals(i.Id, itemId, StringComparison.Ordinal)))
        {
            return;
        }

        if ((finding.BlindedVariables is { Count: > 0 })
            || (record.BlindedItemIds?.Contains(itemId) ?? false))
        {
            return;
        }

        var enrollmentId = record.EnrollmentId;
        ObservationId targetObservation;
        int targetRevision;
        bool isLocked;
        if (record.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)) is { } existingValue)
        {
            targetObservation = existingValue.ObservationId;
            targetRevision = existingValue.Revision;
            isLocked = FormQueryScopes.IsLocked(record, targetObservation);
        }
        else
        {
            var derivedObsId = ObservationId.FromGuid(DeriveUnaskedObservationGuid(tenant, formId, itemId));
            var unasked = await sender.SendAsync(
                new RecordUnaskedCollectionItem(
                    tenant,
                    formId,
                    enrollmentId,
                    itemId,
                    derivedObsId,
                    $"auto-query:unasked:{formId.Value:D}:{itemId}",
                    $"Record unasked item for edit-check rule {finding.PackKey}/{finding.RuleId}"),
                metadata,
                cancellationToken).ConfigureAwait(false);
            if (!unasked.IsSuccess)
            {
                if (unasked.Error?.Code == AssertionErrors.Locked.Code)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Recording unasked item {itemId} on form {formId} failed: {unasked.Error?.Code}.");
            }

            targetObservation = unasked.Value!.ObservationId;
            targetRevision = unasked.Value.Revision;
            formRecordCache.Remove(formId);
            var seededTarget = await forms.FindQueryTargetAsync(
                tenant,
                formId,
                observation: targetObservation,
                checkLocks: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (seededTarget is null)
            {
                return;
            }

            isLocked = seededTarget.IsLocked;
        }

        var episode = 0;
        while (true)
        {
            var candidateId = DeriveAutoQueryId(tenant, finding.Id, episode);
            var existing = formQueries.FirstOrDefault(q => q.Id == candidateId);
            if (existing is null)
            {
                break;
            }

            if (existing.Status is FormQueryStatus.Open or FormQueryStatus.Answered)
            {
                return;
            }

            if (existing.Status is FormQueryStatus.Closed or FormQueryStatus.Cancelled
                && existing.ObservationId == targetObservation
                && existing.Revision >= targetRevision)
            {
                return;
            }

            episode++;
        }

        if (isLocked)
        {
            return;
        }

        var queryId = DeriveAutoQueryId(tenant, finding.Id, episode);
        var queryText = BuildQueryText(finding);
        var reason = $"Automated edit-check discrepancy ({finding.PackKey}/{finding.RuleId})";
        var priority = finding.Severity == ValidationSeverity.Error
            ? FormQueryPriority.High
            : FormQueryPriority.Normal;
        var raiseResult = await sender.SendAsync(
            new RaiseFormQuery(
                tenant,
                queryId,
                formId,
                enrollmentId,
                targetObservation,
                targetRevision,
                queryText,
                $"auto-query:raise:{queryId.Value:D}",
                reason,
                Origin: FormQueryOrigin.System,
                OwnerRole: FormQueryOwnerRole.Site,
                Priority: priority),
            metadata,
            cancellationToken).ConfigureAwait(false);

        if (raiseResult.Error?.Code == FormQueryErrors.RevisionStale.Code)
        {
            formRecordCache.Remove(formId);
            var refreshed = await forms.FindQueryTargetAsync(
                tenant,
                formId,
                observation: targetObservation,
                checkLocks: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (refreshed is { Revision: { } latestRevision })
            {
                raiseResult = await sender.SendAsync(
                    new RaiseFormQuery(
                        tenant,
                        queryId,
                        formId,
                        refreshed.EnrollmentId,
                        targetObservation,
                        latestRevision,
                        queryText,
                        $"auto-query:raise:{queryId.Value:D}:r{latestRevision}",
                        reason,
                        Origin: FormQueryOrigin.System,
                        OwnerRole: FormQueryOwnerRole.Site,
                        Priority: priority),
                    metadata,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (raiseResult.Error is { } error
            && error.Code != KernelErrors.AlreadyExists.Code
            && error.Code != AssertionErrors.Locked.Code)
        {
            throw new InvalidOperationException(
                $"Raising automated query {queryId.Value:D} on form {formId} failed: {error.Code} ({error.Message}).");
        }
    }

    private static async Task CloseResolvedAutoQueriesAsync(
        TenantId tenant,
        ValidationFindingView finding,
        List<FormQuery> formQueries,
        IKernelCommandSender sender,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (formQueries.Count == 0)
        {
            return;
        }

        var autoIds = new HashSet<FormQueryId>();
        for (var episode = 0; episode <= formQueries.Count; episode++)
        {
            autoIds.Add(DeriveAutoQueryId(tenant, finding.Id, episode));
        }

        var activeAutoQueries = formQueries
            .Where(q => autoIds.Contains(q.Id)
                && q.Origin == FormQueryOrigin.System
                && q.Status is FormQueryStatus.Open or FormQueryStatus.Answered)
            .ToList();

        foreach (var query in activeAutoQueries)
        {
            var resolutionRun = finding.ResolvedBy?.Value.ToString("D") ?? "resolved";
            var closeReason = TrimText(
                $"Automated resolution: edit check {finding.PackKey}/{finding.RuleId} passed after data update.",
                AssertionText.MaxLength);
            var closeResult = await sender.SendAsync(
                new CloseFormQuery(
                    tenant,
                    query.Id,
                    query.EnrollmentId,
                    $"auto-query:close:{query.Id.Value:D}:{resolutionRun}",
                    closeReason),
                metadata,
                cancellationToken).ConfigureAwait(false);

            if (closeResult.Error is { } error
                && error.Code != KernelErrors.AlreadyExists.Code
                && error.Code != FormQueryErrors.InvalidTransition.Code
                && error.Code != AssertionErrors.Locked.Code)
            {
                throw new InvalidOperationException(
                    $"Closing resolved automated query {query.Id.Value:D} failed: {error.Code} ({error.Message}).");
            }
        }
    }

    private static bool NeedsFormRecord(
        TenantId tenant,
        ValidationFindingView finding,
        CollectionInstanceId formId,
        Dictionary<CollectionInstanceId, List<FormQuery>> queriesByForm) =>
        finding.Status is FindingStatus.Open or FindingStatus.Investigating
        || (finding.Status is FindingStatus.Accepted or FindingStatus.Explained
            && (finding.Decision?.DecidedOnRun == finding.LastDetectedRun
                || (queriesByForm.TryGetValue(formId, out var existingForForm)
                    && existingForForm.Any(q => q.Id == DeriveAutoQueryId(tenant, finding.Id, 0)))));

    private static Guid DeriveUnaskedObservationGuid(TenantId tenant, CollectionInstanceId formId, string itemId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-auto-discrepancy-unasked:{tenant}:{formId}:{itemId}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static string BuildQueryText(ValidationFindingView finding)
    {
        var prefix = $"[{finding.PackKey}/{finding.RuleId}] ";
        return TrimText(prefix + finding.Message, AssertionText.MaxLength);
    }

    private static string TrimText(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
}
