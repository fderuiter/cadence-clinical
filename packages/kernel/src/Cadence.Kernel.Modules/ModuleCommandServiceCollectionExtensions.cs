using System;
using System.Linq;

using Cadence.Kernel.Commands;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
}
