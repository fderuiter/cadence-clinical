using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Alerts CRAs when a previously source-data-verified observation revision is modified (<c>PRD-QRY-006</c>, ADR-2620,
/// ADR-2621): reads <see cref="IObservationRecords"/> (<c>K-XS-2</c>) on <see cref="ObservationCorrected"/> and sends an
/// idempotent <see cref="RecordFormSdvAlert"/> command as <c>svc:execution-sdv-alerts</c> when
/// <see cref="ObservationCorrected.Supersedes"/> held an active <see cref="AssertionKind.Verified"/> assertion at the
/// moment of the correction.
/// </summary>
internal sealed class AlertMonitorOnVerifiedObservationCorrected(
    IObservationRecords observations,
    ICollectionInstanceRecords forms,
    IKernelCommandSender sender) : KernelEventConsumer<ObservationCorrected>
{
    /// <summary>The consumer's name, and so its service account <c>svc:execution-sdv-alerts</c>.</summary>
    public const string Name = "execution-sdv-alerts";

    protected override async Task HandleAsync(ObservationCorrected payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        var observation = await observations.FindAsync(payload.TenantId, payload.ObservationId, cancellationToken).ConfigureAwait(false);
        if (observation is null)
        {
            return;
        }

        var correctingRevision = observation.Revisions.FirstOrDefault(r => r.Number == payload.Revision);
        var cutoff = correctingRevision?.RecordedAt ?? delivery.Envelope.Recorded;
        var verified = observation.Assertions
            .Where(a => a.Kind == AssertionKind.Verified
                && a.Revision == payload.Supersedes
                && (a.RetractedAt is null || a.RetractedAt > cutoff))
            .OrderByDescending(a => a.AssertedAt)
            .FirstOrDefault();
        if (verified is null)
        {
            return;
        }

        StudySiteId? studySiteId = null;
        if (observation.CollectionInstanceId is { IsEmpty: false } formId
            && await forms.FindAsync(payload.TenantId, formId, cancellationToken).ConfigureAwait(false) is { } formRecord
            && !formRecord.StudySiteId.IsEmpty)
        {
            studySiteId = formRecord.StudySiteId;
        }

        var modifiedBy = !string.IsNullOrWhiteSpace(correctingRevision?.RecordedBy)
            ? correctingRevision.RecordedBy
            : delivery.Envelope.Actor;
        var modifiedAt = correctingRevision?.RecordedAt ?? delivery.Envelope.Recorded;
        var reason = !string.IsNullOrWhiteSpace(payload.ReasonForChange)
            ? payload.ReasonForChange
            : (correctingRevision?.ReasonForChange ?? "Verified observation modified");

        var result = await sender.SendAsync(
            new RecordFormSdvAlert(
                payload.TenantId,
                FormSdvAlert.CreateId(payload.TenantId, payload.ObservationId, payload.Revision),
                observation.StudyId,
                studySiteId,
                observation.EnrollmentId,
                observation.CollectionInstanceId,
                observation.ItemId,
                payload.ObservationId,
                payload.Supersedes,
                payload.Revision,
                verified.AssertedBy,
                verified.AssertedAt,
                modifiedBy,
                modifiedAt,
                delivery.IdempotencyKey,
                reason),
            new CommandMetadata(delivery.Envelope.CorrelationId, delivery.Envelope.EventId),
            cancellationToken).ConfigureAwait(false);

        if (result.Error is { } error && error.Code != KernelErrors.AlreadyExists.Code)
        {
            throw new InvalidOperationException(
                $"Recording CRA SDV modification alert for observation {payload.ObservationId.Value:D} revision {payload.Revision} failed: {error.Code}.");
        }
    }
}
