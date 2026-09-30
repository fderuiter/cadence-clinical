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
    ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken);
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
    /// The command catalogue for <paramref name="resource"/>: every kernel command offered on objects of its kind,
    /// each with the current actor's dry-run decision and its explanation.
    /// </summary>
    ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(
        TenantId tenant, NodeRef resource, CancellationToken cancellationToken);
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
