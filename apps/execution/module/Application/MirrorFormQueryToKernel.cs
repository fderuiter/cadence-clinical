using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// States each query step as a kernel assertion (#5461, spec §8.2, <c>PRD-QRY-001</c>): when Execution commits
/// <see cref="FormQueryChanged"/>, sends the kernel's <see cref="RaiseQuery"/>, <see cref="AnswerQuery"/> or
/// <see cref="RetractAssertion"/> as the service account <c>svc:execution-queries</c>, keyed deterministically by step
/// assertion ID so synchronous projection and outbox replay share idempotency, while terminal errors raise
/// <see cref="KernelOutboxPoisonMessageException"/> so only the affected aggregate stream is quarantined.
/// </summary>
internal sealed class MirrorFormQueryToKernel(IKernelCommandSender sender) : KernelEventConsumer<FormQueryChanged>
{
    /// <summary>The consumer's name, and so its service account <c>svc:execution-queries</c>.</summary>
    public const string Name = "execution-queries";

    protected override async Task HandleAsync(FormQueryChanged payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        if (payload.Steps.Count == 0)
        {
            return;
        }

        var metadata = new CommandMetadata(delivery.Envelope.CorrelationId, delivery.Envelope.EventId);
        var reason = string.IsNullOrWhiteSpace(payload.ReasonForChange)
            ? $"Execution query {payload.QueryId.Value:D} is {payload.Status.ToString().ToLowerInvariant()}"
            : payload.ReasonForChange;
        var actorId = string.IsNullOrWhiteSpace(payload.ActorId) ? delivery.Envelope.Actor : payload.ActorId;

        for (var i = 0; i < payload.Steps.Count; i++)
        {
            var step = payload.Steps[i];
            var key = FormQueryKernelProjector.StepIdempotencyKey(step);
            var result = step.Kind switch
            {
                KernelAssertionStepKind.Raise => await sender.SendAsync(
                    new RaiseQuery(
                        payload.TenantId,
                        step.AssertionId,
                        payload.EnrollmentId,
                        payload.ObservationId,
                        step.Text!,
                        key,
                        reason,
                        Revision: payload.Revision,
                        AssertedBy: actorId),
                    metadata,
                    cancellationToken).ConfigureAwait(false),
                KernelAssertionStepKind.Answer => await sender.SendAsync(
                    new AnswerQuery(
                        payload.TenantId,
                        step.AssertionId,
                        payload.EnrollmentId,
                        step.TargetId!.Value,
                        step.Text!,
                        key,
                        reason,
                        Revision: payload.Revision,
                        AssertedBy: actorId),
                    metadata,
                    cancellationToken).ConfigureAwait(false),
                _ => await sender.SendAsync(
                    new RetractAssertion(
                        payload.TenantId,
                        step.AssertionId,
                        payload.EnrollmentId,
                        step.TargetId!.Value,
                        key,
                        reason,
                        AssertedBy: actorId),
                    metadata,
                    cancellationToken).ConfigureAwait(false),
            };

            // An assertion that already exists was written synchronously or by an earlier delivery: nothing to do for that step.
            if (result.Error is { } error && error.Code != KernelErrors.AlreadyExists.Code)
            {
                var message = $"Stating step {i} ({step.Kind}) of Execution query {payload.QueryId.Value:D} in the kernel failed: {error.Code}.";
                if (error.Code == AssertionErrors.Locked.Code)
                {
                    throw new InvalidOperationException(message);
                }

                throw new KernelOutboxPoisonMessageException(message);
            }
        }
    }
}
