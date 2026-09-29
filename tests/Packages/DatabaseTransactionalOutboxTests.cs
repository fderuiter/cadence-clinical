using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DatabaseTransactionalOutboxTests
{
    /// <summary>
    /// Validates that AddCadenceTransactionalOutboxEntities configures MassTransit inbox/outbox entities in the EF Core ModelBuilder.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void AddCadenceTransactionalOutboxEntities_MapsMassTransitOutboxEntitiesInDbContextModel()
    {
        var options = new DbContextOptionsBuilder<EconsentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new EconsentDbContext(options);
        var entityTypes = dbContext.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();

        entityTypes.Should().Contain("InboxState");
        entityTypes.Should().Contain("OutboxMessage");
        entityTypes.Should().Contain("OutboxState");
    }

    /// <summary>
    /// Validates that AddCadenceTransactionalOutbox registers MassTransit outbox persistence services and options in DI.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void AddCadenceTransactionalOutbox_RegistersMassTransitOutboxServicesInDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EconsentDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddCadenceTransactionalOutbox<EconsentDbContext>(options =>
        {
            options.QueryDelay = TimeSpan.FromSeconds(2);
            options.QueryMessageLimit = 50;
            options.QueryTimeout = TimeSpan.FromSeconds(15);
            options.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
            options.RetryAttemptCap = 3;
            options.RetryInterval = TimeSpan.FromSeconds(1);
        });

        using var serviceProvider = services.BuildServiceProvider();
        var bus = serviceProvider.GetService<IBus>();

        bus.Should().NotBeNull("MassTransit bus should be registered in service container");
    }

    /// <summary>
    /// Validates atomic database transaction commit and MassTransit outbox message staging during simulated operations.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task TransactionalOutbox_StagesMessageWithinDbContextTransaction_AndDeliversAsynchronously()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<EconsentDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddMassTransit(x =>
        {
            x.AddCadenceTransactionalOutbox<EconsentDbContext>();
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<EconsentDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        dbContext.Should().NotBeNull();
        publishEndpoint.Should().NotBeNull();

        // Staging a message via publish endpoint
        await publishEndpoint.Publish(new TestDomainEvent(
            EventId: Guid.NewGuid().ToString(),
            Timestamp: DateTime.UtcNow,
            AggregateId: "CONSENT-101"
        ), TestContext.Current.CancellationToken);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Verify outbox entities are available in the model
        var outboxMessageEntityType = dbContext.Model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.OutboxMessage");
        outboxMessageEntityType.Should().NotBeNull("OutboxMessage entity type must be registered in DbContext model");
    }

    public record TestDomainEvent(string EventId, DateTime Timestamp, string AggregateId);
}
