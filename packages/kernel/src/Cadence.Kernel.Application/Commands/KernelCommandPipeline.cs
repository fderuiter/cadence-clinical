using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Commands;

/// <summary>
/// The kernel command pipeline (spec §5.1): validate → resolve actor (K-TX-5) → authorize (K-AUTH-1, denials
/// audited) → idempotency (K-TX-3) → handler decides on one aggregate (K-TX-1) → one transaction for state,
/// provenance, events, audit and outbox (K-TX-2).
/// @req:PRD-SYS-053
/// </summary>
public sealed class KernelCommandPipeline(
    IServiceProvider services,
    ICurrentActor currentActor,
    IAuthorizer authorizer,
    IKernelUnitOfWorkFactory unitOfWorkFactory,
    IClock clock) : IKernelCommandSender
{
    /// <summary>Serialisation used for request hashes and stored results; stable across processes.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public async Task<CommandResult<TResult>> SendAsync<TResult>(
        ICommand<TResult> command,
        CommandMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (Validate(command) is { } invalid)
        {
            return invalid;
        }

        if (currentActor.Actor is not { } actor)
        {
            return KernelErrors.ActorUnresolved;
        }

        var commandType = command.GetType();
        var invoker = (IHandlerInvoker<TResult>)Invokers.GetOrAdd(
            commandType,
            type => Activator.CreateInstance(typeof(HandlerInvoker<,>).MakeGenericType(type, typeof(TResult)))!);
        if (invoker.Resolve(services) is not { } handler)
        {
            return KernelErrors.HandlerMissing.WithMessage($"No handler is registered for {commandType.Name}.");
        }

        var execution = new CommandExecution(
            command,
            commandType.Name,
            actor,
            TruncateToMicroseconds(clock.UtcNow),
            Guid.CreateVersion7(),
            metadata ?? new CommandMetadata(Guid.CreateVersion7()),
            ComputeRequestHash(command, commandType));

        await using var transaction = unitOfWorkFactory.Begin(execution);

        var decision = await authorizer.AuthorizeAsync(actor, command, cancellationToken).ConfigureAwait(false);
        if (!decision.Permitted)
        {
            var reason = decision.Reason ?? KernelErrors.AuthorizationDenied.Message;
            await transaction.RecordDenialAsync(reason, cancellationToken).ConfigureAwait(false);
            return KernelErrors.AuthorizationDenied.WithMessage(reason);
        }

        if (await ReplayAsync<TResult>(transaction, execution, cancellationToken).ConfigureAwait(false) is { } replay)
        {
            return replay;
        }

        var context = new CommandContext(actor, command.TenantId, execution.RecordedAt, transaction.Work);
        var result = await invoker.InvokeAsync(handler, command, context, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        var resultJson = JsonSerializer.Serialize(result.Value, JsonOptions);
        var outcome = await transaction.CommitAsync(resultJson, cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case CommitOutcome.Committed:
                return result;
            case CommitOutcome.ConcurrencyConflict:
                return KernelErrors.ConcurrencyConflict;
            case CommitOutcome.IdempotencyRace:
            case CommitOutcome.UniqueConflict:
                // A concurrent command with the same key may have committed first and tripped a unique constraint on
                // the aggregate before the idempotency key: its stored outcome wins (K-TX-3).
                return await ReplayAsync<TResult>(transaction, execution, cancellationToken).ConfigureAwait(false)
                    ?? (outcome == CommitOutcome.UniqueConflict
                        ? KernelErrors.AlreadyExists
                        : throw new InvalidOperationException("An idempotency race was reported but no prior outcome exists."));
            default:
                throw new InvalidOperationException($"Unknown commit outcome {outcome}.");
        }
    }

    private static KernelError? Validate(ICommand command)
    {
        if (command.TenantId.IsEmpty)
        {
            return KernelErrors.TenantRequired;
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return KernelErrors.IdempotencyKeyRequired;
        }

        return string.IsNullOrWhiteSpace(command.ReasonForChange) ? KernelErrors.ReasonForChangeRequired : null;
    }

    private static async Task<CommandResult<TResult>?> ReplayAsync<TResult>(
        IKernelCommandTransaction transaction, CommandExecution execution, CancellationToken cancellationToken)
    {
        if (await transaction.FindPriorOutcomeAsync(cancellationToken).ConfigureAwait(false) is not { } prior)
        {
            return null;
        }

        if (!string.Equals(prior.RequestHash, execution.RequestHash, StringComparison.Ordinal))
        {
            return KernelErrors.IdempotencyConflict;
        }

        return CommandResult<TResult>.Replay(JsonSerializer.Deserialize<TResult>(prior.ResultJson, JsonOptions)!);
    }

    /// <summary>
    /// SHA-256 over the command type and its JSON payload, so the same key with a different payload is detectable.
    /// </summary>
    internal static string ComputeRequestHash(ICommand command, Type commandType)
    {
        var payload = commandType.FullName + "\n" + JsonSerializer.Serialize(command, commandType, JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>PostgreSQL stores microseconds; truncating first keeps hashes stable across a database round-trip.</summary>
    internal static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    private interface IHandlerInvoker<TResult>
    {
        object? Resolve(IServiceProvider services);

        Task<CommandResult<TResult>> InvokeAsync(
            object handler, ICommand<TResult> command, ICommandContext context, CancellationToken cancellationToken);
    }

    private sealed class HandlerInvoker<TCommand, TResult> : IHandlerInvoker<TResult>
        where TCommand : ICommand<TResult>
    {
        public object? Resolve(IServiceProvider services) =>
            services.GetService<ICommandHandler<TCommand, TResult>>();

        public Task<CommandResult<TResult>> InvokeAsync(
            object handler, ICommand<TResult> command, ICommandContext context, CancellationToken cancellationToken) =>
            ((ICommandHandler<TCommand, TResult>)handler).HandleAsync((TCommand)command, context, cancellationToken);
    }

    private sealed record CommandContext(Actor Actor, TenantId TenantId, DateTimeOffset RecordedAt, IKernelUnitOfWork Work)
        : ICommandContext;
}

/// <summary>Registers the kernel command pipeline.</summary>
public static class KernelApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKernelCommandSender"/>. The host must also register <see cref="ICurrentActor"/>,
    /// <see cref="IAuthorizer"/>, kernel persistence (which provides <see cref="IKernelUnitOfWorkFactory"/>) and
    /// the command handlers.
    /// </summary>
    public static IServiceCollection AddKernelCommandPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKernelCommandSender, KernelCommandPipeline>();
        services.AddTransient<ICommandHandler<ProvisionTenant, TenantId>, ProvisionTenantHandler>();
        services.AddTransient<ICommandHandler<RegisterOrganization, OrganizationId>, RegisterOrganizationHandler>();
        return services;
    }
}
