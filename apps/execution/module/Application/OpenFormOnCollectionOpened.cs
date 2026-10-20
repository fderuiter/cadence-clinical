using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// The second step of opening a form (ADR-2622 §4.1): when the kernel commits <c>CollectionInstanceOpened</c>, opens the
/// Execution form instance as the service account <c>svc:execution-forms</c> with the delivery's idempotency key, so a
/// redelivered event changes nothing twice. A failed command is thrown, and the outbox relay delivers the event again.
/// </summary>
internal sealed class OpenFormOnCollectionOpened(IKernelCommandSender sender) : KernelEventConsumer<CollectionInstanceOpened>
{
    /// <summary>The consumer's name, and so its service account <c>svc:execution-forms</c>.</summary>
    public const string Name = "execution-forms";

    protected override async Task HandleAsync(CollectionInstanceOpened payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(
            new OpenFormInstance(
                payload.TenantId,
                payload.CollectionInstanceId,
                payload.EnrollmentId,
                delivery.IdempotencyKey,
                "The kernel opened the form instance"),
            new CommandMetadata(delivery.Envelope.CorrelationId, delivery.Envelope.EventId),
            cancellationToken).ConfigureAwait(false);

        // An Execution form that already exists was opened by an earlier delivery under another key: nothing to do.
        if (result.Error is { } error && error.Code != KernelErrors.AlreadyExists.Code)
        {
            throw new InvalidOperationException($"Opening the Execution form for {payload.CollectionInstanceId} failed: {error.Code}.");
        }
    }
}
