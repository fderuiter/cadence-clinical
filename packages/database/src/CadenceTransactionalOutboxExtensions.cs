using System;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Database;

/// <summary>
/// Extension methods for configuring MassTransit transactional outbox persistence with PostgreSQL and EF Core.
/// </summary>
public static class CadenceTransactionalOutboxExtensions
{
    /// <summary>
    /// Configures MassTransit transactional outbox entity mappings (InboxState, OutboxState, OutboxMessage)
    /// within an EF Core DbContext model.
    /// </summary>
    /// <param name="modelBuilder">The EF Core ModelBuilder.</param>
    /// <returns>The configured ModelBuilder.</returns>
    public static ModelBuilder AddCadenceTransactionalOutboxEntities(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.AddTransactionalOutboxEntities();
        return modelBuilder;
    }

    /// <summary>
    /// Configures MassTransit Entity Framework Core Transactional Outbox for PostgreSQL persistence with Cadence outbox options.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type.</typeparam>
    /// <param name="configurator">The MassTransit bus registration configurator.</param>
    /// <param name="configure">Optional delegate to configure CadenceTransactionalOutboxOptions.</param>
    /// <returns>The updated IBusRegistrationConfigurator.</returns>
    public static IBusRegistrationConfigurator AddCadenceTransactionalOutbox<TDbContext>(
        this IBusRegistrationConfigurator configurator,
        Action<CadenceTransactionalOutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var options = new CadenceTransactionalOutboxOptions();
        configure?.Invoke(options);

        configurator.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();

            if (options.QueryDelay.HasValue)
            {
                o.QueryDelay = options.QueryDelay.Value;
            }

            if (options.QueryMessageLimit.HasValue)
            {
                o.QueryMessageLimit = options.QueryMessageLimit.Value;
            }

            if (options.QueryTimeout.HasValue)
            {
                o.QueryTimeout = options.QueryTimeout.Value;
            }

            if (options.DuplicateDetectionWindow.HasValue)
            {
                o.DuplicateDetectionWindow = options.DuplicateDetectionWindow.Value;
            }

            options.ConfigureOutbox?.Invoke(o);
        });

        return configurator;
    }

    /// <summary>
    /// Configures MassTransit Entity Framework Core Transactional Outbox for PostgreSQL persistence with direct configurator callback.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type.</typeparam>
    /// <param name="configurator">The MassTransit bus registration configurator.</param>
    /// <param name="configureOutbox">Direct configuration callback for MassTransit EF Core outbox.</param>
    /// <returns>The updated IBusRegistrationConfigurator.</returns>
    public static IBusRegistrationConfigurator AddCadenceTransactionalOutbox<TDbContext>(
        this IBusRegistrationConfigurator configurator,
        Action<IEntityFrameworkOutboxConfigurator> configureOutbox)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOutbox);

        configurator.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            configureOutbox(o);
        });

        return configurator;
    }

    /// <summary>
    /// Registers MassTransit with Cadence Transactional Outbox for PostgreSQL persistence for the specified DbContext.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional delegate to configure CadenceTransactionalOutboxOptions.</param>
    /// <param name="configureBus">Optional delegate to register consumers or bus endpoints.</param>
    /// <returns>The updated IServiceCollection.</returns>
    public static IServiceCollection AddCadenceTransactionalOutbox<TDbContext>(
        this IServiceCollection services,
        Action<CadenceTransactionalOutboxOptions>? configureOptions = null,
        Action<IBusRegistrationConfigurator>? configureBus = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMassTransit(x =>
        {
            x.AddCadenceTransactionalOutbox<TDbContext>(configureOptions);
            configureBus?.Invoke(x);
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });

        return services;
    }
}
