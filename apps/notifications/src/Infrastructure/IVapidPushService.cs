using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;

namespace Cadence.Apps.Notifications.Infrastructure;

public record PushDispatchResult(
    bool Success,
    int StatusCode,
    string? ErrorMessage = null,
    bool IsExpired = false);

public interface IVapidPushService
{
    string GetPublicKey();

    Task<PushDispatchResult> SendNotificationAsync(
        PushSubscription subscription,
        WebPushNotificationPayload payload,
        CancellationToken cancellationToken = default);
}
