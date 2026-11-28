using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    private static readonly KernelEventAttribute AcceptedEvent = typeof(FindingAccepted).GetCustomAttribute<KernelEventAttribute>()!;
    private static readonly KernelEventAttribute ExplainedEvent = typeof(FindingExplained).GetCustomAttribute<KernelEventAttribute>()!;
    // jsonb reorders object members, so a polymorphic ObservationValue's "type" member may follow "code" or "text".
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
    private static readonly string SelfActorId = Actor.ServiceAccount(Name).Id;

    public bool Handles(EventEnvelope envelope) =>
        envelope is not null
        && ((string.Equals(envelope.Type, SavedEvent.Type, StringComparison.Ordinal) && envelope.SchemaVersion == SavedEvent.SchemaVersion)
            || (string.Equals(envelope.Type, CorrectedEvent.Type, StringComparison.Ordinal) && envelope.SchemaVersion == CorrectedEvent.SchemaVersion)
            || (string.Equals(envelope.Type, AcceptedEvent.Type, StringComparison.Ordinal) && envelope.SchemaVersion == AcceptedEvent.SchemaVersion)
            || (string.Equals(envelope.Type, ExplainedEvent.Type, StringComparison.Ordinal) && envelope.SchemaVersion == ExplainedEvent.SchemaVersion));

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
        if (string.Equals(delivery.Envelope.Type, AcceptedEvent.Type, StringComparison.Ordinal))
        {
            var accepted = delivery.Envelope.Payload.Deserialize<FindingAccepted>(EventJsonOptions)
                ?? throw new JsonException($"The payload of event {delivery.Envelope.EventId} is null.");
            await CloseConcludedFindingQueriesAsync(
                accepted.TenantId,
                accepted.FindingId,
                accepted.DetectedBy,
                FindingStatus.Accepted,
                metadata,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(delivery.Envelope.Type, ExplainedEvent.Type, StringComparison.Ordinal))
        {
            var explained = delivery.Envelope.Payload.Deserialize<FindingExplained>(EventJsonOptions)
                ?? throw new JsonException($"The payload of event {delivery.Envelope.EventId} is null.");
            await CloseConcludedFindingQueriesAsync(
                explained.TenantId,
                explained.FindingId,
                explained.DetectedBy,
                FindingStatus.Explained,
                metadata,
                cancellationToken).ConfigureAwait(false);
            return;
        }

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
            if (obs?.CollectionInstanceId is null || obs.StudyId.IsEmpty)
            {
                return;
            }

            studyId = obs.StudyId;
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

        await AutomatedQueryReconciler.ReconcileAutomatedQueriesAsync(
            tenant,
            findings,
            forms,
            contexts,
            sender,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    internal static FormQueryId DeriveAutoQueryId(TenantId tenant, ValidationFindingId findingId, int episode) =>
        AutomatedQueryReconciler.DeriveAutoQueryId(tenant, findingId, episode);

    private async Task CloseConcludedFindingQueriesAsync(
        TenantId tenant,
        ValidationFindingId findingId,
        ValidationRunId detectedBy,
        FindingStatus decisionStatus,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var finding = await findingsReader.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (finding is null || finding.Status is not (FindingStatus.Accepted or FindingStatus.Explained))
        {
            return;
        }

        CollectionInstanceId formId;
        if (finding.Target.Kind == FindingTargetKind.DatasetRecord
            && Guid.TryParse(finding.Target.Key, out var parsedFormGuid)
            && parsedFormGuid != Guid.Empty)
        {
            formId = CollectionInstanceId.FromGuid(parsedFormGuid);
        }
        else if (finding.Target.Kind == FindingTargetKind.Observation
            && finding.Target.Node is { } obsNode)
        {
            var obs = await observations.FindAsync(tenant, ObservationId.FromGuid(obsNode.Id), cancellationToken).ConfigureAwait(false);
            if (obs?.CollectionInstanceId is not { } parentFormId)
            {
                return;
            }

            formId = parentFormId;
        }
        else
        {
            return;
        }

        var scope = await forms.FindScopeStateAsync(tenant, formId, cancellationToken).ConfigureAwait(false);
        if (scope is null)
        {
            return;
        }

        var enrollmentForms = await forms.ListRuleInputsForEnrollmentAsync(tenant, scope.EnrollmentId, cancellationToken).ConfigureAwait(false);
        var targetInput = enrollmentForms.FirstOrDefault(f => f.CollectionInstanceId == formId)
            ?? new CollectionRuleInput(
                scope.CollectionInstanceId,
                scope.EnrollmentId,
                scope.StudyId,
                scope.ProtocolVersionId,
                scope.CollectionSpecId,
                DateTimeOffset.MinValue,
                [],
                [.. scope.Values.Select(v => new CollectionRuleValue(v.ItemId, v.ObservationId, v.Value))]);
        if (!ObservedValueComparator.AreObservedValuesUnchanged(finding, targetInput, enrollmentForms))
        {
            return;
        }

        List<FormQuery> formQueries;
        await using (var db = contexts.Create(tenant))
        {
            formQueries = await db.FormQueries.AsNoTracking()
                .Where(q => q.FormId == formId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var currentRevisions = scope.Values.ToDictionary(v => v.ObservationId, v => v.Revision);
        await AutomatedQueryReconciler.CloseConcludedAutoQueriesOnUnchangedValuesAsync(
            tenant,
            finding,
            currentRevisions,
            formQueries,
            detectedBy,
            decisionStatus,
            sender,
            metadata,
            cancellationToken).ConfigureAwait(false);
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
}
