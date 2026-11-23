using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Intelligence;
using Cadence.Kernel.Application.Knowledge;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Tenancy;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

using FluentValidation;
using FluentValidation.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Commands;

/// <summary>
/// The kernel command pipeline (spec §5.1): pipeline-wide checks → resolve actor (K-TX-5) → the command's validators
/// (ADR-2600 §4.4) → authorize (K-AUTH-1, denials audited) → idempotency (K-TX-3) → the approved proposal the command
/// executes, when its metadata names one (K-AI-2) → handler decides on one aggregate (K-TX-1; a refusal it explains is
/// audited) → one transaction for state, provenance, events, audit and outbox (K-TX-2).
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
            ComputeRequestHash(command, commandType, metadata?.Proposal));

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

        if (!BaselineKernelCommandAttribute.IsBaseline(commandType)
            && await CheckSubscriptionActiveAsync(transaction, execution, cancellationToken).ConfigureAwait(false) is { } expired)
        {
            return expired;
        }

        if (execution.Metadata.Proposal is { } proposalId
            && await ExecuteUnderProposalAsync(transaction, execution, proposalId, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var context = new CommandContext(actor, command.TenantId, execution.RecordedAt, transaction.Work);
        var result = await invoker.InvokeAsync(handler, command, context, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            // A concurrent command with the same key can commit between the replay check and the handler's reads, so
            // the handler sees the winner's state (for example AlreadyExists). The stored outcome wins (K-TX-3).
            if (await ReplayAsync<TResult>(transaction, execution, cancellationToken).ConfigureAwait(false) is { } winner)
            {
                return winner;
            }

            // A refusal the handler explains was decided by a condition, as a denial is by authorization: it is audited.
            if (result.Error is { Explanation: not null } refusal)
            {
                await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            }

            return result;
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
    /// SHA-256 over the command type, its JSON payload and the proposal it executes, so the same key with a different
    /// payload or under a different proposal is detectable.
    /// </summary>
    internal static string ComputeRequestHash(ICommand command, Type commandType, ProposalId? proposal = null)
    {
        string serializedPayload;
        if (command is ImportProtocolDraft { RequestFingerprint: { Length: > 0 } } importDraft)
        {
            serializedPayload = JsonSerializer.Serialize(
                new
                {
                    importDraft.TenantId,
                    importDraft.StudyId,
                    importDraft.RequestFingerprint,
                    importDraft.IdempotencyKey,
                    importDraft.ReasonForChange,
                },
                JsonOptions);
        }
        else
        {
            serializedPayload = JsonSerializer.Serialize(command, commandType, JsonOptions);
        }

        var payload = commandType.FullName + "\n" + serializedPayload;
        if (proposal is { } executed)
        {
            payload += "\nproposal:" + executed;
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private async Task<KernelError?> CheckSubscriptionActiveAsync(
        IKernelCommandTransaction transaction, CommandExecution execution, CancellationToken cancellationToken)
    {
        if (services.GetService<ITenantEntitlementReader>() is not { } reader)
        {
            return null;
        }

        var snapshot = await reader
            .GetEntitlementsAsync(execution.Command.TenantId, execution.RecordedAt, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot.IsReadOnly)
        {
            var condition = EvaluatedCondition.Unsatisfied(
                "subscription.active",
                "Activate or renew the workspace subscription to record new study mutations; regulated reads, exports, closeout signatures, and audit trails remain available.");
            var refusal = KernelErrors.SubscriptionExpired with
            {
                Explanation = new Explanation(
                    NodeRef.For(execution.Command.TenantId),
                    $"{execution.CommandType} is refused because the workspace trial subscription has expired.",
                    "kernel.subscription",
                    [new ExplanationStep(KernelErrors.SubscriptionExpired.Message, [])],
                    [condition],
                    [new NextAction(condition.Description)]),
            };
            await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            return refusal;
        }

        if (ResolveRequiredCommandFamilies(execution.Command) is { } requiredFamilies
            && !requiredFamilies.Any(required => snapshot.ProductFamilies.Contains(required)))
        {
            string familyList = string.Join(", ", requiredFamilies.Select(static f => f.ToString()));
            var condition = EvaluatedCondition.Unsatisfied(
                "subscription.product_family",
                $"Enable one of the required product families ({familyList}) on the workspace subscription.");
            var refusal = CommercialErrors.ProductFamilyNotEntitled
                .WithMessage($"{CommercialErrors.ProductFamilyNotEntitled.Message} Required product family: {familyList}.") with
            {
                Explanation = new Explanation(
                    NodeRef.For(execution.Command.TenantId),
                    $"{execution.CommandType} is refused because the workspace is not entitled to {familyList}.",
                    "kernel.subscription",
                    [new ExplanationStep(CommercialErrors.ProductFamilyNotEntitled.Message, [])],
                    [condition],
                    [new NextAction(condition.Description)]),
            };
            await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            return refusal;
        }

        if (snapshot.Scoping is { } scoping
            && await CheckSubscriptionScopingCapsAsync(execution.Command, execution.RecordedAt, scoping, cancellationToken).ConfigureAwait(false) is { } capDetail)
        {
            var condition = EvaluatedCondition.Unsatisfied("subscription.scoping", capDetail);
            var refusal = CommercialErrors.ScopingCapExceeded.WithMessage(capDetail) with
            {
                Explanation = new Explanation(
                    NodeRef.For(execution.Command.TenantId),
                    $"{execution.CommandType} is refused because it exceeds the workspace subscription's contracted scoping parameters.",
                    "kernel.subscription",
                    [new ExplanationStep(capDetail, [])],
                    [condition],
                    [new NextAction("Update the workspace subscription against a new commercial quote covering the expanded study scope.")]),
            };
            await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            return refusal;
        }

        return null;
    }

    private static readonly ProductFamily[] BlueprintCommandFamilies = [ProductFamily.Blueprint];
    private static readonly ProductFamily[] CaptureCommandFamilies = [ProductFamily.Capture];
    private static readonly ProductFamily[] StudySetupCommandFamilies = [ProductFamily.Blueprint, ProductFamily.Capture, ProductFamily.Orbit];
    private static readonly ProductFamily[] AnyEntitledCommandFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Assure,
    ];

    private static IReadOnlyList<ProductFamily>? ResolveRequiredCommandFamilies(ICommand command)
    {
        if (command is SwitchTrialTier or GrantProductEntitlement or RevokeProductEntitlement)
        {
            return null;
        }

        if (command is CreateProtocolDraft
            or ReviseProtocolDraft
            or SubmitProtocolVersionForReview
            or ReturnProtocolVersionToDraft
            or ApproveProtocolVersion
            or WithdrawProtocolVersion
            or ForkProtocolVersion
            or ImportProtocolDraft
            or RegisterCodeSystem
            or ImportCodeSystemVersion
            or DefineConcept
            or ReviseConcept
            or DeprecateConcept
            or MapConcept
            or ReviewConceptMapping)
        {
            return BlueprintCommandFamilies;
        }

        if (command is RegisterParticipant
            or ScreenParticipant
            or ChangeEnrollmentStatus
            or WithdrawParticipant
            or OpenCollectionInstance
            or RecordCollectedValues
            or RecordUnaskedCollectionItem
            or RecordObservation
            or CorrectObservation
            or VerifyObservation
            or RaiseQuery
            or AnswerQuery
            or RetractAssertion
            or RecordPersonIdentity
            or RecordSiteLocalIdentity
            or LinkParticipantToPerson)
        {
            return CaptureCommandFamilies;
        }

        if (command is RegisterStudy
            or RegisterSite
            or LinkSiteToStudy
            or DeclareEffectivity
            or ChangeEffectivityStatus
            or ActivateStudySite)
        {
            return StudySetupCommandFamilies;
        }

        string ns = command.GetType().Namespace ?? string.Empty;
        if (ns.StartsWith("Cadence.Kernel.Standards", StringComparison.Ordinal))
        {
            return BlueprintCommandFamilies;
        }

        if (ns.StartsWith("Cadence.Apps.Execution", StringComparison.Ordinal))
        {
            return CaptureCommandFamilies;
        }

        return AnyEntitledCommandFamilies;
    }

    private async Task<string?> CheckSubscriptionScopingCapsAsync(
        ICommand command,
        DateTimeOffset recordedAt,
        SubscriptionScoping scoping,
        CancellationToken cancellationToken)
    {
        var usageReader = services.GetService<ISubscriptionScopingUsageReader>();

        switch (command)
        {
            case RegisterStudy registerStudy:
                {
                    string trimmedPhase = registerStudy.Phase?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(trimmedPhase)
                        && !string.Equals(trimmedPhase, scoping.StudyPhase, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Study phase '{trimmedPhase}' does not match the workspace subscription's contracted phase ('{scoping.StudyPhase}').";
                    }

                    if (usageReader is not null)
                    {
                        int currentStudies = await usageReader
                            .CountStudiesAsync(registerStudy.TenantId, registerStudy.StudyId, cancellationToken)
                            .ConfigureAwait(false);
                        if (currentStudies >= scoping.ActiveStudyCount)
                        {
                            return $"The workspace subscription allows at most {scoping.ActiveStudyCount} active study(ies), and {currentStudies} already exist.";
                        }
                    }

                    return null;
                }

            case RegisterSite registerSite:
                {
                    string trimmedCountry = registerSite.Country?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(trimmedCountry)
                        && !scoping.Countries.Any(c => string.Equals(c, trimmedCountry, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"Country '{trimmedCountry}' is outside the workspace subscription's contracted countries ({string.Join(", ", scoping.Countries)}).";
                    }

                    if (usageReader is not null)
                    {
                        int currentSites = await usageReader
                            .CountSitesAsync(registerSite.TenantId, registerSite.SiteId, cancellationToken)
                            .ConfigureAwait(false);
                        if (currentSites >= scoping.SiteCount)
                        {
                            return $"The workspace subscription allows at most {scoping.SiteCount} site(s), and {currentSites} already exist.";
                        }
                    }

                    return null;
                }

            case DeclareEffectivity declareEffectivity
                when (declareEffectivity.DeclaredAt - recordedAt).TotalDays > scoping.StudyDurationMonths * 31.0:
                {
                    return $"Effectivity horizon exceeds the workspace subscription's contracted study duration ({scoping.StudyDurationMonths} months).";
                }

            case ChangeEffectivityStatus changeEffectivity
                when (changeEffectivity.EffectiveAt - recordedAt).TotalDays > scoping.StudyDurationMonths * 31.0:
                {
                    return $"Effectivity horizon exceeds the workspace subscription's contracted study duration ({scoping.StudyDurationMonths} months).";
                }

            case RegisterParticipant registerParticipant when usageReader is not null:
                {
                    int currentParticipants = await usageReader
                        .CountParticipantsAsync(registerParticipant.TenantId, registerParticipant.ParticipantId, cancellationToken)
                        .ConfigureAwait(false);
                    if (currentParticipants >= scoping.ParticipantCount)
                    {
                        return $"The workspace subscription allows at most {scoping.ParticipantCount} participant(s), and {currentParticipants} already exist.";
                    }

                    return null;
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// The execution link (K-AI-2, ADR-2632): the command runs under an approved proposal only when the proposal exists,
    /// is approved and unexpired, was approved by the acting person, requested the command's capability, and the command
    /// acts on an object of the proposal's study or of no study. The command has already been authorized as the acting
    /// person, so the proposal never widens what anyone may do. On success the commit records the proposal on the
    /// provenance activity and in lineage, and a <see cref="ProposalExecuted"/> event names the agent and model behind it.
    /// </summary>
    private async Task<KernelError?> ExecuteUnderProposalAsync(
        IKernelCommandTransaction transaction, CommandExecution execution, ProposalId proposalId, CancellationToken cancellationToken)
    {
        var catalog = services.GetService<KernelCommandCatalog>() ?? KernelCommandCatalog.Kernel;
        if (catalog.Find(execution.Command.GetType()) is not { } entry || entry.ResourceOf(execution.Command) is not { } resource)
        {
            return ProposalErrors.CapabilityNotRequested;
        }

        if (await transaction.Work.ReadAsync<Proposal>(proposalId, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return ProposalErrors.ExecutionUnknown;
        }

        if (proposal.CheckExecution(execution.Actor, entry.Capability.Token, execution.RecordedAt) is { } refused)
        {
            return refused;
        }

        // Fail closed: without the scope graph the command's study cannot be checked.
        if (services.GetService<IAuthorityStore>() is not { } store)
        {
            return ProposalErrors.OutsideStudy.WithMessage("The command's study cannot be checked against the proposal's.");
        }

        var path = await store.PathOfAsync(execution.Command.TenantId, resource, cancellationToken).ConfigureAwait(false);
        if (path.Study is { } study && study != proposal.StudyId)
        {
            return ProposalErrors.OutsideStudy;
        }

        transaction.RecordProposalExecution(NodeRef.For(proposal.Id, proposal.VersionIndex));
        transaction.Work.Raise(new ProposalExecuted(
            proposal.TenantId,
            proposal.Id,
            proposal.StudyId,
            execution.CommandType,
            entry.Capability.Token,
            resource.ToString(),
            execution.Actor.Id,
            proposal.ApprovalAttestationId!.Value,
            proposal.ProposedById,
            proposal.ProposedByKind,
            proposal.ModelId,
            proposal.RequestedById));
        return null;
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
    /// <see cref="IKernelUnitOfWorkFactory"/>, <see cref="IAttestationVerifier"/> and
    /// <see cref="Execution.IEnrollmentDirectory"/>).
    /// </summary>
    public static IServiceCollection AddKernelCommandPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKernelCommandSender, KernelCommandPipeline>();
        services.TryAddScoped<ConsumerActor>();
        services.AddTransient<ICommandHandler<ProvisionTenant, TenantId>, ProvisionTenantHandler>();
        services.AddTransient<ICommandHandler<RegisterOrganization, OrganizationId>, RegisterOrganizationHandler>();
        services.AddTransient<ICommandHandler<RegisterPerson, PersonId>, RegisterPersonHandler>();
        services.AddTransient<ICommandHandler<RegisterTenantMembership, TenantMembershipId>, RegisterTenantMembershipHandler>();
        services.AddKernelCommercialHandlers();
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
