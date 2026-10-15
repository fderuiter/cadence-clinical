using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Apps.Notifications.Infrastructure.Consumers;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Templating;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Notifications.Consumers;

public class ParticipantEngagementConsumerTests
{
    static ParticipantEngagementConsumerTests()
    {
        FluidTemplateRenderer.RegisterType<NotificationPayload>();
        FluidTemplateRenderer.RegisterType<NotificationRenderModel>();
        FluidTemplateRenderer.RegisterType<EngagementNudgePayload>();
    }

    private static ServiceProvider CreateServiceProvider(NotificationsDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddFluidTemplating();
        services.AddSingleton<IOptions<NotificationBrandingOptions>>(Options.Create(new NotificationBrandingOptions
        {
            BrandName = "Cadence Clinical",
            PortalUrl = "https://participant.cadenceclinical.org"
        }));

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<ParticipantEngagementConsumer>();
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ValidParticipantStreakMilestoneAchieved_CreatesNotificationRecordAndDeliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ParticipantStreakMilestoneAchieved(
            SubjectId: "SUBJ-100",
            StudyId: "STUDY-001",
            MilestoneType: "STREAK_7_DAY",
            StreakCount: 7,
            BonusAmount: 25.00m,
            AchievedAt: clock.UtcNow,
            Message: "Awesome job completing 7 days in a row!");

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<ParticipantStreakMilestoneAchieved>(ct)).Should().BeTrue();

        string expectedRelatedEntityId = "STUDY-001:SUBJ-100:STREAK_7_DAY:7";
        var record = await dbContext.NotificationRecords
            .FirstOrDefaultAsync(r => r.RelatedEntityId == expectedRelatedEntityId, ct);

        record.Should().NotBeNull();
        record!.RecipientUserId.Should().Be("SUBJ-100");
        record.RecipientRoleId.Should().Be("Participant");
        record.Category.Should().Be("ParticipantEngagement");
        record.RelatedEntityType.Should().Be("ParticipantEngagement");
        record.MessageContent.Should().Contain("SUBJ-100");
        record.MessageContent.Should().Contain("STREAK_7_DAY");
        record.MessageContent.Should().Contain("7 Days");

        var deliveries = await dbContext.NotificationDeliveries
            .Where(d => d.NotificationId == record.Id)
            .ToListAsync(ct);
        deliveries.Should().HaveCount(3);
        deliveries.Select(d => d.Channel).Should().BeEquivalentTo("InApp", "SMS", "MobilePush");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_DuplicateParticipantStreakMilestoneAchieved_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ParticipantStreakMilestoneAchieved(
            SubjectId: "SUBJ-100",
            StudyId: "STUDY-001",
            MilestoneType: "STREAK_7_DAY",
            StreakCount: 7,
            BonusAmount: 25.00m,
            AchievedAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<ParticipantStreakMilestoneAchieved>(ct)).Should().BeTrue();

        string expectedRelatedEntityId = "STUDY-001:SUBJ-100:STREAK_7_DAY:7";
        int initialCount = await dbContext.NotificationRecords.CountAsync(r => r.RelatedEntityId == expectedRelatedEntityId, ct);
        initialCount.Should().Be(1);

        // Publish duplicate event
        await harness.Bus.Publish(message, ct);

        int countAfterDuplicate = await dbContext.NotificationRecords.CountAsync(r => r.RelatedEntityId == expectedRelatedEntityId, ct);
        countAfterDuplicate.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ValidEngagementNudgeTriggered_CreatesNotificationRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EngagementNudgeTriggered(
            SubjectId: "SUBJ-200",
            StudyId: "STUDY-001",
            NudgeType: "DAILY_REMINDER",
            CurrentStreak: 5,
            TriggeredAt: clock.UtcNow,
            Message: "Don't forget your daily survey today!");

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<EngagementNudgeTriggered>(ct)).Should().BeTrue();

        var record = await dbContext.NotificationRecords
            .FirstOrDefaultAsync(r => r.RecipientUserId == "SUBJ-200" && r.RelatedEntityType == "EngagementNudge", ct);

        record.Should().NotBeNull();
        record!.Category.Should().Be("ParticipantEngagement");
        record.MessageContent.Should().Contain("SUBJ-200");
        record.MessageContent.Should().Contain("daily survey");
    }

    [Fact]
    public async Task Consume_MalformedEvent_SkippedGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ParticipantStreakMilestoneAchieved(
            SubjectId: "",
            StudyId: "STUDY-001",
            MilestoneType: "STREAK_3_DAY",
            StreakCount: 3,
            BonusAmount: 25.00m,
            AchievedAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<ParticipantStreakMilestoneAchieved>(ct)).Should().BeTrue();

        var count = await dbContext.NotificationRecords.CountAsync(ct);
        count.Should().Be(0);
    }
}
