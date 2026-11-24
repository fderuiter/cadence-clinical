using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// One authorization question: may <see cref="Actor"/> exercise <see cref="Capability"/> on <see cref="Resource"/> in
/// <see cref="Tenant"/>, as of valid time <see cref="At"/> and knowledge time <see cref="KnownAt"/> (spec §9.3, ADR-2251).
/// It deliberately says nothing about whether the question comes from a command or a dry run, so the evaluator
/// cannot answer the two differently (ADR-2483).
/// </summary>
public sealed record AuthorizationRequest(
    Actor Actor,
    TenantId Tenant,
    Capability Capability,
    NodeRef Resource,
    DateTimeOffset At,
    DateTimeOffset KnownAt);

/// <summary>
/// The kernel's single authorization decision procedure (spec §9.3). The authority slice (#4781) implements it from
/// Grants, Delegations and Qualifications; it returns every condition it checked with its evidence (ADR-2482). Both the
/// command pipeline and <see cref="IAuthorizationProbe"/> call it through <see cref="KernelAuthorization"/>.
/// </summary>
public interface IAuthorizationEvaluator
{
    /// <summary>Evaluates <paramref name="request"/> and returns the decision with its checked conditions.</summary>
    ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates whether <paramref name="actor"/> may exercise <paramref name="capability"/> on each resource in
    /// <paramref name="resources"/> in <paramref name="tenant"/> as of <c>(at, knownAt)</c>, batching store lookups when
    /// supported by the implementation.
    /// </summary>
    async ValueTask<IReadOnlyDictionary<NodeRef, AuthorizationDecision>> EvaluateManyAsync(
        Actor actor,
        TenantId tenant,
        Capability capability,
        IReadOnlyCollection<NodeRef> resources,
        DateTimeOffset at,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resources);
        var decisions = new Dictionary<NodeRef, AuthorizationDecision>();
        foreach (var resource in resources)
        {
            if (!decisions.ContainsKey(resource))
            {
                decisions[resource] = await EvaluateAsync(
                    new AuthorizationRequest(actor, tenant, capability, resource, at, knownAt),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return decisions;
    }
}

/// <summary>
/// Dry-run authorization (Canvas D8, ADR-2483): "could I?" without executing. It answers only for the calling actor,
/// writes nothing to the audit chain, and is recorded in the authorization access log instead.
/// </summary>
public interface IAuthorizationProbe
{
    /// <summary>Whether the current actor may exercise <paramref name="capability"/> on <paramref name="resource"/> now.</summary>
    ValueTask<AuthorizationDecision> CanIAsync(
        TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the current actor may exercise <paramref name="capability"/> on each resource in <paramref name="resources"/>
    /// now, evaluated in one batch when supported by the probe.
    /// </summary>
    async ValueTask<IReadOnlyDictionary<NodeRef, AuthorizationDecision>> CanIManyAsync(
        TenantId tenant,
        Capability capability,
        IReadOnlyCollection<NodeRef> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resources);
        var decisions = new Dictionary<NodeRef, AuthorizationDecision>();
        foreach (var resource in resources)
        {
            if (!decisions.ContainsKey(resource))
            {
                decisions[resource] = await CanIAsync(tenant, capability, resource, cancellationToken).ConfigureAwait(false);
            }
        }

        return decisions;
    }

    /// <summary>
    /// The command catalogue for <paramref name="resource"/>: every kernel command offered on objects of its kind,
    /// each with the current actor's dry-run decision and its explanation.
    /// </summary>
    ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(
        TenantId tenant, NodeRef resource, CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates whether the current actor holds <paramref name="capability"/> over <paramref name="resource"/>'s scope
    /// before a capability-keyed work queue masks restricted or blinded items in place (ADR-2471, ADR-2486).
    /// </summary>
    ValueTask<AuthorizationDecision> CanIWorkQueueCandidateAsync(
        TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
        CanIAsync(tenant, capability, resource, cancellationToken);

    /// <summary>
    /// Records a non-confirming <see cref="AuthorityEvaluator.GrantCondition"/> dry-run denial when
    /// <paramref name="resource"/> is masked or undetected for the calling actor.
    /// </summary>
    ValueTask<AuthorizationDecision> DenyHiddenAsync(
        TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
        CanIAsync(tenant, capability, resource, cancellationToken);

    /// <summary>
    /// Returns the command catalogue for <paramref name="resource"/> with non-confirming
    /// <see cref="AuthorityEvaluator.GrantCondition"/> dry-run denials when the resource is masked or undetected for the calling actor.
    /// </summary>
    async ValueTask<IReadOnlyList<CommandAvailability>> DenyHiddenCommandsAsync(
        TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        var existing = await GetCommandsAsync(tenant, resource, cancellationToken).ConfigureAwait(false);
        var denied = new List<CommandAvailability>(existing.Count);
        foreach (var item in existing)
        {
            var decision = await DenyHiddenAsync(tenant, item.Capability, resource, cancellationToken).ConfigureAwait(false);
            denied.Add(new CommandAvailability(item.Command, item.Capability, resource, decision, decision.Explain(item.Command, resource)));
        }

        return denied;
    }
}

/// <summary>A command offered on an object, with whether the current actor may run it and why.</summary>
/// <param name="Command">The command type name, for example <c>ReviseConcept</c>.</param>
/// <param name="Capability">The capability it needs.</param>
/// <param name="Resource">The object it would be authorized against.</param>
/// <param name="Decision">The dry-run decision, identical to what sending the command would decide now.</param>
/// <param name="Explanation">The decision explained (ADR-2482).</param>
public sealed record CommandAvailability(
    string Command,
    Capability Capability,
    NodeRef Resource,
    AuthorizationDecision Decision,
    Explanation Explanation);
