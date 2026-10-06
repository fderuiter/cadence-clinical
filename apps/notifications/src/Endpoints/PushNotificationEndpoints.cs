using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Notifications.Endpoints;

public static class PushNotificationEndpoints
{
    public static RouteGroupBuilder MapPushNotificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/vapid-public-key", GetVapidPublicKey)
            .WithName("GetVapidPublicKey")
            .WithSummary("Gets the VAPID public key for Web Push subscription negotiation.")
            .AllowAnonymous();

        group.MapPost("/subscriptions", RegisterPushSubscriptionAsync)
            .WithName("RegisterPushSubscription")
            .WithSummary("Registers or updates a participant device Web Push subscription.")
            .AllowAnonymous();

        group.MapDelete("/subscriptions", UnsubscribePushSubscriptionAsync)
            .WithName("UnsubscribePushSubscription")
            .WithSummary("Deactivates or removes a device Web Push subscription.")
            .AllowAnonymous();

        group.MapPost("/trigger-ecoa-job", TriggerEcoaJobAsync)
            .WithName("TriggerEcoaPushJob")
            .WithSummary("Manually triggers the background eCOA visit window push scheduler job.")
            .AllowAnonymous();

        return group;
    }

    public static Ok<VapidPublicKeyResponse> GetVapidPublicKey(IVapidPushService pushService)
    {
        string key = pushService.GetPublicKey();
        return TypedResults.Ok(new VapidPublicKeyResponse(key));
    }

    public static async Task<Results<Ok<PushSubscription>, BadRequest<string>>> RegisterPushSubscriptionAsync(
        PushSubscriptionRequest request,
        NotificationsDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return TypedResults.BadRequest("SubjectId and Endpoint are required.");
        }

        var existing = await dbContext.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);

        if (existing is not null)
        {
            existing.SubjectId = request.SubjectId;
            existing.P256dh = request.P256dh; // deid-ignore: VAPID P-256 public key property
            existing.Auth = request.Auth;
            existing.UserAgent = request.UserAgent;
            existing.IsActive = true;
            existing.UpdatedAt = clock.UtcNow;
            existing.ReasonForChange = "Updated Web Push subscription endpoint";

            await dbContext.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(existing);
        }

        var subscription = new PushSubscription
        {
            SubjectId = request.SubjectId,
            Endpoint = request.Endpoint,
            P256dh = request.P256dh, // deid-ignore: VAPID P-256 public key property
            Auth = request.Auth,
            UserAgent = request.UserAgent,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedBy = request.SubjectId,
            ReasonForChange = "Initial Web Push subscription enrollment"
        };

        dbContext.PushSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(subscription);
    }

    public static async Task<Results<Ok<string>, NotFound<string>>> UnsubscribePushSubscriptionAsync(
        [FromQuery] string endpoint,
        NotificationsDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return TypedResults.NotFound("Endpoint query parameter missing.");
        }

        var existing = await dbContext.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

        if (existing is null)
        {
            return TypedResults.NotFound("Subscription endpoint not found.");
        }

        existing.IsActive = false;
        existing.UpdatedAt = clock.UtcNow;
        existing.ReasonForChange = "User requested unsubscription";

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok("Subscription deactivated successfully.");
    }

    public static async Task<Ok<EcoaJobResponse>> TriggerEcoaJobAsync(
        EcoaWindowSchedulerService schedulerService,
        CancellationToken cancellationToken)
    {
        int count = await schedulerService.RunSchedulerCycleAsync(cancellationToken);
        return TypedResults.Ok(new EcoaJobResponse(count));
    }
}

public record VapidPublicKeyResponse(string PublicKey);
public record EcoaJobResponse(int DispatchedCount);
