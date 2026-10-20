using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Application.Events;

/// <summary>
/// Where the outbox relay delivers committed kernel events (spec §5.1 "Outbox relay → subscribers"). The host provides
/// it: a message bus, in-process projections, or both. Delivery is at-least-once, so a subscriber deduplicates on
/// <see cref="EventEnvelope.EventId"/>. Throwing leaves the event in the outbox for a later retry.
/// </summary>
public interface IKernelEventPublisher
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
