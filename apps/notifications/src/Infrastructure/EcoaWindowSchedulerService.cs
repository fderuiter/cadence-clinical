using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Notifications.Infrastructure;

public sealed class EcoaWindowSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IClock _clock;
    private readonly ILogger<EcoaWindowSchedulerService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    public EcoaWindowSchedulerService(
        IServiceProvider serviceProvider,
        IClock clock,
        ILogger<EcoaWindowSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("eCOA Visit Window Push Notification Scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSchedulerCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during eCOA visit window scheduler execution cycle.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task<int> RunSchedulerCycleAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var pushService = scope.ServiceProvider.GetRequiredService<IVapidPushService>();

        var activeSubscriptions = await dbContext.PushSubscriptions
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);

        if (activeSubscriptions.Count == 0)
        {
            _logger.LogDebug("No active push subscriptions found for eCOA notification dispatch.");
            return 0;
        }

        int notificationsSent = 0;
        foreach (var subscription in activeSubscriptions)
        {
            // Non-sensitive HIPAA and Part 11 compliant push notification payload
            var payload = new WebPushNotificationPayload(
                Title: "Clinical Questionnaire Ready",
                Body: "You have a trial questionnaire ready to complete. Please tap to view.",
                Icon: "/subject-portal/icon.png",
                Badge: "/subject-portal/badge.png",
                TargetUrl: "/subject-portal/#view-questionnaire",
                QuestionnaireId: "inst_daily_diary",
                Tag: $"ecoa-window-{subscription.SubjectId}-{_clock.UtcNow:yyyyMMdd}"
            );

            var result = await pushService.SendNotificationAsync(subscription, payload, cancellationToken);
            if (result.Success)
            {
                notificationsSent++;
            }
        }

        _logger.LogInformation("eCOA scheduler cycle completed. Dispatched {Count} push alerts.", notificationsSent);
        return notificationsSent;
    }
}
