using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Creates a module context for one tenant, for the module's reads outside a command (K-ID-2, ADR-2600). Every
/// connection it opens has the tenant set before any statement runs and cleared before it returns to the pool, as the
/// kernel's contexts do, so the module's Row-Level Security policies (<see cref="ModuleTenantIsolationSql"/>) show the
/// tenant's rows only. Audited records it loads still cannot be saved: a <see cref="ModuleDbContext"/> refuses that
/// outside the command pipeline.
/// </summary>
/// <typeparam name="TContext">The module's context.</typeparam>
public interface IModuleTenantContextFactory<out TContext>
    where TContext : ModuleDbContext
{
    /// <summary>A new context scoped to <paramref name="tenant"/>; the caller disposes it.</summary>
    TContext Create(TenantId tenant);
}

/// <summary>Registers a module's tenant-scoped read contexts (ADR-2600).</summary>
public static class ModuleTenantContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IModuleTenantContextFactory{TContext}"/> for <typeparamref name="TContext"/> on
    /// <paramref name="connectionString"/>, the host's application-role connection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The connection string the host's kernel persistence uses.</param>
    /// <param name="create">Creates the context from options, usually <c>options =&gt; new TContext(options)</c>.</param>
    /// <exception cref="InvalidOperationException">A factory for <typeparamref name="TContext"/> is already registered.</exception>
    public static IServiceCollection AddModuleTenantContext<TContext>(
        this IServiceCollection services,
        string connectionString,
        Func<DbContextOptions<TContext>, TContext> create)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(create);
        if (services.Any(d => d.ServiceType == typeof(IModuleTenantContextFactory<TContext>)))
        {
            throw new InvalidOperationException($"{typeof(TContext).Name} already has a tenant context factory.");
        }

        services.AddSingleton<IModuleTenantContextFactory<TContext>>(new ModuleTenantContextFactory<TContext>(connectionString, create));
        return services;
    }
}

internal sealed class ModuleTenantContextFactory<TContext>(
    string connectionString,
    Func<DbContextOptions<TContext>, TContext> create) : IModuleTenantContextFactory<TContext>
    where TContext : ModuleDbContext
{
    public TContext Create(TenantId tenant)
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(new ModuleTenantConnectionInterceptor(tenant))
            .Options;
        return create(options);
    }
}

/// <summary>Sets one tenant on every connection a module read context opens, and clears it on close (K-ID-2).</summary>
internal sealed class ModuleTenantConnectionInterceptor(TenantId tenant) : DbConnectionInterceptor
{
    private const string SetTenantSql = $"SELECT pg_catalog.set_config('{ModuleTenantIsolationSql.TenantSetting}', @tenant, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = SetTenantCommand(connection, tenant.Value.ToString());
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = SetTenantCommand(connection, tenant.Value.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            using var command = SetTenantCommand(connection, string.Empty);
            command.ExecuteNonQuery();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            await using var command = SetTenantCommand(connection, string.Empty);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return result;
    }

    private static DbCommand SetTenantCommand(DbConnection connection, string value)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetTenantSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return command;
    }
}
