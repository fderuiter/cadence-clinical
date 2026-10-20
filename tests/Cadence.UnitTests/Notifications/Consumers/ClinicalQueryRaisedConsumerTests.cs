using System;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Infrastructure.Consumers;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Notifications.Consumers;

public class ClinicalQueryRaisedConsumerTests
{
    private static ServiceProvider CreateServiceProvider(NotificationsDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<ClinicalQueryRaisedConsumer>();
        });

        // The harness stops waiting for messages after this long without bus activity. Its default is about a second,
        // and on a loaded machine the duplicate's delivery can take longer than that.
        services.Configure<TestHarnessOptions>(options => options.TestInactivityTimeout = TimeSpan.FromSeconds(30));
        return services.BuildServiceProvider();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ValidClinicalQueryRaised_CreatesNotificationRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalQueryRaised(
            QueryId: "QRY-1001",
            StudyId: "STUDY-01",
            SubjectId: "SUBJ-01",
            SiteId: "SITE-01",
            TestCode: "SYSBP",
            Explanation: "BP out of normal range",
            RaisedBy: "cra-user-1",
            RaisedAt: clock.UtcNow,
            QueryType: "DISCREPANCY",
            Priority: "HIGH");

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<ClinicalQueryRaised>(ct)).Should().BeTrue();

        var record = await dbContext.NotificationRecords.FirstOrDefaultAsync(r => r.RelatedEntityId == "QRY-1001", ct);
        record.Should().NotBeNull();
        record!.RelatedEntityType.Should().Be("ClinicalQuery");
        record.Category.Should().Be("ClinicalQuery");
        record.Priority.Should().Be("HIGH");
        record.MessageContent.Should().Contain("QRY-1001");
        record.MessageContent.Should().Contain("SUBJ-01");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_DuplicateClinicalQueryRaised_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalQueryRaised(
            QueryId: "QRY-1002",
            StudyId: "STUDY-01",
            SubjectId: "SUBJ-01",
            SiteId: "SITE-01",
            TestCode: "SYSBP",
            Explanation: "BP out of normal range",
            RaisedBy: "cra-user-1",
            RaisedAt: clock.UtcNow,
            QueryType: "DISCREPANCY",
            Priority: "HIGH");

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<ClinicalQueryRaised>(ct)).Should().BeTrue();

        // Publish duplicate message, and wait until the consumer has finished with it before reading the context
        await harness.Bus.Publish(message, ct);
        int consumed = 0;
        await foreach (var _ in harness.Consumed.SelectAsync<ClinicalQueryRaised>(ct))
        {
            if (++consumed == 2)
            {
                break;
            }
        }

        consumed.Should().Be(2);

        var recordCount = await dbContext.NotificationRecords.CountAsync(r => r.RelatedEntityId == "QRY-1002", ct);
        recordCount.Should().Be(1);
    }

    [Fact]
    public async Task Consume_MalformedEvent_SkippedGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        await using var provider = CreateServiceProvider(dbContext);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalQueryRaised(
            QueryId: "",
            StudyId: "STUDY-01",
            SubjectId: "SUBJ-01",
            SiteId: "SITE-01",
            TestCode: "SYSBP",
            Explanation: "Empty QueryId",
            RaisedBy: "cra-user-1",
            RaisedAt: clock.UtcNow,
            QueryType: "DISCREPANCY",
            Priority: "HIGH");

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<ClinicalQueryRaised>(ct)).Should().BeTrue();

        var count = await dbContext.NotificationRecords.CountAsync(ct);
        count.Should().Be(0);
    }
}
