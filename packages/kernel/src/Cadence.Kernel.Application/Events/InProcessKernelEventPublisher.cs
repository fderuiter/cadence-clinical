using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Events;

/// <summary>
/// Delivers committed kernel events to the consumers registered in this host (ADR-2600 §4.6, ADR-2601). Each consumer
/// gets its own scope in which <see cref="IKernelCommandSender"/> acts as the consumer's service account, so the
/// commands it sends are attributed and authorized as <c>svc:&lt;name&gt;</c> (K-TX-5). Every consumer that handles the
/// event is called even when an earlier one fails; any failure is rethrown so the relay retries the event, and the
/// consumers that already succeeded replay their stored command outcome on the retry.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class InProcessKernelEventPublisher(
    IServiceScopeFactory scopes,
    IEnumerable<KernelEventConsumerRegistration> consumers) : IKernelEventPublisher
{
    private readonly KernelEventConsumerRegistration[] _consumers = [.. consumers.OrderBy(c => c.Name, StringComparer.Ordinal)];

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        List<Exception> failures = [];
        foreach (var registration in _consumers)
        {
            await using var scope = scopes.CreateAsyncScope();
            var consumer = (IKernelEventConsumer)scope.ServiceProvider.GetRequiredService(registration.ConsumerType);
            if (!consumer.Handles(envelope))
            {
                continue;
            }

            var delivery = new KernelEventDelivery(registration.Name, envelope);
            scope.ServiceProvider.GetRequiredService<ConsumerActor>().Actor = delivery.Actor;
            try
            {
                await consumer.ConsumeAsync(delivery, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(new InvalidOperationException($"Kernel event consumer '{registration.Name}' failed on {envelope.Type} {envelope.EventId}.", ex));
            }
        }

        if (failures.Count > 0)
        {
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }
    }
}

/// <summary>
/// The service account a consumer's scope acts as; when set, the command pipeline uses it instead of the host's
/// <see cref="Commands.ICurrentActor"/>. Only <see cref="InProcessKernelEventPublisher"/> sets it.
/// </summary>
public sealed class ConsumerActor
{
    /// <summary>The consumer's service account, or null outside a consumer's scope.</summary>
    public Actor? Actor { get; internal set; }
}

/// <summary>Registers in-process delivery of committed kernel events.</summary>
public static class KernelEventDeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Makes the consumers registered with <see cref="ModuleCommandServiceCollectionExtensions.AddKernelEventConsumer"/>
    /// the host's <see cref="IKernelEventPublisher"/>, which the outbox relay delivers to (ADR-2601).
    /// </summary>
    public static IServiceCollection AddKernelInProcessEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IKernelEventPublisher, InProcessKernelEventPublisher>();
        return services;
    }
}
