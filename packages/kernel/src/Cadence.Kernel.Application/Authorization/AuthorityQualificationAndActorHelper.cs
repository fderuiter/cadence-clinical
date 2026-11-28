using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Authorization;

internal static partial class AuthorityQualificationAndActorHelper
{
    internal static readonly IReadOnlyDictionary<ProtocolVersionId, int> EmptyVersionNumbers =
        new Dictionary<ProtocolVersionId, int>();

    internal static async ValueTask<IReadOnlyList<AuthorizationDecision>> DenyHiddenResourceCapabilitiesAsync(
        IAuthorityStore? store,
        TenantId tenant,
        Actor actor,
        IReadOnlyList<Capability> capabilities,
        NodeRef resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(resource);
        if (capabilities.Count == 0)
        {
            return [];
        }

        if (actor.Kind == ActorKind.AutomationAgent && actor.RequestedBy is null)
        {
            var missingRequester = AuthorizationDecision.Deny(
                "An automation agent acts only for the person who asked it (K-AI-6).",
                EvaluatedCondition.Unsatisfied(AuthorityEvaluator.RequesterCondition, "Run the agent for the person it acts for"));
            return [.. capabilities.Select(_ => missingRequester)];
        }

        if (actor.Kind != ActorKind.AutomationAgent && actor.RequestedBy is not null)
        {
            var invalidRequester = AuthorizationDecision.Deny(
                "Only an automation agent acts for a requesting person.",
                EvaluatedCondition.Unsatisfied(AuthorityEvaluator.RequesterCondition, "Act as yourself"));
            return [.. capabilities.Select(_ => invalidRequester)];
        }

        var grantee = actor.AsIdentity();
        IReadOnlyList<Grant> held = store is null
            ? []
            : await store.GrantsHeldAsync(tenant, grantee, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<RoleId, Role> roles = store is null || held.Count == 0
            ? new Dictionary<RoleId, Role>()
            : await RoleCapabilitiesFromStoreAsync(store, tenant, held, cancellationToken).ConfigureAwait(false);

        var decisions = new List<AuthorizationDecision>(capabilities.Count);
        foreach (var capability in capabilities)
        {
            if (actor.Kind == ActorKind.AutomationAgent && capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
            {
                decisions.Add(AuthorizationDecision.Deny(
                    $"An automation agent cannot {capability.VerbClass.ToString().ToLowerInvariant()}; a person must.",
                    EvaluatedCondition.Unsatisfied(AuthorityEvaluator.AgentVerbCondition, "Have a person perform this action")));
                continue;
            }

            var carrying = held.Where(g => Carries(g, capability.Token, roles)).ToList();
            decisions.Add(DenyMissingGrant(grantee, capability.Token, resource, carrying));
        }

        return decisions;
    }

    internal static AuthorizationDecision DenyMissingGrant(
        Actor actor,
        string token,
        NodeRef resource,
        IReadOnlyList<Grant> carrying)
    {
        var actorLabel = DescribeActor(actor, "The signed-in user");
        var reason = carrying.Count == 0
            ? $"{actorLabel} holds no grant of {token}."
            : $"{actorLabel} holds no grant of {token} in effect for this {resource.Kind}.";
        NodeRef[] grantEvidence = ActorEvidence(actor) is { } actorNode
            ? [resource, actorNode, .. carrying.Select(g => NodeRef.For(g.Id))]
            : [resource, .. carrying.Select(g => NodeRef.For(g.Id))];
        return AuthorizationDecision.Deny(
            reason,
            EvaluatedCondition.Unsatisfied(
                AuthorityEvaluator.GrantCondition,
                $"Obtain a grant or delegation of {token} covering this {resource.Kind}",
                grantEvidence));
    }

    internal static async Task<IReadOnlyList<EvaluatedCondition>> QualificationConditionsAsync(
        IAuthorityStore store,
        AuthorizationRequest request,
        string capabilityToken,
        ScopePath path,
        CancellationToken cancellationToken)
    {
        var activeRequirements = (await store.QualificationRequirementsAsync(request.Tenant, capabilityToken, cancellationToken).ConfigureAwait(false))
            .Where(r => r.IsInEffect(request.At, request.KnownAt) && r.Scope.Covers(path))
            .OrderBy(r => r.Id)
            .ToList();
        if (activeRequirements.Count == 0)
        {
            return [];
        }

        var versionIds = activeRequirements
            .Where(r => r.ProtocolVersionId is not null)
            .Select(r => r.ProtocolVersionId!.Value)
            .Distinct()
            .ToList();
        IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers = versionIds.Count == 0
            ? EmptyVersionNumbers
            : await store.ProtocolVersionNumbersAsync(request.Tenant, versionIds, cancellationToken).ConfigureAwait(false);

        var held = await store.QualificationsHeldAsync(request.Tenant, request.Actor, cancellationToken).ConfigureAwait(false);
        return EvaluateQualificationConditions(activeRequirements, held, versionNumbers, path, request.At, request.KnownAt);
    }

    internal static IReadOnlyList<EvaluatedCondition> EvaluateQualificationConditions(
        IReadOnlyList<QualificationRequirement> activeRequirements,
        IReadOnlyList<Qualification> held,
        IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers,
        ScopePath path,
        DateTimeOffset at,
        DateTimeOffset knownAt)
    {
        var requirements = activeRequirements.Where(r => r.Scope.Covers(path)).ToList();
        if (requirements.Count == 0)
        {
            return [];
        }

        var conditions = new List<EvaluatedCondition>(requirements.Count);
        foreach (var requirement in requirements)
        {
            var matching = held.Where(requirement.IsMetBy).OrderBy(q => q.Id).ToList();
            var named = Named(requirement, versionNumbers);
            if (matching.FirstOrDefault(q => q.IsInEffect(at, knownAt)) is { } current)
            {
                conditions.Add(EvaluatedCondition.Satisfied(
                    AuthorityEvaluator.QualificationCondition,
                    $"Holds current {named}",
                    NodeRef.For(requirement.Id),
                    NodeRef.For(current.Id),
                    NodeRef.For(current.EvidenceArtifactVersionId)));
                continue;
            }

            var lapsed = matching
                .Select(q => (Qualification: q, Ended: q.LapsedBy(at, knownAt)))
                .Where(x => x.Ended is not null)
                .OrderByDescending(x => x.Ended)
                .FirstOrDefault();
            NodeRef[] missingEvidence = requirement.ProtocolVersionId is { } requiredVersion
                ? [NodeRef.For(requirement.Id), NodeRef.For(requiredVersion)]
                : [NodeRef.For(requirement.Id)];
            if (lapsed.Qualification is { } expired)
            {
                NodeRef[] expiredEvidence = requirement.ProtocolVersionId is { } expiredVersion
                    ? [NodeRef.For(requirement.Id), NodeRef.For(expired.Id), NodeRef.For(expiredVersion)]
                    : [NodeRef.For(requirement.Id), NodeRef.For(expired.Id)];
                conditions.Add(EvaluatedCondition.Unsatisfied(
                    AuthorityEvaluator.QualificationCondition,
                    $"Renew {named}; it expired on {lapsed.Ended!.Value:yyyy-MM-dd}",
                    expiredEvidence));
            }
            else
            {
                conditions.Add(EvaluatedCondition.Unsatisfied(AuthorityEvaluator.QualificationCondition, $"Obtain {named}", missingEvidence));
            }
        }

        return conditions;
    }

    internal static string DescribeActor(Actor actor, string personFallback)
    {
        if (!UuidPattern().IsMatch(actor.Id))
        {
            return actor.Id;
        }

        return actor.Kind switch
        {
            ActorKind.ServiceAccount => "The service account",
            ActorKind.AutomationAgent => "The automation agent",
            _ => personFallback,
        };
    }

    internal static NodeRef? ActorEvidence(Actor actor) =>
        actor.Kind == ActorKind.Person && Guid.TryParse(actor.Id, out var personGuid) && personGuid != Guid.Empty
            ? NodeRef.For(PersonId.FromGuid(personGuid))
            : null;

    internal static async Task<IReadOnlyDictionary<RoleId, Role>> RoleCapabilitiesFromStoreAsync(
        IAuthorityStore store,
        TenantId tenant,
        IReadOnlyList<Grant> held,
        CancellationToken cancellationToken)
    {
        var ids = held.Where(g => g.RoleId is not null).Select(g => g.RoleId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<RoleId, Role>();
        }

        var roles = await store.RolesAsync(tenant, ids, cancellationToken).ConfigureAwait(false);
        return roles.ToDictionary(r => r.Id);
    }

    internal static bool Carries(Grant grant, string token, IReadOnlyDictionary<RoleId, Role> roles) =>
        string.Equals(grant.Capability, token, StringComparison.Ordinal)
        || (grant.RoleId is { } roleId && roles.TryGetValue(roleId, out var role) && role.Capabilities.Contains(token, StringComparer.Ordinal));

    private static string Named(QualificationRequirement requirement, IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers)
    {
        if (requirement.ProtocolVersionId is not { } version)
        {
            return requirement.QualificationCode;
        }

        return versionNumbers.TryGetValue(version, out var number)
            ? $"{requirement.QualificationCode} for protocol version {number}"
            : $"{requirement.QualificationCode} for the required protocol version";
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex UuidPattern();
}
