using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Intelligence;
using Cadence.Kernel.Application.Knowledge;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Time;

using FluentValidation;
using FluentValidation.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Commands;

/// <summary>
/// The kernel command pipeline (spec §5.1): pipeline-wide checks → resolve actor (K-TX-5) → the command's validators
/// (ADR-2600 §4.4) → authorize (K-AUTH-1, denials audited) → idempotency (K-TX-3) → handler decides on one aggregate (K-TX-1) → one transaction for state,
/// provenance, events, audit and outbox (K-TX-2).
/// @req:PRD-SYS-053
/// </summary>
public sealed class KernelCommandPipeline(
    IServiceProvider services,
    ICurrentActor currentActor,
    ConsumerActor consumerActor,
    IAuthorizer authorizer,
    IKernelUnitOfWorkFactory unitOfWorkFactory,
    IClock clock) : IKernelCommandSender
{
    /// <summary>Serialisation used for request hashes and stored results; stable across processes.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

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

        if ((consumerActor.Actor ?? currentActor.Actor) is not { } actor)
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

        if (Invalid(await invoker.ValidateAsync(services, command, cancellationToken).ConfigureAwait(false)) is { } fieldErrors)
        {
            return fieldErrors;
        }

        var execution = new CommandExecution(
            command,
            commandType.Name,
            actor,
            KernelTimestamps.ToStoredPrecision(clock.UtcNow),
            Guid.CreateVersion7(),
            metadata ?? new CommandMetadata(Guid.CreateVersion7()),
            ComputeRequestHash(command, commandType));

        await using var transaction = unitOfWorkFactory.Begin(execution);

        var decision = await authorizer.AuthorizeAsync(actor, command, cancellationToken).ConfigureAwait(false);
        if (!decision.Permitted)
        {
            var denial = decision with { Reason = decision.Reason ?? KernelErrors.AuthorizationDenied.Message };
            await transaction.RecordDenialAsync(denial, cancellationToken).ConfigureAwait(false);
            return KernelErrors.AuthorizationDenied.WithMessage(denial.Reason!) with
            {
                Explanation = denial.Explain(execution.CommandType),
            };
        }

        transaction.RecordPermit(decision);
        if (await ReplayAsync<TResult>(transaction, execution, cancellationToken).ConfigureAwait(false) is { } replay)
        {
            return replay;
        }

        var context = new CommandContext(actor, command.TenantId, execution.RecordedAt, transaction.Work);
        var result = await invoker.InvokeAsync(handler, command, context, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            // A concurrent command with the same key can commit between the replay check and the handler's reads, so
            // the handler sees the winner's state (for example AlreadyExists). The stored outcome wins (K-TX-3).
            return await ReplayAsync<TResult>(transaction, execution, cancellationToken).ConfigureAwait(false) ?? result;
        }

        var resultJson = JsonSerializer.Serialize(result.Value, JsonOptions);
        var outcome = await transaction.CommitAsync(resultJson, cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case CommitOutcome.Committed:
                return result;
            case CommitOutcome.ConcurrencyConflict:
                // The aggregate changed under us; if the change was a same-key command, its outcome is the answer.
                return await ReplayAsync<TResult>(transaction, execution, cancellationToken).ConfigureAwait(false)
                    ?? KernelErrors.ConcurrencyConflict;
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

    /// <summary>
    /// <see cref="KernelErrors.CommandInvalid"/> with the failures grouped by property, or null when there are none.
    /// </summary>
    private static KernelError? Invalid(IReadOnlyList<ValidationFailure> failures)
    {
        if (failures.Count == 0)
        {
            return null;
        }

        var errors = failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return KernelErrors.CommandInvalid.WithMessage(
            $"The command is not valid: {string.Join(", ", errors.Keys)}.") with
        { Errors = errors };
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


    private interface IHandlerInvoker<TResult>
    {
        object? Resolve(IServiceProvider services);

        Task<IReadOnlyList<ValidationFailure>> ValidateAsync(
            IServiceProvider services, ICommand<TResult> command, CancellationToken cancellationToken);

        Task<CommandResult<TResult>> InvokeAsync(
            object handler, ICommand<TResult> command, ICommandContext context, CancellationToken cancellationToken);
    }

    private sealed class HandlerInvoker<TCommand, TResult> : IHandlerInvoker<TResult>
        where TCommand : ICommand<TResult>
    {
        public object? Resolve(IServiceProvider services) =>
            services.GetService<ICommandHandler<TCommand, TResult>>();

        public async Task<IReadOnlyList<ValidationFailure>> ValidateAsync(
            IServiceProvider services, ICommand<TResult> command, CancellationToken cancellationToken)
        {
            List<ValidationFailure> failures = [];
            foreach (var validator in services.GetServices<IValidator<TCommand>>())
            {
                var result = await validator.ValidateAsync((TCommand)command, cancellationToken).ConfigureAwait(false);
                failures.AddRange(result.Errors.Where(e => e.Severity == Severity.Error));
            }

            return failures;
        }

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
    /// Registers <see cref="IKernelCommandSender"/> and the kernel's command handlers. The host must also register
    /// <see cref="ICurrentActor"/>, <see cref="IAuthorizer"/> and kernel persistence (which provides
    /// <see cref="IKernelUnitOfWorkFactory"/>, <see cref="Trust.IAttestationVerifier"/> and
    /// <see cref="Execution.IEnrollmentDirectory"/>).
    /// </summary>
    public static IServiceCollection AddKernelCommandPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKernelCommandSender, KernelCommandPipeline>();
        services.TryAddScoped<ConsumerActor>();
        services.AddTransient<ICommandHandler<ProvisionTenant, TenantId>, ProvisionTenantHandler>();
        services.AddTransient<ICommandHandler<RegisterOrganization, OrganizationId>, RegisterOrganizationHandler>();
        services.AddKernelKnowledgeHandlers();
        services.AddKernelProtocolHandlers();
        services.AddKernelTrustHandlers();
        services.AddKernelExecutionHandlers();
        services.AddKernelStudyHandlers();
        services.AddKernelEvidenceHandlers();
        services.AddKernelObservationHandlers();
        services.AddKernelAssertionHandlers();
        services.AddKernelCollectionHandlers();
        services.AddKernelAuthorityHandlers();
        services.AddKernelWorkQueueHandlers();
        services.AddKernelEffectivityHandlers();
        services.AddKernelProposalHandlers();
        return services;
    }
}
