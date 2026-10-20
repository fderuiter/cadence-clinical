using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Reacts to committed kernel events in the host's process (ADR-2600 §4.6, ADR-2601). The outbox relay delivers each
/// event at least once; a consumer that changes state does so by sending a command with
/// <see cref="KernelEventDelivery.IdempotencyKey"/>, so a redelivered event replays the stored outcome instead of acting
/// twice. Throwing leaves the event in the outbox for a retry, which delivers it to every consumer again.
/// </summary>
public interface IKernelEventConsumer
{
    /// <summary>True when the consumer reacts to <paramref name="envelope"/>.</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Kernel module SDK vocabulary (ADR-2600); modules are written in C#, where it is not a keyword.")]
    bool Handles(EventEnvelope envelope);

    /// <summary>Reacts to one delivery of an event this consumer handles.</summary>
    Task ConsumeAsync(KernelEventDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>One delivery of a committed event to a named consumer.</summary>
/// <param name="Consumer">The consumer's registered name; it acts as the service account <c>svc:&lt;name&gt;</c>.</param>
/// <param name="Envelope">The event.</param>
public sealed record KernelEventDelivery(string Consumer, EventEnvelope Envelope)
{
    /// <summary>
    /// The idempotency key for the one command this delivery may send: <c>&lt;consumer&gt;:&lt;event id&gt;</c>. The
    /// kernel's idempotency records are therefore the consumer's inbox.
    /// </summary>
    public string IdempotencyKey => $"{Consumer}:{Envelope.EventId:D}";

    /// <summary>The service account the consumer's commands run as.</summary>
    public Actor Actor => Actor.ServiceAccount(Consumer);
}

/// <summary>
/// A consumer of one event type, matched by its <see cref="KernelEventAttribute"/> type and schema version, with the
/// payload deserialized as the pipeline serialized it.
/// </summary>
/// <typeparam name="TEvent">The event record, carrying <see cref="KernelEventAttribute"/>.</typeparam>
public abstract class KernelEventConsumer<TEvent> : IKernelEventConsumer
    where TEvent : class
{
    private static readonly KernelEventAttribute Event = typeof(TEvent).GetCustomAttribute<KernelEventAttribute>()
        ?? throw new InvalidOperationException($"{typeof(TEvent).Name} must declare [KernelEvent(type, schemaVersion)].");

    /// <inheritdoc />
    public bool Handles(EventEnvelope envelope) =>
        envelope is not null
        && string.Equals(envelope.Type, Event.Type, StringComparison.Ordinal)
        && envelope.SchemaVersion == Event.SchemaVersion;

    /// <inheritdoc />
    public Task ConsumeAsync(KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var payload = delivery.Envelope.Payload.Deserialize<TEvent>(KernelEventJson.Options)
            ?? throw new JsonException($"The payload of event {delivery.Envelope.EventId} is null.");
        return HandleAsync(payload, delivery, cancellationToken);
    }

    /// <summary>Reacts to the event; a state change goes through a command keyed by <see cref="KernelEventDelivery.IdempotencyKey"/>.</summary>
    protected abstract Task HandleAsync(TEvent payload, KernelEventDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>A registered consumer: its name and the type the host resolves for each delivery.</summary>
/// <param name="Name">Lower case, digits, <c>-</c> and <c>_</c>; unique per host.</param>
/// <param name="ConsumerType">The <see cref="IKernelEventConsumer"/> implementation.</param>
public sealed record KernelEventConsumerRegistration(string Name, Type ConsumerType);

/// <summary>The JSON options the kernel pipeline serializes event payloads with.</summary>
internal static class KernelEventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
