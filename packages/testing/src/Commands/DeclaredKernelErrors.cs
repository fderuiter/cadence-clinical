using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Testing.Commands;

/// <summary>
/// The test half of ADR-2600 §4.5: wraps the registered <see cref="IKernelCommandSender"/> so that a command carrying
/// <see cref="RaisesKernelErrorsAttribute"/> fails the test when it returns an error it does not declare (beyond
/// <see cref="KernelErrors.CommandPipeline"/>). Endpoints publish the declared set in OpenAPI, so an undeclared error is
/// a contract the clients were never told about. Commands without a declaration pass through unchecked.
/// </summary>
public static class DeclaredKernelErrors
{
    /// <summary>
    /// Wraps the last <see cref="IKernelCommandSender"/> registration (call it after <c>AddKernelCommandPipeline()</c>).
    /// </summary>
    public static IServiceCollection AddDeclaredKernelErrorsCheck(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IKernelCommandSender))
            ?? throw new InvalidOperationException("Register the kernel command sender before wrapping it.");
        services.Remove(inner);
        services.Add(new ServiceDescriptor(
            typeof(IKernelCommandSender),
            provider => new CheckingSender(Create(provider, inner)),
            inner.Lifetime));
        return services;
    }

    /// <summary>
    /// The error <paramref name="commandType"/> returned, when it declares its errors and this one is neither declared
    /// nor the pipeline's own (or <see cref="KernelErrors.HandlerMissing"/>, a wiring fault); null when it is allowed.
    /// </summary>
    public static string? Undeclared(Type commandType, KernelError error)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(error);
        if (RaisesKernelErrorsAttribute.Of(commandType) is not { } declared
            || error.Code == KernelErrors.HandlerMissing.Code
            || KernelErrors.CommandPipeline.Concat(declared).Any(e => e.Code == error.Code && e.HttpStatus == error.HttpStatus))
        {
            return null;
        }

        return $"{commandType.Name} returned {error.Code} ({error.HttpStatus}), which it does not declare with [RaisesKernelErrors].";
    }

    private static IKernelCommandSender Create(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance as IKernelCommandSender
        ?? (IKernelCommandSender?)descriptor.ImplementationFactory?.Invoke(provider)
        ?? (IKernelCommandSender)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

    private sealed class CheckingSender(IKernelCommandSender inner) : IKernelCommandSender
    {
        public async Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            var result = await inner.SendAsync(command, metadata, cancellationToken).ConfigureAwait(false);
            if (result.Error is { } error && Undeclared(command.GetType(), error) is { } problem)
            {
                throw new InvalidOperationException(problem);
            }

            return result;
        }
    }
}
