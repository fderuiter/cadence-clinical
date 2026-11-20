using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// Direct unit tests for <see cref="RequesterBoundVisibility"/> (ADR-2633 §4.2, K-AI-3, K-AI-6): skips the agent pass
/// when the requester masks every node, passes only requester-visible nodes to <see cref="AgentPrincipal.Acting"/>, and
/// preserves the requester's <see cref="MaskReason"/> precedence on mixed sets.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AgentVisibilityTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("01920000-0000-7000-8000-000000000001"));
    private static readonly Actor Requester = Actor.Person("coordinator@cadence.test");
    private static readonly Actor Agent = Actor.AutomationAgent("data-reviewer");
    private static readonly AgentPrincipal Principal = new(
        Tenant, Agent, Requester, Guid.Parse("01920000-0000-7000-8000-000000000099"));

    private static readonly NodeRef BlindedNode = new(NodeKinds.Observation, Guid.Parse("01920000-0000-7000-8000-000000000101"), 1);
    private static readonly NodeRef VaultNode = new(NodeKinds.Participant, Guid.Parse("01920000-0000-7000-8000-000000000102"), 1);
    private static readonly NodeRef ForbiddenToRequester = new(NodeKinds.StudySite, Guid.Parse("01920000-0000-7000-8000-000000000103"), 1);
    private static readonly NodeRef ForbiddenToAgent = new(NodeKinds.StudySite, Guid.Parse("01920000-0000-7000-8000-000000000104"), 1);
    private static readonly NodeRef VisibleToBoth = new(NodeKinds.Study, Guid.Parse("01920000-0000-7000-8000-000000000105"), 1);

    [Fact(DisplayName = "RequesterBoundVisibility_WhenRequesterMasksEveryNode_SkipsAgentPass")]
    [KernelRule("K-AI-3")]
    [KernelRule("K-AI-6")]
    public async Task RequesterBoundVisibilityWhenRequesterMasksEveryNodeSkipsAgentPass()
    {
        var spy = new SpyNodeVisibility();
        spy.Hide(Requester, BlindedNode, MaskReason.Blinded);
        spy.Hide(Requester, VaultNode, MaskReason.IdentityVault);
        spy.Hide(Requester, ForbiddenToRequester, MaskReason.NotPermitted);
        var visibility = new RequesterBoundVisibility(spy);

        var masked = await visibility.MaskAsync(
            Principal, [BlindedNode, VaultNode, ForbiddenToRequester], CancellationToken.None);

        spy.Calls.Should().ContainSingle("no node survived the requester check, so principal.Acting is not queried");
        spy.Calls[0].Reader.Should().Be(Requester);
        masked.Should().Equal(new Dictionary<NodeRef, MaskReason>
        {
            [BlindedNode] = MaskReason.Blinded,
            [VaultNode] = MaskReason.IdentityVault,
            [ForbiddenToRequester] = MaskReason.NotPermitted,
        });
    }

    [Fact(DisplayName = "RequesterBoundVisibility_MixedSet_PassesOnlyRequesterVisibleNodesToAgentAndPreservesRequesterReasons")]
    [KernelRule("K-AI-3")]
    [KernelRule("K-AI-6")]
    public async Task RequesterBoundVisibilityMixedSetPassesOnlyRequesterVisibleNodesToAgentAndPreservesRequesterReasons()
    {
        var spy = new SpyNodeVisibility();
        spy.Hide(Requester, BlindedNode, MaskReason.Blinded);
        spy.Hide(Requester, VaultNode, MaskReason.IdentityVault);
        spy.Hide(Requester, ForbiddenToRequester, MaskReason.NotPermitted);
        spy.Hide(Principal.Acting, BlindedNode, MaskReason.NotPermitted);
        spy.Hide(Principal.Acting, ForbiddenToAgent, MaskReason.NotPermitted);
        var visibility = new RequesterBoundVisibility(spy);

        NodeRef[] input =
        [
            BlindedNode,
            VaultNode,
            ForbiddenToRequester,
            ForbiddenToAgent,
            VisibleToBoth,
            VisibleToBoth,
        ];

        var masked = await visibility.MaskAsync(Principal, input, CancellationToken.None);

        spy.Calls.Should().HaveCount(2);
        spy.Calls[0].Reader.Should().Be(Requester);
        spy.Calls[1].Reader.Should().Be(Principal.Acting);
        spy.Calls[1].Nodes.Should().Equal(ForbiddenToAgent, VisibleToBoth);

        masked.Should().Equal(new Dictionary<NodeRef, MaskReason>
        {
            [BlindedNode] = MaskReason.Blinded,
            [VaultNode] = MaskReason.IdentityVault,
            [ForbiddenToRequester] = MaskReason.NotPermitted,
            [ForbiddenToAgent] = MaskReason.NotPermitted,
        });
    }

    [Fact(DisplayName = "RequesterBoundVisibility_EmptyNodes_MakesZeroVisibilityCalls")]
    public async Task RequesterBoundVisibilityEmptyNodesMakesZeroVisibilityCalls()
    {
        var spy = new SpyNodeVisibility();
        var visibility = new RequesterBoundVisibility(spy);

        var masked = await visibility.MaskAsync(Principal, [], CancellationToken.None);

        masked.Should().BeEmpty();
        spy.Calls.Should().BeEmpty();
    }

    private sealed class SpyNodeVisibility : INodeVisibility
    {
        private readonly Dictionary<Actor, Dictionary<NodeRef, MaskReason>> _rules = [];

        public List<(Actor Reader, IReadOnlyList<NodeRef> Nodes)> Calls { get; } = [];

        public void Hide(Actor reader, NodeRef node, MaskReason reason)
        {
            if (!_rules.TryGetValue(reader, out var byNode))
            {
                _rules[reader] = byNode = [];
            }

            byNode[node] = reason;
        }

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            Calls.Add((reader, [.. nodes]));
            var readerRules = _rules.GetValueOrDefault(reader);
            if (readerRules is null)
            {
                return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(new Dictionary<NodeRef, MaskReason>());
            }

            var matched = nodes
                .Distinct()
                .Where(readerRules.ContainsKey)
                .ToDictionary(n => n, n => readerRules[n]);
            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(matched);
        }
    }
}
