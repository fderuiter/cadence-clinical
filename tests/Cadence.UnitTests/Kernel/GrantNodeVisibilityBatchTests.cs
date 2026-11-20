using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Batched scope lookups, unversioned <see cref="NodeRef"/> deduplication, delegation-chain frontier batching, and
/// automation-agent requester intersection in <see cref="GrantNodeVisibility"/> and <see cref="GrantChain"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GrantNodeVisibilityBatchTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly OrganizationId Sponsor = OrganizationId.New();
    private static readonly OrganizationId Institution = OrganizationId.New();
    private static readonly StudyId StudyA = StudyId.New();
    private static readonly StudyId StudyB = StudyId.New();
    private static readonly StudySiteId SiteA1 = StudySiteId.New();
    private static readonly StudySiteId SiteA2 = StudySiteId.New();
    private static readonly StudySiteId SiteB1 = StudySiteId.New();
    private static readonly Actor Admin = Actor.Person("admin@sponsor.test");
    private static readonly Actor Crc = Actor.Person("crc@site.test");
    private static readonly Actor AgentIdentity = Actor.AutomationAgent("triage-agent");

    private static readonly ProtocolVersionId ProtocolAId = ProtocolVersionId.New();
    private static readonly ProtocolVersionId ProtocolBId = ProtocolVersionId.New();
    private static readonly EnrollmentId EnrollmentA1Id = EnrollmentId.New();
    private static readonly EnrollmentId EnrollmentA2Id = EnrollmentId.New();
    private static readonly ParticipantId ParticipantA1Id = ParticipantId.New();
    private static readonly ParticipantId ParticipantA2Id = ParticipantId.New();
    private static readonly SiteId HospitalAId = SiteId.New();
    private static readonly SiteId HospitalBId = SiteId.New();

    /// <summary>
    /// Multiple versioned and unversioned references to the same object resolve its scope path once via unversioned
    /// <c>(Kind, Id)</c> and map <see cref="MaskReason.NotPermitted"/> back to every matching input <see cref="NodeRef"/>.
    /// </summary>
    [Fact(DisplayName = "Visibility_DeduplicatesUnversionedObjects_AndMapsMasksBackToEveryVersion")]
    public async Task VisibilityDeduplicatesUnversionedObjectsAndMapsMasksBackToEveryVersion()
    {
        var grant = Issue(Crc, AuthorityScope.StudySite(SiteA1));
        var store = new RecordingStore(grant);
        var visibility = new GrantNodeVisibility(store, new FakeClock(T0.AddSeconds(1)));

        NodeRef[] input =
        [
            NodeRef.For(ProtocolAId, 1),
            NodeRef.For(ProtocolAId, 2),
            NodeRef.For(ProtocolAId),
            NodeRef.For(ProtocolBId, 1),
            NodeRef.For(ProtocolBId, 2),
            NodeRef.For(ProtocolBId),
            NodeRef.For(ProtocolBId, 2),
            NodeRef.For(EnrollmentA1Id, 1),
            NodeRef.For(EnrollmentA2Id, 1),
            NodeRef.For(EnrollmentA2Id, 3),
            NodeRef.For(ParticipantA1Id, 1),
            NodeRef.For(ParticipantA1Id, 2),
            NodeRef.For(ParticipantA2Id, 1),
            NodeRef.For(ParticipantA2Id, 2),
            NodeRef.For(ConceptId.New(), 1),
        ];

        var masked = await visibility.MaskAsync(Tenant, Crc, input, CancellationToken.None);

        masked.Keys.Should().BeEquivalentTo(
        [
            NodeRef.For(ProtocolBId, 1),
            NodeRef.For(ProtocolBId, 2),
            NodeRef.For(ProtocolBId),
            NodeRef.For(EnrollmentA2Id, 1),
            NodeRef.For(EnrollmentA2Id, 3),
            NodeRef.For(ParticipantA2Id, 1),
            NodeRef.For(ParticipantA2Id, 2),
        ]);
        masked.Values.Should().AllSatisfy(r => r.Should().Be(MaskReason.NotPermitted));

        store.PathsOfBatches.Should().ContainSingle();
        var queriedBatch = store.PathsOfBatches[0];
        queriedBatch.Should().OnlyContain(r => r.Version == null, "paths are queried on unversioned (Kind, Id) keys");
        queriedBatch.Should().BeEquivalentTo(
        [
            NodeRef.For(grant.Id),
            NodeRef.For(ProtocolAId),
            NodeRef.For(ProtocolBId),
            NodeRef.For(EnrollmentA1Id),
            NodeRef.For(EnrollmentA2Id),
            NodeRef.For(ParticipantA1Id),
            NodeRef.For(ParticipantA2Id),
        ]);

        store.StudySiteBatches.Should().ContainSingle();
        store.StudySiteBatches[0].Should().BeEquivalentTo(
        [
            NodeRef.For(ParticipantA1Id),
            NodeRef.For(ParticipantA2Id),
        ]);
        store.SinglePathCalls.Should().Be(0);
        store.SingleStudySiteCalls.Should().Be(0);
    }

    /// <summary>
    /// An automation agent acting for a person resolves both actors' non-tenant grant scopes and the candidate nodes in
    /// one shared <see cref="IAuthorityStore.PathsOfAsync"/> call, returning the intersection of what both may see
    /// (K-AI-6, ADR-2632). Acting for nobody, or when either side lacks an active grant, masks everything without path lookups.
    /// </summary>
    [Fact(DisplayName = "Visibility_AutomationAgent_EvaluatesHeldAndRequesterOverOneSharedPathBatch")]
    [KernelRule("K-AI-6")]
    public async Task VisibilityAutomationAgentEvaluatesHeldAndRequesterOverOneSharedPathBatch()
    {
        var agentGrant = Issue(AgentIdentity, AuthorityScope.Study(StudyA));
        var crcGrant = Issue(Crc, AuthorityScope.StudySite(SiteA1));
        var store = new RecordingStore(agentGrant, crcGrant);
        var visibility = new GrantNodeVisibility(store, new FakeClock(T0.AddSeconds(1)));

        NodeRef[] nodes =
        [
            NodeRef.For(StudyA),
            NodeRef.For(StudyB),
            NodeRef.For(SiteA1),
            NodeRef.For(SiteA2),
            NodeRef.For(EnrollmentA1Id),
            NodeRef.For(EnrollmentA2Id),
            NodeRef.For(ParticipantA1Id),
            NodeRef.For(ParticipantA2Id),
            NodeRef.For(HospitalAId),
            NodeRef.For(HospitalBId),
            NodeRef.For(ConceptId.New()),
        ];

        var masked = await visibility.MaskAsync(Tenant, AgentIdentity.ActingFor(Crc), nodes, CancellationToken.None);

        masked.Keys.Should().BeEquivalentTo(
        [
            NodeRef.For(StudyB),
            NodeRef.For(SiteA2),
            NodeRef.For(EnrollmentA2Id),
            NodeRef.For(ParticipantA2Id),
            NodeRef.For(HospitalBId),
        ]);
        store.PathsOfBatches.Should().ContainSingle("held grants, requester grants and candidate nodes share one path batch");
        store.PathsOfBatches[0].Should().Contain([NodeRef.For(agentGrant.Id), NodeRef.For(crcGrant.Id)]);

        // Symmetric case: narrow agent grant with wider requester grant yields the same intersection.
        var reverseStore = new RecordingStore(
            Issue(AgentIdentity, AuthorityScope.StudySite(SiteA1)),
            Issue(Crc, AuthorityScope.Study(StudyA)));
        var reverseMasked = await new GrantNodeVisibility(reverseStore, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, AgentIdentity.ActingFor(Crc), nodes, CancellationToken.None);
        reverseMasked.Keys.Should().BeEquivalentTo(masked.Keys);

        // Agent acting for nobody sees nothing and performs zero store reads.
        var unattributedStore = new RecordingStore(agentGrant);
        var unattributed = await new GrantNodeVisibility(unattributedStore, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, AgentIdentity, nodes, CancellationToken.None);
        unattributed.Keys.Should().BeEquivalentTo(nodes);
        unattributedStore.PathsOfBatches.Should().BeEmpty();
        unattributedStore.StudySiteBatches.Should().BeEmpty();

        // Agent with grant acting for a person with no grant masks everything without path lookups.
        var noRequesterGrantStore = new RecordingStore(agentGrant);
        var noRequesterMasked = await new GrantNodeVisibility(noRequesterGrantStore, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, AgentIdentity.ActingFor(Crc), nodes, CancellationToken.None);
        noRequesterMasked.Keys.Should().BeEquivalentTo(nodes);
        noRequesterGrantStore.PathsOfBatches.Should().BeEmpty();
    }

    /// <summary>
    /// <see cref="IAuthorityStore.StudySitePathsAsync(TenantId, IReadOnlyCollection{NodeRef}, CancellationToken)"/> is
    /// skipped entirely when uncovered nodes contain neither <c>participant</c> nor <c>site</c>, or when a <c>site</c>
    /// is already covered by its institution organization grant.
    /// </summary>
    [Fact(DisplayName = "Visibility_SkipsStudySiteLookup_WhenNoUncoveredParticipantOrSiteRemains")]
    public async Task VisibilitySkipsStudySiteLookupWhenNoUncoveredParticipantOrSiteRemains()
    {
        var siteGrantStore = new RecordingStore(Issue(Crc, AuthorityScope.StudySite(SiteA1)));
        var visibility = new GrantNodeVisibility(siteGrantStore, new FakeClock(T0.AddSeconds(1)));

        NodeRef[] nonReachable =
        [
            NodeRef.For(StudyA),
            NodeRef.For(StudyB),
            NodeRef.For(SiteA1),
            NodeRef.For(SiteA2),
            NodeRef.For(EnrollmentA2Id),
            NodeRef.For(ObservationId.New()),
        ];

        var masked = await visibility.MaskAsync(Tenant, Crc, nonReachable, CancellationToken.None);

        masked.Keys.Should().BeEquivalentTo(
        [
            NodeRef.For(StudyB),
            NodeRef.For(SiteA2),
            NodeRef.For(EnrollmentA2Id),
            nonReachable[^1],
        ]);
        siteGrantStore.StudySiteBatches.Should().BeEmpty("no participant or site remained uncovered");

        var institutionStore = new RecordingStore(Issue(Admin, AuthorityScope.Organization(Institution)));
        var institutionMasked = await new GrantNodeVisibility(institutionStore, new FakeClock(T0.AddSeconds(1)))
            .MaskAsync(Tenant, Admin, [NodeRef.For(HospitalAId), NodeRef.For(HospitalBId)], CancellationToken.None);

        institutionMasked.Should().BeEmpty("both hospitals belong to Institution directly on their scope path");
        institutionStore.StudySiteBatches.Should().BeEmpty("covered sites do not trigger study-site reachability queries");
    }

    /// <summary>
    /// Validates <see cref="GrantChain.FilterInEffectAsync"/> frontier batching (K-AUTH-3): delegations up to
    /// <see cref="AuthorityEvaluator.MaxDelegationDepth"/> (8) resolve one batch per depth level, depth 9 is rejected,
    /// shared ancestors are fetched once, and revoked, future or missing ancestors invalidate only their own subtree.
    /// </summary>
    [Fact(DisplayName = "GrantChain_BatchesFrontiersUpToMaxDelegationDepth_AndHonorsBitemporalValidity")]
    [KernelRule("K-AUTH-3")]
    public async Task GrantChainBatchesFrontiersUpToMaxDelegationDepthAndHonorsBitemporalValidity()
    {
        var now = T0.AddMinutes(5);
        var depth8Chain = BuildDelegationChain(AuthorityEvaluator.MaxDelegationDepth, AuthorityScope.StudySite(SiteA1));
        var depth8Store = new RecordingStore([.. depth8Chain]);
        var depth8Visibility = new GrantNodeVisibility(depth8Store, new FakeClock(now));

        var depth8Masked = await depth8Visibility.MaskAsync(
            Tenant, depth8Chain[^1].Grantee, [NodeRef.For(SiteA1), NodeRef.For(SiteA2)], CancellationToken.None);

        depth8Masked.Keys.Should().Equal(NodeRef.For(SiteA2));
        depth8Store.FindGrantBatches.Should().HaveCount(AuthorityEvaluator.MaxDelegationDepth);
        depth8Store.SingleFindGrantCalls.Should().Be(0);

        // Exceeding MaxDelegationDepth (depth 9) rejects the leaf delegation.
        var depth9Chain = BuildDelegationChain(AuthorityEvaluator.MaxDelegationDepth + 1, AuthorityScope.StudySite(SiteA1));
        var depth9Store = new RecordingStore([.. depth9Chain]);
        var depth9Masked = await new GrantNodeVisibility(depth9Store, new FakeClock(now))
            .MaskAsync(Tenant, depth9Chain[^1].Grantee, [NodeRef.For(SiteA1), NodeRef.For(SiteA2)], CancellationToken.None);

        depth9Masked.Keys.Should().BeEquivalentTo([NodeRef.For(SiteA1), NodeRef.For(SiteA2)]);

        // Shared ancestor + mixed sibling branches (valid, revoked parent, future parent, missing parent).
        var root = Issue(Admin, AuthorityScope.Study(StudyA), withGrantOption: true);
        var validBranch = Issue(Actor.Person("lead-1@site.test"), AuthorityScope.StudySite(SiteA1), withGrantOption: true, parent: root.Id);
        var revokedBranch = Issue(Actor.Person("lead-2@site.test"), AuthorityScope.StudySite(SiteA2), withGrantOption: true, parent: root.Id);
        revokedBranch.Revoke(T0.AddMinutes(1), T0.AddMinutes(1)).Should().BeNull();
        var futureBranch = Issue(Actor.Person("lead-3@site.test"), AuthorityScope.Study(StudyB), validFrom: now.AddHours(1), withGrantOption: true, parent: root.Id);

        var leafValid = Issue(Crc, AuthorityScope.StudySite(SiteA1), parent: validBranch.Id);
        var leafUnderRevoked = Issue(Crc, AuthorityScope.StudySite(SiteA2), parent: revokedBranch.Id);
        var leafUnderFuture = Issue(Crc, AuthorityScope.Study(StudyB), parent: futureBranch.Id);
        var leafUnderMissing = Issue(Crc, AuthorityScope.Tenant(), parent: GrantId.New());

        var mixedStore = new RecordingStore(
            root, validBranch, revokedBranch, futureBranch, leafValid, leafUnderRevoked, leafUnderFuture, leafUnderMissing);
        var inEffect = await GrantChain.FilterInEffectAsync(
            mixedStore, Tenant, [leafValid, leafUnderRevoked, leafUnderFuture, leafUnderMissing], now, now, CancellationToken.None);

        inEffect.Should().Equal(leafValid.Id);
        mixedStore.FindGrantBatches.Should().HaveCount(2, "level 1 batches all 4 parents; level 2 fetches only the shared root of the valid parent");
        mixedStore.FindGrantBatches[1].Should().Equal(root.Id);
    }

    private static List<Grant> BuildDelegationChain(int delegations, AuthorityScope leafScope)
    {
        var chain = new List<Grant>(delegations + 1);
        var root = Issue(Admin, AuthorityScope.Tenant(), withGrantOption: true);
        chain.Add(root);
        var parentId = root.Id;
        for (var i = 1; i <= delegations; i++)
        {
            var actor = Actor.Person($"delegate-{i}@site.test");
            var scope = i == delegations ? leafScope : AuthorityScope.Study(StudyA);
            var next = Issue(actor, scope, withGrantOption: i < delegations, parent: parentId);
            chain.Add(next);
            parentId = next.Id;
        }

        return chain;
    }

    private static Grant Issue(
        Actor grantee,
        AuthorityScope scope,
        DateTimeOffset? validFrom = null,
        bool withGrantOption = false,
        GrantId? parent = null) =>
        Grant.Issue(GrantId.New(), Tenant, grantee, "observation.record", null, scope, validFrom ?? T0, null, withGrantOption, parent, Admin, T0).Value!;

    private sealed class RecordingStore(params Grant[] allGrants) : IAuthorityStore
    {
        private static readonly ScopePath A1 = new(Sponsor, StudyA, "US", SiteA1);
        private static readonly ScopePath A2 = new(Sponsor, StudyA, "US", SiteA2);
        private static readonly ScopePath B1 = new(Sponsor, StudyB, "DE", SiteB1);

        private static readonly Dictionary<NodeRef, ScopePath> Paths = new()
        {
            [NodeRef.For(Sponsor)] = new(Sponsor),
            [NodeRef.For(Institution)] = new(Institution),
            [NodeRef.For(StudyA)] = new(Sponsor, StudyA),
            [NodeRef.For(StudyB)] = new(Sponsor, StudyB),
            [NodeRef.For(SiteA1)] = A1,
            [NodeRef.For(SiteA2)] = A2,
            [NodeRef.For(SiteB1)] = B1,
            [NodeRef.For(ProtocolAId)] = new(Sponsor, StudyA),
            [NodeRef.For(ProtocolBId)] = new(Sponsor, StudyB),
            [NodeRef.For(EnrollmentA1Id)] = A1,
            [NodeRef.For(EnrollmentA2Id)] = A2,
            [NodeRef.For(HospitalAId)] = new(Institution),
            [NodeRef.For(HospitalBId)] = new(Institution),
        };

        private static readonly Dictionary<NodeRef, ScopePath[]> Reached = new()
        {
            [NodeRef.For(ParticipantA1Id)] = [A1],
            [NodeRef.For(ParticipantA2Id)] = [A2],
            [NodeRef.For(HospitalAId)] = [A1],
            [NodeRef.For(HospitalBId)] = [B1],
        };

        public List<IReadOnlyList<GrantId>> FindGrantBatches { get; } = [];

        public List<IReadOnlyList<NodeRef>> PathsOfBatches { get; } = [];

        public List<IReadOnlyList<NodeRef>> StudySiteBatches { get; } = [];

        public int SingleFindGrantCalls { get; private set; }

        public int SinglePathCalls { get; private set; }

        public int SingleStudySiteCalls { get; private set; }

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. _allGrants.Where(g => g.Grantee == actor)]);

        private readonly Grant[] _allGrants = allGrants;

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken)
        {
            SingleFindGrantCalls++;
            return Task.FromResult(_allGrants.SingleOrDefault(g => g.Id == grant));
        }

        public Task<IReadOnlyDictionary<GrantId, Grant>> FindGrantsAsync(
            TenantId tenant, IReadOnlyCollection<GrantId> grants, CancellationToken cancellationToken)
        {
            FindGrantBatches.Add([.. grants]);
            var found = _allGrants.Where(g => grants.Contains(g.Id)).ToDictionary(g => g.Id);
            return Task.FromResult<IReadOnlyDictionary<GrantId, Grant>>(found);
        }

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            SinglePathCalls++;
            return Task.FromResult(ResolvePath(resource));
        }

        public Task<IReadOnlyDictionary<NodeRef, ScopePath>> PathsOfAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
        {
            PathsOfBatches.Add([.. resources]);
            var resolved = resources.Distinct().ToDictionary(r => r, ResolvePath);
            return Task.FromResult<IReadOnlyDictionary<NodeRef, ScopePath>>(resolved);
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
        {
            SingleStudySiteCalls++;
            return Task.FromResult<IReadOnlyList<ScopePath>>(Reached.GetValueOrDefault(node.Unversioned(), []));
        }

        public Task<IReadOnlyDictionary<NodeRef, IReadOnlyList<ScopePath>>> StudySitePathsAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            StudySiteBatches.Add([.. nodes]);
            var resolved = nodes.Distinct().ToDictionary(
                n => n,
                n => (IReadOnlyList<ScopePath>)Reached.GetValueOrDefault(n.Unversioned(), []));
            return Task.FromResult<IReadOnlyDictionary<NodeRef, IReadOnlyList<ScopePath>>>(resolved);
        }

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(
            TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);

        private ScopePath ResolvePath(NodeRef resource)
        {
            var key = resource.Unversioned();
            if (key.Kind == NodeKinds.Grant)
            {
                var scope = _allGrants.Single(g => g.Id.Value == key.Id).Scope;
                return scope.Level switch
                {
                    ScopeLevel.Organization => Paths[NodeRef.For(OrganizationId.FromGuid(scope.TargetId!.Value))],
                    ScopeLevel.Study => Paths[NodeRef.For(StudyId.FromGuid(scope.TargetId!.Value))],
                    ScopeLevel.StudyCountry => new ScopePath(Sponsor, StudyId.FromGuid(scope.TargetId!.Value), scope.Country),
                    ScopeLevel.StudySite => Paths[NodeRef.For(StudySiteId.FromGuid(scope.TargetId!.Value))],
                    _ => ScopePath.TenantWide,
                };
            }

            return Paths.GetValueOrDefault(key, ScopePath.TenantWide);
        }
    }
}
