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

public class EconsentReconsentConsumerTests
{
    static EconsentReconsentConsumerTests()
    {
        FluidTemplateRenderer.RegisterType<NotificationPayload>();
        FluidTemplateRenderer.RegisterType<NotificationRenderModel>();
    }

    private static ServiceProvider CreateServiceProvider(NotificationsDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddFluidTemplating();
        services.AddSingleton<IEnrolledSubjectResolver, DefaultEnrolledSubjectResolver>();
        services.AddSingleton<IOptions<NotificationBrandingOptions>>(Options.Create(new NotificationBrandingOptions
        {
            BrandName = "Cadence Clinical",
            PortalUrl = "https://participant.cadenceclinical.org"
        }));

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<EconsentReconsentConsumer>();
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ValidEconsentReconsentRequired_CreatesNotificationRecordDeliveriesAndAuditLogs()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EconsentReconsentRequired(
            StudyId: "STUDY-101",
            TemplateId: "tpl-001",
            VersionIndex: 2,
            ChangeSummary: "Updated dosing and procedure risks.",
            Channels: new[] { "SMS", "Mobile Push", "Email" },
            ProtocolVersion: "v2.0",
            OccurredAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<EconsentReconsentRequired>(ct)).Should().BeTrue();

        var records = await dbContext.NotificationRecords.Where(r => r.RelatedEntityType == "EconsentReconsent").ToListAsync(ct);
        records.Should().NotBeEmpty();

        var rec = records[0];
        rec.Category.Should().Be("EconsentReconsent");
        rec.Priority.Should().Be("URGENT");
        rec.Channels.Should().Be("SMS,Mobile Push,Email");

        var deliveries = await dbContext.NotificationDeliveries.Where(d => d.NotificationId == rec.Id).ToListAsync(ct);
        deliveries.Should().HaveCount(3);
        deliveries.Select(d => d.Channel).Should().BeEquivalentTo("SMS", "Mobile Push", "Email");

        var auditLogs = await dbContext.NotificationAuditLogs.Where(a => a.Action == "ECONSENT_RECONSENT_DISPATCH").ToListAsync(ct);
        auditLogs.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Consume_DuplicateEconsentReconsentRequired_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EconsentReconsentRequired(
            StudyId: "STUDY-101",
            TemplateId: "tpl-002",
            VersionIndex: 1,
            ChangeSummary: "Initial version requiring re-consent.",
            Channels: new[] { "SMS", "Email" },
            ProtocolVersion: "v1.0",
            OccurredAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<EconsentReconsentRequired>(ct)).Should().BeTrue();

        int initialCount = await dbContext.NotificationRecords.CountAsync(r => r.RelatedEntityType == "EconsentReconsent", ct);
        initialCount.Should().BeGreaterThan(0);

        // Publish duplicate message
        await harness.Bus.Publish(message, ct);

        int countAfterDuplicate = await dbContext.NotificationRecords.CountAsync(r => r.RelatedEntityType == "EconsentReconsent", ct);
        countAfterDuplicate.Should().Be(initialCount);
    }

    [Fact]
    public async Task Consume_MalformedEvent_SkippedGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EconsentReconsentRequired(
            StudyId: "",
            TemplateId: "",
            VersionIndex: 1,
            ChangeSummary: "Empty study and template id.");

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<EconsentReconsentRequired>(ct)).Should().BeTrue();

        var count = await dbContext.NotificationRecords.CountAsync(ct);
        count.Should().Be(0);
    }
}
