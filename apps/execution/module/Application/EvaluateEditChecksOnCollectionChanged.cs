using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Post-commit collection edit-check and query auto-answer consumer (spec §10.2, K-RULE-3, K-FIND-1, <c>PRD-QRY-001</c>):
/// <list type="bullet">
/// <item>When a user edits a queried observation (<see cref="ObservationCorrected"/>), automatically transitions open manual
/// <see cref="FormQuery"/> aggregates on that observation to <see cref="FormQueryStatus.Answered"/>, assigns ownership to
/// <see cref="FormQueryOwnerRole.Monitor"/>, and mirrors <see cref="AnswerQuery"/> with the editing user and their verbatim
/// change reason.</item>
/// <item>Evaluates pinned study collection rule packs via <see cref="ICollectionValidationService"/>, raises automated
/// (<see cref="FormQueryOrigin.System"/>) queries on active violations, and auto-closes resolved system rule queries.</item>
/// </list>
/// </summary>
internal sealed class EvaluateEditChecksOnCollectionChanged(
    ICollectionValidationService validation,
    ICollectionInstanceRecords forms,
    IObservationRecords observations,
    IValidationFindings findingsReader,
    IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
    IKernelCommandSender sender) : IKernelEventConsumer
{
    /// <summary>The consumer's name, and so its service account <c>svc:execution-edit-checks</c>.</summary>
    public const string Name = "execution-edit-checks";

    private static readonly KernelEventAttribute SavedEvent = typeof(CollectedValuesSaved).GetCustomAttribute<KernelEventAttribute>()!;
    private static readonly KernelEventAttribute CorrectedEvent = typeof(ObservationCorrected).GetCustomAttribute<KernelEventAttribute>()!;
    // jsonb reorders object members, so a polymorphic ObservationValue's "type" member may follow "code" or "text".
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
    private static readonly string SelfActorId = Actor.ServiceAccount(Name).Id;

    public bool Handles(EventEnvelope envelope) =>
        envelope is not null
        && ((string.Equals(envelope.Type, SavedEvent.Type, StringComparison.Ordinal) && envelope.SchemaVersion == SavedEvent.SchemaVersion)
            || (string.Equals(envelope.Type, CorrectedEvent.Type, StringComparison.Ordinal) && envelope.SchemaVersion == CorrectedEvent.SchemaVersion));

    public async Task ConsumeAsync(KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        // Skip self-emitted events from RecordUnaskedCollectionItem so blank-item query seeding never loops.
        if (string.Equals(delivery.Envelope.Actor, SelfActorId, StringComparison.Ordinal)
            || string.Equals(delivery.Envelope.Actor, $"svc:{Name}", StringComparison.Ordinal))
        {
            return;
        }

        var metadata = new CommandMetadata(delivery.Envelope.CorrelationId, delivery.Envelope.EventId);
        TenantId tenant;
        StudyId studyId;
        if (string.Equals(delivery.Envelope.Type, SavedEvent.Type, StringComparison.Ordinal))
        {
            var saved = delivery.Envelope.Payload.Deserialize<CollectedValuesSaved>(EventJsonOptions)
                ?? throw new JsonException($"The payload of event {delivery.Envelope.EventId} is null.");
            tenant = saved.TenantId;
            studyId = saved.StudyId;
        }
        else
        {
            var corrected = delivery.Envelope.Payload.Deserialize<ObservationCorrected>(EventJsonOptions)
                ?? throw new JsonException($"The payload of event {delivery.Envelope.EventId} is null.");
            tenant = corrected.TenantId;

            // Automatically answer open manual queries on the edited observation before rule evaluation so studies
            // without pinned rule packs still auto-answer open manual queries.
            await AutoAnswerOpenManualQueriesAsync(tenant, corrected, delivery.Envelope.Actor, metadata, cancellationToken).ConfigureAwait(false);

            var obs = await observations.FindAsync(tenant, corrected.ObservationId, cancellationToken).ConfigureAwait(false);
            if (obs?.CollectionInstanceId is not { } formId)
            {
                return;
            }

            var formRecord = await forms.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
            if (formRecord is null)
            {
                return;
            }

            studyId = formRecord.StudyId;
        }

        var evaluatedAt = delivery.Envelope.Recorded;
        var asOf = DateOnly.FromDateTime(evaluatedAt.UtcDateTime);
        var runKey = $"{delivery.IdempotencyKey}:run";
        var runReason = $"Evaluate collection edit checks after {delivery.Envelope.Type}";

        var validationResult = await validation.ValidateStudyCollectionsAsync(
            tenant,
            studyId,
            asOf,
            evaluatedAt,
            runKey,
            runReason,
            metadata,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ValidationFindingView> findings;
        if (validationResult.IsSuccess)
        {
            if (validationResult.Value is not { } resultValue
                || (resultValue.RunOutcome is null && resultValue.Findings.Count == 0))
            {
                return;
            }

            findings = resultValue.Findings;
        }
        else if (validationResult.Error?.Code == KernelErrors.AlreadyExists.Code)
        {
            var page = await findingsReader.ListAsync(
                tenant,
                new FindingQuery(studyId) { Stage = ValidationStage.Collection, Take = FindingQuery.MaxTake },
                cancellationToken).ConfigureAwait(false);
            findings = page.Findings;
        }
        else
        {
            throw new InvalidOperationException(
                $"Collection validation run for study {studyId} failed on event {delivery.Envelope.EventId}: {validationResult.Error?.Code} ({validationResult.Error?.Message}).");
        }

        await ReconcileAutomatedQueriesAsync(tenant, findings, metadata, cancellationToken).ConfigureAwait(false);
    }

    private async Task AutoAnswerOpenManualQueriesAsync(
        TenantId tenant,
        ObservationCorrected corrected,
        string editingActor,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        List<FormQuery> openManualQueries;
        await using (var db = contexts.Create(tenant))
        {
            openManualQueries = await db.FormQueries.AsNoTracking()
                .Where(q => q.ObservationId == corrected.ObservationId
                    && q.Origin == FormQueryOrigin.Manual
                    && q.Status == FormQueryStatus.Open
                    && q.Revision < corrected.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var query in openManualQueries)
        {
            var result = await sender.SendAsync(
                new AutoAnswerFormQuery(
                    tenant,
                    query.Id,
                    query.EnrollmentId,
                    corrected.ObservationId,
                    corrected.Revision,
                    editingActor,
                    $"auto-query:answer:{query.Id.Value:D}:r{corrected.Revision}",
                    corrected.ReasonForChange),
                metadata,
                cancellationToken).ConfigureAwait(false);

            if (result.Error is { } error
                && error.Code != KernelErrors.AlreadyExists.Code
                && error.Code != FormQueryErrors.InvalidTransition.Code
                && error.Code != AssertionErrors.Locked.Code)
            {
                throw new InvalidOperationException(
                    $"Auto-answering manual query {query.Id.Value:D} on edited observation {corrected.ObservationId} failed: {error.Code} ({error.Message}).");
            }
        }
    }

    private async Task ReconcileAutomatedQueriesAsync(
        TenantId tenant,
        IReadOnlyList<ValidationFindingView> findings,
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
                await EnsureOpenAutoQueryAsync(tenant, finding, formId, formQueries, formRecordCache, metadata, cancellationToken).ConfigureAwait(false);
            }
            else if (finding.Status == FindingStatus.Resolved)
            {
                await CloseResolvedAutoQueriesAsync(tenant, finding, formQueries, metadata, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureOpenAutoQueryAsync(
        TenantId tenant,
        ValidationFindingView finding,
        CollectionInstanceId formId,
        List<FormQuery> formQueries,
        Dictionary<CollectionInstanceId, CollectionInstanceRecord?> formRecordCache,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
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

            episode++;
        }

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

        ObservationId targetObservation;
        int targetRevision;
        if (record.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)) is { } existingValue)
        {
            targetObservation = existingValue.ObservationId;
            targetRevision = existingValue.Revision;
        }
        else
        {
            var derivedObsId = ObservationId.FromGuid(DeriveUnaskedObservationGuid(tenant, formId, itemId));
            var unasked = await sender.SendAsync(
                new RecordUnaskedCollectionItem(
                    tenant,
                    formId,
                    record.EnrollmentId,
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
            record = await forms.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
            formRecordCache[formId] = record;
            if (record is null)
            {
                return;
            }
        }

        if (FormQueryScopes.IsLocked(record, targetObservation))
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
                record.EnrollmentId,
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
            var refreshed = await forms.FindAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
            formRecordCache[formId] = refreshed;
            if (refreshed?.Values.FirstOrDefault(v => v.ObservationId == targetObservation) is { } latest)
            {
                raiseResult = await sender.SendAsync(
                    new RaiseFormQuery(
                        tenant,
                        queryId,
                        formId,
                        refreshed.EnrollmentId,
                        targetObservation,
                        latest.Revision,
                        queryText,
                        $"auto-query:raise:{queryId.Value:D}:r{latest.Revision}",
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

    private async Task CloseResolvedAutoQueriesAsync(
        TenantId tenant,
        ValidationFindingView finding,
        List<FormQuery> formQueries,
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

    internal static FormQueryId DeriveAutoQueryId(TenantId tenant, ValidationFindingId findingId, int episode)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-auto-discrepancy-query:{tenant}:{findingId}:{episode}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return FormQueryId.FromGuid(new Guid(bytes, bigEndian: true));
    }

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
