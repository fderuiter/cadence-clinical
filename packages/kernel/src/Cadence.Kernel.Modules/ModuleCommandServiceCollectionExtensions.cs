using System;
using System.Linq;

using Cadence.Kernel.Commands;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Cadence.Kernel.Modules;

/// <summary>Registers command handlers with the kernel command pipeline (ADR-2600).</summary>
public static class ModuleCommandServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the one handler of <typeparamref name="TCommand"/>. The host's
    /// <see cref="IKernelCommandSender"/> runs it through the same stages as a kernel command: actor, validation,
    /// authorization, idempotency, then one transaction for state, audit, events and outbox.
    /// </summary>
    /// <exception cref="InvalidOperationException">A handler for <typeparamref name="TCommand"/> is already registered.</exception>
    public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(ICommandHandler<TCommand, TResult>)))
        {
            throw new InvalidOperationException($"{typeof(TCommand).Name} already has a handler; a command has exactly one.");
        }

        services.TryAddTransient<ICommandHandler<TCommand, TResult>, THandler>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> as the store for the aggregates <paramref name="module"/>'s commands
    /// write (ADR-2600). The pipeline creates it per command on the command's connection with the options built here
    /// (Npgsql, no retrying execution strategy), enlists it in the command's transaction and audits the one changed
    /// aggregate on the kernel chain as <c>&lt;module&gt;.&lt;Type&gt;</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="module">The module's name, lower snake case (for example <c>execution</c>).</param>
    /// <param name="create">Creates the context from options, usually <c>options =&gt; new TContext(options)</c>.</param>
    /// <param name="npgsql">Optional Npgsql settings, such as the migrations history table.</param>
    /// <exception cref="InvalidOperationException">The module or the context type already has a command store.</exception>
    public static IServiceCollection AddModuleCommandStore<TContext>(
        this IServiceCollection services,
        string module,
        Func<DbContextOptions<TContext>, TContext> create,
        Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);
        var store = new ModuleCommandStore<TContext>(module, create, npgsql);
        if (services.Any(d => d.ServiceType == typeof(IModuleCommandStore)
            && d.ImplementationInstance is IModuleCommandStore registered
            && (registered.Module == store.Module || registered is ModuleCommandStore<TContext>)))
        {
            throw new InvalidOperationException($"Module '{store.Module}' or {typeof(TContext).Name} already has a command store.");
        }

        services.AddSingleton<IModuleCommandStore>(store);
        return services;
    }
}
