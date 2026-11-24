using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// The one path to an authorization decision (ADR-2483). The command pipeline reaches it as <see cref="IAuthorizer"/>
/// and the Canvas reaches it as <see cref="IAuthorizationProbe"/>; both build an <see cref="AuthorizationRequest"/>
/// and ask the same <see cref="IAuthorizationEvaluator"/>, so a dry run and a real command cannot be decided
/// differently. Real decisions are audited by the pipeline (K-AUTH-2); dry runs go to the authorization access log.
/// </summary>
public sealed partial class KernelAuthorization(
    IAuthorizationEvaluator evaluator,
    ICurrentActor currentActor,
    KernelCommandCatalog catalog,
    IClock clock,
    ILogger<KernelAuthorization> logger,
    IAuthorityStore? store = null) : IAuthorizer, IAuthorizationProbe
{
    /// <summary>Condition code reported when a command type declares no capability.</summary>
    public const string CapabilityDeclaredCondition = "capability.declared";

    /// <summary>Condition code reported when a command leaves its resource empty.</summary>
    public const string ResourceNamedCondition = "resource.named";

    /// <summary>Condition code reported when a dry run has no accountable actor (K-TX-5).</summary>
    public const string ActorResolvedCondition = "actor.resolved";

    /// <summary>Condition code reported when a workspace administrator submits a quote for cryptographic verification by the handler.</summary>
    public const string QuoteVerificationChannelCondition = "commercial.quote_channel";

    public async ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);
        var commandName = command.GetType().Name;
        if (catalog.Find(command.GetType()) is not { } entry)
        {
            // Fail closed: a command nobody declared a capability for cannot be authorized.
            return AuthorizationDecision.Deny(
                $"{commandName} declares no capability.",
                EvaluatedCondition.Unsatisfied(CapabilityDeclaredCondition, $"{commandName} must declare the capability it needs"));
        }

        var resolvedResource = command is Attest attest
            ? ResolveAttestResource(attest)
            : entry.ResourceOf(command);
        if (resolvedResource is not { } resource)
        {
            return AuthorizationDecision.Deny(
                $"{commandName} names no {entry.ResourceKind}.",
                EvaluatedCondition.Unsatisfied(ResourceNamedCondition, $"Name the {entry.ResourceKind} to act on"));
        }

        var decision = await DecideAsync(actor, command.TenantId, entry.Capability, resource, cancellationToken).ConfigureAwait(false);
        if (!decision.Permitted && command is ActivateSubscription)
        {
            var switchDecision = await DecideAsync(
                actor,
                command.TenantId,
                new Capability("subscription.switch_tier", VerbClass.Execute),
                resource,
                cancellationToken).ConfigureAwait(false);
            if (switchDecision.Permitted)
            {
                return AuthorizationDecision.Permit(
                    "Workspace administrator may present a signed commercial quote for verification.",
                    [
                        .. switchDecision.Conditions,
                        EvaluatedCondition.Satisfied(
                            QuoteVerificationChannelCondition,
                            "Holds subscription.switch_tier; ActivateSubscriptionHandler verifies cryptographic quote token"),
                    ]);
            }
        }

        return decision;
    }

    private static NodeRef? ResolveAttestResource(Attest attest)
    {
        if (attest.Target is { } evidenceTarget
            && evidenceTarget.Id != Guid.Empty
            && evidenceTarget.Kind is NodeKinds.Observation or NodeKinds.ValidationFinding or NodeKinds.Assertion or NodeKinds.FormQuery)
        {
            return new NodeRef(evidenceTarget.Kind, evidenceTarget.Id);
        }

        if (attest.Scope is { } scope)
        {
            return scope.Unversioned();
        }

        if (attest.Target is { } target && NodeKinds.IsRegistered(target.Kind) && target.Id != Guid.Empty)
        {
            return new NodeRef(target.Kind, target.Id);
        }

        return !attest.TenantId.IsEmpty ? NodeRef.For(attest.TenantId) : null;
    }

    public async ValueTask<AuthorizationDecision> CanIAsync(
        TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resource);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("A dry run is asked within a tenant.", nameof(tenant));
        }

        AuthorizationDecision decision;
        if (currentActor.Actor is not { } actor)
        {
            decision = AuthorizationDecision.Deny(
                KernelErrors.ActorUnresolved.Message,
                EvaluatedCondition.Unsatisfied(ActorResolvedCondition, "Sign in as an accountable person or service account"));
            LogDryRun(logger, tenant, "unresolved", capability.Token, resource, false, ActorResolvedCondition);
            return decision;
        }

        decision = await DecideAsync(actor, tenant, capability, resource, cancellationToken).ConfigureAwait(false);
        LogDryRun(logger, tenant, actor.ToString(), capability.Token, resource, decision.Permitted,
            string.Join(',', decision.Conditions.Where(c => !c.Met).Select(c => c.Code)));
        return decision;
    }

    public async ValueTask<AuthorizationDecision> CanIWorkQueueCandidateAsync(
        TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resource);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("A dry run is asked within a tenant.", nameof(tenant));
        }

        if (currentActor.Actor is not { } actor || evaluator is not AuthorityEvaluator authorityEvaluator)
        {
            return await CanIAsync(tenant, capability, resource, cancellationToken).ConfigureAwait(false);
        }

        var now = KernelTimestamps.ToStoredPrecision(clock.UtcNow);
        var decision = await authorityEvaluator
            .EvaluateScopeCapabilityAsync(new AuthorizationRequest(actor, tenant, capability, resource, now, now), cancellationToken)
            .ConfigureAwait(false);
        if (!decision.Permitted && decision.Reason is null)
        {
            decision = decision with { Reason = KernelErrors.AuthorizationDenied.Message };
        }

        LogDryRun(logger, tenant, actor.ToString(), capability.Token, resource, decision.Permitted,
            string.Join(',', decision.Conditions.Where(c => !c.Met).Select(c => c.Code)));
        return decision;
    }

    public async ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(
        TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var entries = catalog.ForKind(resource.Kind);
        var commands = new List<CommandAvailability>(entries.Count);
        foreach (var entry in entries)
        {
            var decision = await CanIAsync(tenant, entry.Capability, resource, cancellationToken).ConfigureAwait(false);
            commands.Add(new CommandAvailability(entry.Command, entry.Capability, resource, decision, decision.Explain(entry.Command, resource)));
        }

        return commands;
    }

    public async ValueTask<AuthorizationDecision> DenyHiddenAsync(
        TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resource);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("A dry run is asked within a tenant.", nameof(tenant));
        }

        if (currentActor.Actor is not { } actor)
        {
            return await CanIAsync(tenant, capability, resource, cancellationToken).ConfigureAwait(false);
        }

        var decisions = await AuthorityEvaluator
            .DenyHiddenResourceCapabilitiesAsync(store, tenant, actor, [capability], resource, cancellationToken)
            .ConfigureAwait(false);
        var decision = decisions[0];
        LogDryRun(
            logger,
            tenant,
            actor.ToString(),
            capability.Token,
            resource,
            false,
            string.Join(',', decision.Conditions.Where(c => !c.Met).Select(c => c.Code)));
        return decision;
    }

    public async ValueTask<IReadOnlyList<CommandAvailability>> DenyHiddenCommandsAsync(
        TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (currentActor.Actor is not { } actor)
        {
            return await GetCommandsAsync(tenant, resource, cancellationToken).ConfigureAwait(false);
        }

        var entries = catalog.ForKind(resource.Kind);
        if (entries.Count == 0)
        {
            return [];
        }

        var capabilities = entries.Select(e => e.Capability).ToList();
        var decisions = await AuthorityEvaluator
            .DenyHiddenResourceCapabilitiesAsync(store, tenant, actor, capabilities, resource, cancellationToken)
            .ConfigureAwait(false);
        var commands = new List<CommandAvailability>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var decision = decisions[i];
            LogDryRun(
                logger,
                tenant,
                actor.ToString(),
                entry.Capability.Token,
                resource,
                false,
                string.Join(',', decision.Conditions.Where(c => !c.Met).Select(c => c.Code)));
            commands.Add(new CommandAvailability(entry.Command, entry.Capability, resource, decision, decision.Explain(entry.Command, resource)));
        }

        return commands;
    }

    private async ValueTask<AuthorizationDecision> DecideAsync(
        Actor actor, TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
    {
        var now = KernelTimestamps.ToStoredPrecision(clock.UtcNow);
        var decision = await evaluator
            .EvaluateAsync(new AuthorizationRequest(actor, tenant, capability, resource, now, now), cancellationToken)
            .ConfigureAwait(false);
        return decision.Permitted || decision.Reason is not null
            ? decision
            : decision with { Reason = KernelErrors.AuthorizationDenied.Message };
    }

    [LoggerMessage(EventId = 2483, EventName = "AuthorizationDryRun", Level = LogLevel.Information,
        Message = "Dry-run authorization in tenant {Tenant}: {Actor} asked {Capability} on {Resource}; permitted {Permitted}; unmet [{Unmet}]")]
    private static partial void LogDryRun(
        ILogger logger, TenantId tenant, string actor, string capability, NodeRef resource, bool permitted, string unmet);
}

/// <summary>Registers kernel authorization.</summary>
public static class KernelAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="KernelAuthorization"/> as the pipeline's <see cref="IAuthorizer"/> and as
    /// <see cref="IAuthorizationProbe"/>, over the command catalogue of the kernel and every module registered with
    /// <see cref="CadenceModuleServiceCollectionExtensions.AddCadenceModule"/> (ADR-2601). The host also registers the
    /// <see cref="IAuthorizationEvaluator"/> (the authority slice, #4781) and logging.
    /// </summary>
    public static IServiceCollection AddKernelAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(sp => sp.GetServices<CadenceModuleRegistration>().ToArray() is { Length: > 0 } modules
            ? new KernelCommandCatalog([typeof(ICommand).Assembly, .. modules.Select(m => m.CommandAssembly)])
            : KernelCommandCatalog.Kernel);
        services.AddScoped<KernelAuthorization>();
        services.AddScoped<IAuthorizer>(sp => sp.GetRequiredService<KernelAuthorization>());
        services.AddScoped<IAuthorizationProbe>(sp => sp.GetRequiredService<KernelAuthorization>());
        services.TryAddScoped<IKernelExplanationMasker, KernelExplanationMasker>();
        return services;
    }
}
