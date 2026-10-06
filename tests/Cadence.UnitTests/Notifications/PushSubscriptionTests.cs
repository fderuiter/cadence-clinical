using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Notifications;

public sealed class PushSubscriptionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PushSubscription_CanBeCreatedAndPersistedInDbContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new NotificationsDbContext(options);

        var subscription = new PushSubscription
        {
            SubjectId = "SUBJ-101",
            Endpoint = "https://push.example.com/sub/123",
            P256dh = "BEl62iUYg...",
            Auth = "authSecret123",
            UserAgent = "Mozilla/5.0 TestBrowser",
            IsActive = true,
            CreatedAt = SystemClock.Instance.UtcNow,
            CreatedBy = "SUBJ-101",
            ReasonForChange = "Test creation"
        };

        dbContext.PushSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(Ct);

        var retrieved = await dbContext.PushSubscriptions
            .FirstOrDefaultAsync(s => s.SubjectId == "SUBJ-101", Ct);

        retrieved.Should().NotBeNull();
        retrieved!.Endpoint.Should().Be("https://push.example.com/sub/123");
        retrieved.P256dh.Should().Be("BEl62iUYg...");
        retrieved.Auth.Should().Be("authSecret123");
        retrieved.IsActive.Should().BeTrue();
    }

    [Fact]
    public void VapidPushService_GetPublicKey_ReturnsConfiguredKey()
    {
        var vapidOptions = Options.Create(new VapidOptions
        {
            PublicKey = "custom-test-vapid-public-key-1234567890"
        });

        using var httpClient = new HttpClient();
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new NotificationsDbContext(options);

        var service = new VapidPushService(
            httpClient,
            dbContext,
            vapidOptions,
            SystemClock.Instance,
            NullLogger<VapidPushService>.Instance);

        service.GetPublicKey().Should().Be("custom-test-vapid-public-key-1234567890");
    }

    [Fact]
    public void VapidPushService_GetPublicKey_GeneratesDefaultKey_WhenOptionsEmpty()
    {
        var vapidOptions = Options.Create(new VapidOptions());

        using var httpClient = new HttpClient();
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new NotificationsDbContext(options);

        var service = new VapidPushService(
            httpClient,
            dbContext,
            vapidOptions,
            SystemClock.Instance,
            NullLogger<VapidPushService>.Instance);

        service.GetPublicKey().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendNotificationAsync_ReturnsSuccess_WhenEndpointResponds200Ok()
    {
        var handler = new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(options);

        var service = new VapidPushService(
            httpClient,
            dbContext,
            Options.Create(new VapidOptions()),
            SystemClock.Instance,
            NullLogger<VapidPushService>.Instance);

        var sub = new PushSubscription
        {
            SubjectId = "SUBJ-201",
            Endpoint = "https://push.example.com/sub/201",
            IsActive = true
        };

        var payload = new WebPushNotificationPayload("Title", "Body");
        var result = await service.SendNotificationAsync(sub, payload, Ct);

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.IsExpired.Should().BeFalse();
        sub.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SendNotificationAsync_DeactivatesSubscription_WhenEndpointResponds410Gone()
    {
        var handler = new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)));

        using var httpClient = new HttpClient(handler);
        var dbOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(dbOptions);

        var sub = new PushSubscription
        {
            SubjectId = "SUBJ-301",
            Endpoint = "https://push.example.com/sub/301",
            IsActive = true
        };
        dbContext.PushSubscriptions.Add(sub);
        await dbContext.SaveChangesAsync(Ct);

        var service = new VapidPushService(
            httpClient,
            dbContext,
            Options.Create(new VapidOptions()),
            SystemClock.Instance,
            NullLogger<VapidPushService>.Instance);

        var payload = new WebPushNotificationPayload("Title", "Body");
        var result = await service.SendNotificationAsync(sub, payload, Ct);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(410);
        result.IsExpired.Should().BeTrue();

        var reloaded = await dbContext.PushSubscriptions.FindAsync([sub.Id], Ct);
        reloaded!.IsActive.Should().BeFalse();
        reloaded.ReasonForChange.Should().Contain("expired or revoked");
    }

    [Fact]
    public async Task EcoaWindowSchedulerService_RunsCycleAndDispatchesToActiveSubscriptions()
    {
        var dbOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new NotificationsDbContext(dbOptions);

        var activeSub = new PushSubscription
        {
            SubjectId = "SUBJ-ACTIVE",
            Endpoint = "https://push.example.com/sub/active",
            IsActive = true
        };
        var inactiveSub = new PushSubscription
        {
            SubjectId = "SUBJ-INACTIVE",
            Endpoint = "https://push.example.com/sub/inactive",
            IsActive = false
        };

        dbContext.PushSubscriptions.AddRange(activeSub, inactiveSub);
        await dbContext.SaveChangesAsync(Ct);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IClock>(SystemClock.Instance);

        var mockPushService = new FakeVapidPushService();
        services.AddSingleton<IVapidPushService>(mockPushService);

        var provider = services.BuildServiceProvider();

        var scheduler = new EcoaWindowSchedulerService(
            provider,
            SystemClock.Instance,
            NullLogger<EcoaWindowSchedulerService>.Instance);

        int count = await scheduler.RunSchedulerCycleAsync(Ct);

        count.Should().Be(1);
        mockPushService.LastPayloadDispatched.Should().NotBeNull();
        mockPushService.LastPayloadDispatched!.Title.Should().Be("Clinical Questionnaire Ready");
        mockPushService.LastPayloadDispatched.Body.Should().Contain("questionnaire ready");
    }

    [Fact]
    public async Task ApiEndpoints_VapidPublicKeyAndSubscriptions_WorkEndToEnd()
    {
        await using var factory = new PushNotificationsFactory();
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("test-user", "Subject"));

        // 1. GET /vapid-public-key
        var keyResponse = await client.GetAsync("/api/v1/notifications/vapid-public-key", Ct);
        keyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var keyObj = await keyResponse.Content.ReadFromJsonAsync<VapidPublicKeyResponse>(cancellationToken: Ct);
        keyObj.Should().NotBeNull();
        keyObj!.PublicKey.Should().NotBeNullOrEmpty();

        // 2. POST /subscriptions (Register)
        var regRequest = new PushSubscriptionRequest(
            SubjectId: "SUBJ-PORTAL-1",
            Endpoint: "https://push.example.com/sub/portal1",
            P256dh: "key-p256dh",
            Auth: "auth-token",
            UserAgent: "Mozilla/5.0 PortalApp"
        );

        var regResponse = await client.PostAsJsonAsync("/api/v1/notifications/subscriptions", regRequest, Ct);
        regResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdSub = await regResponse.Content.ReadFromJsonAsync<PushSubscription>(cancellationToken: Ct);
        createdSub.Should().NotBeNull();
        createdSub!.SubjectId.Should().Be("SUBJ-PORTAL-1");
        createdSub.IsActive.Should().BeTrue();

        // 3. POST /trigger-ecoa-job
        var triggerResponse = await client.PostAsync("/api/v1/notifications/trigger-ecoa-job", null, Ct);
        triggerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var jobResult = await triggerResponse.Content.ReadFromJsonAsync<EcoaJobResponse>(cancellationToken: Ct);
        jobResult.Should().NotBeNull();
        jobResult!.DispatchedCount.Should().BeGreaterThanOrEqualTo(0);

        // 4. DELETE /subscriptions
        var delResponse = await client.DeleteAsync($"/api/v1/notifications/subscriptions?endpoint={Uri.EscapeDataString(regRequest.Endpoint)}", Ct);
        delResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegisterPushSubscription_BadRequest_WhenMissingRequiredFields()
    {
        await using var factory = new PushNotificationsFactory();
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("test-user", "Subject"));

        var invalidRequest = new PushSubscriptionRequest("", "", "", "");
        var response = await client.PostAsJsonAsync("/api/v1/notifications/subscriptions", invalidRequest, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class PushNotificationsFactory : WebApplicationFactory<Cadence.Apps.Notifications.Program>
    {
        private readonly string _dbName = $"push-notif-test-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();

            builder.ConfigureServices(services =>
            {
                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<DbContextOptions<NotificationsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<NotificationsDbContext>>();
                services.AddDbContext<NotificationsDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(_dbName);
                    options.UseInternalServiceProvider(inMemoryServices);
                });
            });
        }
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class FakeVapidPushService : IVapidPushService
    {
        public WebPushNotificationPayload? LastPayloadDispatched { get; private set; }

        public string GetPublicKey() => "fake-vapid-public-key";

        public Task<PushDispatchResult> SendNotificationAsync(
            PushSubscription subscription,
            WebPushNotificationPayload payload,
            CancellationToken cancellationToken = default)
        {
            LastPayloadDispatched = payload;
            return Task.FromResult(new PushDispatchResult(true, 201));
        }
    }
}
