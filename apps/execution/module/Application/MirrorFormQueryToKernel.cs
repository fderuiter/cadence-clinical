using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// States each query step as a kernel assertion (#5461, spec §8.2): when Execution commits
/// <see cref="FormQueryChanged"/>, sends the kernel's <see cref="RaiseQuery"/>, <see cref="AnswerQuery"/> or
/// <see cref="RetractAssertion"/> as the service account <c>svc:execution-queries</c>, keyed by the delivery, with the
/// assertion ids the query chose. A redelivered event replays the stored outcome, so each assertion is written once. A
/// refusal is thrown and the relay delivers the event again; one refused because a Locked lock now covers the value
/// waits for its release, since nothing more is asserted under a lock.
/// </summary>
internal sealed class MirrorFormQueryToKernel(IKernelCommandSender sender) : KernelEventConsumer<FormQueryChanged>
{
    /// <summary>The consumer's name, and so its service account <c>svc:execution-queries</c>.</summary>
    public const string Name = "execution-queries";

    protected override async Task HandleAsync(FormQueryChanged payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        if (payload.ObservationId is not { } observationId || payload.Steps.Count == 0)
        {
            return;
        }

        var metadata = new CommandMetadata(delivery.Envelope.CorrelationId, delivery.Envelope.EventId);
        var reason = $"Execution query {payload.QueryId:D} is {payload.Status.ToString().ToLowerInvariant()}";
        for (var i = 0; i < payload.Steps.Count; i++)
        {
            if (payload.Steps[i] is not { } step)
            {
                continue;
            }

            var key = $"{delivery.IdempotencyKey}:{i}";
            var result = step.Kind switch
            {
                KernelAssertionStepKind.Raise => await sender.SendAsync(
                    new RaiseQuery(payload.TenantId, step.AssertionId, payload.EnrollmentId, observationId, step.Text!, key, reason),
                    metadata,
                    cancellationToken).ConfigureAwait(false),
                KernelAssertionStepKind.Answer => await sender.SendAsync(
                    new AnswerQuery(payload.TenantId, step.AssertionId, payload.EnrollmentId, step.TargetId!.Value, step.Text!, key, reason),
                    metadata,
                    cancellationToken).ConfigureAwait(false),
                _ => await sender.SendAsync(
                    new RetractAssertion(payload.TenantId, step.AssertionId, payload.EnrollmentId, step.TargetId!.Value, key, reason),
                    metadata,
                    cancellationToken).ConfigureAwait(false),
            };

            // An assertion that already exists was written by an earlier delivery: nothing to do for that step.
            if (result.Error is { } error && error.Code != KernelErrors.AlreadyExists.Code)
            {
                throw new InvalidOperationException(
                    $"Stating step {i} ({step.Kind}) of Execution query {payload.QueryId:D} in the kernel failed: {error.Code}.");
            }
        }
    }
}
