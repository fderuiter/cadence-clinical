using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The lineage store (spec §9.6, ADR-2255, ADR-2471) against PostgreSQL as the application role: edges written by the
/// command pipeline in the object's transaction (K-LIN-2), derived objects refused without inputs (K-LIN-1), and the
/// masked read contract (#4840). Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LineagePostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Lineage test";

    private static readonly Actor Reader = Actor.Person("reader@cadence.test");

    /// <summary>
    /// Validates K-LIN-2 for a real writer: mapping a concept records the mapping version as a derived node with a
    /// <c>definedBy</c> edge from the concept version and from the code-system version, written by the same
    /// ProvenanceActivity as the mapping.
    /// </summary>
    [Fact(DisplayName = "MapConcept_WritesMappingNodeAndDefinedByEdges_InTheCommandActivity")]
    [KernelRule("K-LIN-2")]
    public async Task MapConceptWritesMappingNodeAndDefinedByEdgesInTheCommandActivity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var mapping = NodeRef.For(seed.Mapping, 1);

        await using var db = fixture.CreateContext(seed.Tenant);
        var activity = await db.ProvenanceActivities.AsNoTracking()
            .SingleAsync(a => a.AggregateType == nameof(ConceptMapping) && a.AggregateId == seed.Mapping.Value.ToString("D"), ct);
        var node = await db.LineageNodes.AsNoTracking().SingleAsync(n => n.NodeId == seed.Mapping.Value, ct);
        var edges = await db.LineageEdges.AsNoTracking().Where(e => e.ToId == seed.Mapping.Value).ToListAsync(ct);

        node.Should().BeEquivalentTo(new { Kind = NodeKinds.ConceptMapping, Version = 1L, Derived = true, ActivityId = activity.Id });
        edges.Select(e => (new NodeRef(e.FromKind, e.FromId, e.FromVersion), e.Kind, e.ToVersion, e.ActivityId)).Should().BeEquivalentTo(
            [
                (NodeRef.For(seed.Concept.ConceptId, seed.Concept.Version), LineageEdgeKind.DefinedBy, 1L, activity.Id),
                (NodeRef.For(seed.CodeSystemVersion, 1), LineageEdgeKind.DefinedBy, 1L, activity.Id),
            ]);

        var trace = await fixture.Lineage.TraceAsync(seed.Tenant, Reader, new LineageQuery(mapping, LineageDirection.Backward), ct);
        trace.Nodes.Select(n => n.Ref!.Kind).Should().BeEquivalentTo(NodeKinds.ConceptMapping, NodeKinds.Concept, NodeKinds.CodeSystemVersion);
        trace.Truncated.Should().BeFalse();
    }

    /// <summary>
    /// Validates that every update supersedes the previous version: reviewing a mapping and revising a concept each
    /// write a <c>supersedes</c> edge from version n to n + 1, so a forward trace from the first concept version
    /// reaches its revision and the mapping built on it.
    /// </summary>
    [Fact(DisplayName = "Updates_WriteSupersedesEdges_FromThePreviousVersion")]
    public async Task UpdatesWriteSupersedesEdgesFromThePreviousVersion()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);

        (await fixture.Sender.SendAsync(
            new ReviewConceptMapping(seed.Tenant, seed.Mapping, MappingStatus.Confirmed, Key(), Reason), cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(
            new ReviseConcept(seed.Tenant, seed.Concept.ConceptId, "Sitting", "Seated position.", Key(), Reason), cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var forward = await fixture.Lineage.TraceAsync(
            seed.Tenant, Reader, new LineageQuery(NodeRef.For(seed.Concept.ConceptId, 1), LineageDirection.Forward), ct);

        forward.Edges.Select(e => (e.From, e.To, e.Kind)).Should().BeEquivalentTo(
            [
                (Ref(seed.Concept.ConceptId, 1), Ref(seed.Concept.ConceptId, 2), LineageEdgeKind.Supersedes),
                (Ref(seed.Concept.ConceptId, 1), Ref(seed.Mapping, 1), LineageEdgeKind.DefinedBy),
                (Ref(seed.Mapping, 1), Ref(seed.Mapping, 2), LineageEdgeKind.Supersedes),
            ]);
        forward.Nodes.Single(n => n.Handle == Ref(seed.Mapping, 2)).Distance.Should().Be(2);
    }

    /// <summary>
    /// Validates K-LIN-2 atomicity: when the commit fails after lineage is staged, neither the object, its node nor its
    /// edges are written.
    /// </summary>
    [Fact(DisplayName = "FailedCommit_WritesNoLineage")]
    [KernelRule("K-LIN-2")]
    public async Task FailedCommitWritesNoLineage()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var mapping = ConceptMappingId.New();
        var unitOfWork = fixture.UnitOfWork;
        unitOfWork.FaultInjector = stage =>
        {
            if (stage == CommitStage.AuditAppended)
            {
                throw new InvalidOperationException("Injected failure after lineage was staged.");
            }
        };

        try
        {
            var act = () => fixture.Sender.SendAsync(
                new MapConcept(seed.Tenant, mapping, seed.Concept, new Coding(seed.CodeSystemVersion, "C62166"), MappingRelation.Exact, "Reviewed.", false, Key(), Reason),
                cancellationToken: ct);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            unitOfWork.FaultInjector = null;
        }

        await using var db = fixture.CreateAdminContext();
        (await db.ConceptMappings.AnyAsync(m => m.Id == mapping, ct)).Should().BeFalse();
        (await db.LineageNodes.AnyAsync(n => n.NodeId == mapping.Value, ct)).Should().BeFalse();
        (await db.LineageEdges.AnyAsync(e => e.ToId == mapping.Value, ct)).Should().BeFalse();
    }

    /// <summary>
    /// Validates K-LIN-1 in the pipeline: a derived object committed without any declared input is refused and writes
    /// nothing, and an input without a version is refused when it is declared.
    /// </summary>
    [Fact(DisplayName = "DerivedObject_WithoutInputs_IsRefused")]
    [KernelRule("K-LIN-1")]
    public async Task DerivedObjectWithoutInputsIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var mapping = ConceptMapping.Create(
            ConceptMappingId.New(), seed.Tenant, seed.Concept, new Coding(seed.CodeSystemVersion, "C62167"), MappingRelation.Exact, "Reviewed.", false).Value!;

        await using (var transaction = fixture.UnitOfWork.Begin(Execution(seed.Tenant)))
        {
            transaction.Work.Add(mapping);
            var unversioned = () => transaction.Work.LinkInput(NodeRef.For(seed.Concept.ConceptId), LineageEdgeKind.DefinedBy);
            unversioned.Should().Throw<ArgumentException>().WithMessage("K-LIN*");

            var commit = () => transaction.CommitAsync("null", ct);
            (await commit.Should().ThrowAsync<InvalidOperationException>()).WithMessage("K-LIN-1*");
        }

        await using var db = fixture.CreateAdminContext();
        (await db.ConceptMappings.AnyAsync(m => m.Id == mapping.Id, ct)).Should().BeFalse();
        (await db.LineageNodes.AnyAsync(n => n.NodeId == mapping.Id.Value, ct)).Should().BeFalse();
    }

    /// <summary>
    /// Validates the backward and forward walks over a six-object chain: every object is reached at its fewest-edges
    /// distance, every edge is returned once, and the forward walk from the head reaches the tail.
    /// </summary>
    [Fact(DisplayName = "Trace_WalksBackwardAndForward_WithDistances")]
    public async Task TraceWalksBackwardAndForwardWithDistances()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);

        var backward = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward), ct);
        var forward = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Study, LineageDirection.Forward), ct);

        backward.Start.Should().Be(chain.Derived.ToString());
        backward.Nodes.Select(n => n.Distance).Should().BeInAscendingOrder();
        backward.Nodes.Select(n => (n.Handle, n.Distance)).Should().BeEquivalentTo(
            [
                (chain.Derived.ToString(), 0),
                (chain.Corrected.ToString(), 1),
                (chain.Original.ToString(), 2),
                (chain.Protocol.ToString(), 3),
                (chain.StudySite.ToString(), 3),
                (chain.Study.ToString(), 4),
            ]);
        backward.Edges.Should().HaveCount(5);
        backward.Truncated.Should().BeFalse();
        forward.Nodes.Select(n => n.Handle).Should().Equal(
            chain.Study.ToString(), chain.Protocol.ToString(), chain.Original.ToString(), chain.Corrected.ToString(), chain.Derived.ToString());
        forward.Nodes[^1].Distance.Should().Be(4);
    }

    /// <summary>
    /// Validates #4840: an object the reader may not see, in the middle of a trace, comes back as
    /// <c>Masked(kind, reason)</c> with an opaque handle. It is not dropped, the path keeps its length, the objects
    /// beyond it are still reached, the edges touching it withhold their activity, and its id appears nowhere in the
    /// result.
    /// </summary>
    [Fact(DisplayName = "Trace_MaskedNodeInTheMiddle_KeepsThePathLength")]
    public async Task TraceMaskedNodeInTheMiddleKeepsThePathLength()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        fixture.Visibility.Masked[chain.Protocol] = MaskReason.Blinded;
        try
        {
            var trace = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward), ct);

            var masked = trace.Nodes.Single(n => n.IsMasked);
            masked.Should().BeEquivalentTo(new { Handle = "masked:1", Kind = NodeKinds.ProtocolVersion, Ref = (NodeRef?)null, Mask = MaskReason.Blinded, Distance = 3 });
            trace.Nodes.Should().HaveCount(6);
            trace.Nodes.Single(n => n.Handle == chain.Study.ToString()).Distance.Should().Be(4);
            trace.Edges.Should().HaveCount(5);
            trace.Edges.Where(e => e.From == "masked:1" || e.To == "masked:1").Select(e => (e.From, e.To)).Should().BeEquivalentTo(
                [(chain.Study.ToString(), "masked:1"), ("masked:1", chain.Original.ToString())]);
            trace.Edges.Should().OnlyContain(e => (e.ActivityId == null) == (e.From == "masked:1" || e.To == "masked:1"));
            JsonSerializer.Serialize(trace).Should().NotContain(chain.Protocol.Id.ToString("D"), "a masked object's id never leaves the read");
        }
        finally
        {
            fixture.Visibility.Masked.Clear();
        }
    }

    /// <summary>
    /// Validates that masking hides the object, not one version: when the reader may not see one version, every
    /// version of that object in the result is masked, so no visible version reveals its id.
    /// </summary>
    [Fact(DisplayName = "Trace_MaskedVersion_MasksEveryVersionOfTheObject")]
    public async Task TraceMaskedVersionMasksEveryVersionOfTheObject()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        fixture.Visibility.Masked[chain.Original] = MaskReason.IdentityVault;
        try
        {
            var trace = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward), ct);

            trace.Nodes.Where(n => n.IsMasked).Select(n => (n.Handle, n.Distance, n.Mask)).Should().Equal(
                ("masked:1", 1, MaskReason.IdentityVault), ("masked:2", 2, MaskReason.IdentityVault));
            JsonSerializer.Serialize(trace).Should().NotContain(chain.Original.Id.ToString("D"));
        }
        finally
        {
            fixture.Visibility.Masked.Clear();
        }
    }

    /// <summary>
    /// Validates the depth limit: a walk that stops before the graph ends says so with <c>Truncated</c>, and one that
    /// reaches exactly the last object does not.
    /// </summary>
    [Fact(DisplayName = "Trace_DepthLimit_SetsTruncatedOnlyWhenTheGraphContinues")]
    public async Task TraceDepthLimitSetsTruncatedOnlyWhenTheGraphContinues()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);

        var short3 = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward, Depth: 3), ct);
        var exact4 = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward, Depth: 4), ct);

        short3.Truncated.Should().BeTrue();
        short3.Nodes.Select(n => n.Ref).Should().NotContain(chain.Study);
        short3.Edges.Should().HaveCount(4);
        exact4.Truncated.Should().BeFalse();
        exact4.Nodes.Should().HaveCount(6);

        var tooDeep = () => fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward, Depth: LineageQuery.MaxDepth + 1), ct);
        await tooDeep.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Validates the bitemporal parameters (K-TIME-*): an edge recorded after <c>known_at</c> is not followed, and an
    /// edge that takes effect after <c>valid_at</c> is not followed until then.
    /// </summary>
    [Fact(DisplayName = "Trace_AsOfValidAndKnown_FollowsOnlyEdgesThatHeldThen")]
    public async Task TraceAsOfValidAndKnownFollowsOnlyEdgesThatHeldThen()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        var late = NodeRef.For(StudySiteId.New(), 1);
        var future = NodeRef.For(StudySiteId.New(), 1);
        await fixture.SeedAsync(
            Edge(chain.Tenant, late, chain.Original, LineageEdgeKind.CollectedBy, chain.RecordedAt.AddDays(1)),
            Edge(chain.Tenant, future, chain.Original, LineageEdgeKind.CollectedBy, chain.RecordedAt, validFrom: chain.RecordedAt.AddDays(10)));

        async Task<IEnumerable<NodeRef?>> Reached(DateTimeOffset validAt, DateTimeOffset knownAt) =>
            (await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(chain.Original, LineageDirection.Backward, 1, ValidAt: validAt, KnownAt: knownAt), ct))
            .Nodes.Select(n => n.Ref);

        (await Reached(chain.RecordedAt, chain.RecordedAt)).Should().NotContain([late, future]);
        (await Reached(chain.RecordedAt, chain.RecordedAt.AddDays(2))).Should().Contain(late).And.NotContain(future);
        (await Reached(chain.RecordedAt.AddDays(10), chain.RecordedAt.AddDays(2))).Should().Contain([late, future]);
    }

    /// <summary>Validates the edge-kind filter: a walk restricted to some kinds stops where only other kinds continue.</summary>
    [Fact(DisplayName = "Trace_EdgeKindFilter_FollowsOnlyTheNamedKinds")]
    public async Task TraceEdgeKindFilterFollowsOnlyTheNamedKinds()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);

        var trace = await fixture.Lineage.TraceAsync(
            chain.Tenant,
            Reader,
            new LineageQuery(chain.Derived, LineageDirection.Backward, EdgeKinds: new HashSet<LineageEdgeKind> { LineageEdgeKind.DerivedFrom, LineageEdgeKind.Supersedes }),
            ct);

        trace.Nodes.Select(n => n.Ref).Should().Equal(chain.Derived, chain.Corrected, chain.Original);
        trace.Truncated.Should().BeFalse();
    }

    /// <summary>
    /// Validates that a start without a version walks from every stored version of the object, each at distance 0,
    /// and that masking any version masks the start.
    /// </summary>
    [Fact(DisplayName = "Trace_UnversionedStart_WalksFromEveryVersion")]
    public async Task TraceUnversionedStartWalksFromEveryVersion()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        var observation = chain.Original.Unversioned();

        var trace = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(observation, LineageDirection.Backward, 1), ct);

        trace.Start.Should().Be(observation.ToString());
        trace.Nodes.Where(n => n.Distance == 0).Select(n => n.Ref).Should().BeEquivalentTo([chain.Original, chain.Corrected]);
        trace.Nodes.Where(n => n.Distance == 1).Select(n => n.Ref).Should().BeEquivalentTo([chain.Protocol, chain.StudySite]);

        fixture.Visibility.Masked[observation] = MaskReason.IdentityVault;
        try
        {
            var masked = await fixture.Lineage.TraceAsync(chain.Tenant, Reader, new LineageQuery(observation, LineageDirection.Backward, 1), ct);
            masked.Start.Should().StartWith("masked:");
            masked.Nodes.Where(n => n.Distance == 0).Should().OnlyContain(n => n.Mask == MaskReason.IdentityVault);
        }
        finally
        {
            fixture.Visibility.Masked.Clear();
        }
    }

    /// <summary>
    /// Validates the evidence dossier: one read returns the backward and forward walks at the same time point, grouped
    /// by kind, with one handle per masked object across both walks and a single visibility decision.
    /// </summary>
    [Fact(DisplayName = "Dossier_MergesBothDirections_GroupedByKind")]
    public async Task DossierMergesBothDirectionsGroupedByKind()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        fixture.Visibility.Masked[chain.Derived] = MaskReason.NotPermitted;
        fixture.Visibility.Calls.Clear();
        try
        {
            var dossier = await fixture.Lineage.DossierAsync(chain.Tenant, Reader, chain.Original, cancellationToken: ct);

            dossier.Backward.Nodes.Select(n => n.Ref).Should().BeEquivalentTo([chain.Original, chain.Protocol, chain.StudySite, chain.Study]);
            dossier.Forward.Nodes.Select(n => n.Handle).Should().Equal(chain.Original.ToString(), chain.Corrected.ToString(), "masked:1");
            dossier.ByKind.Keys.Should().BeEquivalentTo(NodeKinds.Observation, NodeKinds.ProtocolVersion, NodeKinds.StudySite, NodeKinds.Study);
            dossier.ByKind[NodeKinds.Observation].Select(n => n.Handle).Should().BeEquivalentTo(
                chain.Original.ToString(), chain.Corrected.ToString(), "masked:1");
            dossier.Forward.KnownAt.Should().Be(dossier.Backward.KnownAt);
            fixture.Visibility.Calls.Should().ContainSingle();
        }
        finally
        {
            fixture.Visibility.Masked.Clear();
        }
    }

    /// <summary>
    /// Validates that lineage reads are tenant-bound (K-ID-2): another tenant tracing the same object sees no edges,
    /// even though the rows exist.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Trace_FromAnotherTenant_SeesNoEdges")]
    [GxPRequirement("PRD-CRF-013")]
    [KernelRule("K-ID-2")]
    public async Task TraceFromAnotherTenantSeesNoEdges()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        var other = await NewTenantAsync(ct);

        var trace = await fixture.Lineage.TraceAsync(other, Reader, new LineageQuery(chain.Derived, LineageDirection.Backward), ct);

        trace.Edges.Should().BeEmpty();
        trace.Nodes.Select(n => n.Ref).Should().Equal(chain.Derived);
    }

    /// <summary>
    /// Validates the scheduled K-LIN-1 check: a derived object version stored without an inbound edge (only possible
    /// by writing outside the pipeline) is reported, and versions written by the pipeline are not.
    /// </summary>
    [Fact(DisplayName = "OrphanCheck_ReportsDerivedVersionsWithoutInputs")]
    [KernelRule("K-LIN-1")]
    public async Task OrphanCheckReportsDerivedVersionsWithoutInputs()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var orphan = NodeRef.For(ConceptMappingId.New(), 1);
        await fixture.SeedAsync(new LineageNodeRecord
        {
            TenantId = seed.Tenant,
            Kind = orphan.Kind,
            NodeId = orphan.Id,
            Version = 1,
            Derived = true,
            ActivityId = Guid.CreateVersion7(),
            RecordedAt = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow),
        });

        var orphans = await fixture.LineageOrphanCheck.FindOrphansAsync(seed.Tenant, 100, ct);

        orphans.Select(o => o.Node).Should().Equal(orphan);
        await fixture.LineageOrphanCheck.CheckAllAsync(ct);
    }

    private async Task<Chain> SeedChainAsync(CancellationToken ct)
    {
        var tenant = await NewTenantAsync(ct);
        var at = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow);
        var study = NodeRef.For(StudyId.New(), 1);
        var protocol = NodeRef.For(ProtocolVersionId.New(), 1);
        var studySite = NodeRef.For(StudySiteId.New(), 1);
        var observation = ObservationId.New();
        var original = NodeRef.For(observation, 1);
        var corrected = NodeRef.For(observation, 2);
        var derived = NodeRef.For(ObservationId.New(), 1);
        await fixture.SeedAsync(
            Edge(tenant, study, protocol, LineageEdgeKind.RequiredBy, at),
            Edge(tenant, protocol, original, LineageEdgeKind.GovernedBy, at),
            Edge(tenant, studySite, original, LineageEdgeKind.CollectedBy, at),
            Edge(tenant, original, corrected, LineageEdgeKind.Supersedes, at),
            Edge(tenant, corrected, derived, LineageEdgeKind.DerivedFrom, at));
        return new Chain(tenant, at, study, protocol, studySite, original, corrected, derived);
    }

    private static LineageEdgeRecord Edge(
        TenantId tenant, NodeRef from, NodeRef to, LineageEdgeKind kind, DateTimeOffset recordedAt, DateTimeOffset? validFrom = null) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            FromKind = from.Kind,
            FromId = from.Id,
            FromVersion = from.Version!.Value,
            ToKind = to.Kind,
            ToId = to.Id,
            ToVersion = to.Version!.Value,
            Kind = kind,
            ActivityId = Guid.CreateVersion7(),
            RecordedAt = recordedAt,
            ValidFrom = validFrom,
        };

    private async Task<TenantId> NewTenantAsync(CancellationToken ct)
    {
        var id = TenantId.New();
        var result = await fixture.Sender.SendAsync(new ProvisionTenant(id, $"L{id.Value:N}"[..20], "Lineage tenant", Key(), Reason), cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return id;
    }

    private CommandExecution Execution(TenantId tenant) => new(
        new ProvisionTenant(tenant, "unused", "unused", Key(), Reason),
        nameof(MapConcept),
        Actor.Person(KernelPostgresFixture.ActorId),
        KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow),
        Guid.CreateVersion7(),
        new CommandMetadata(Guid.CreateVersion7()),
        new string('0', 64));

    private static string Ref<TId>(TId id, long version)
        where TId : struct, IEntityId<TId> => NodeRef.For(id, version).ToString();

    private static string Key() => Guid.CreateVersion7().ToString();

    private sealed record Chain(
        TenantId Tenant,
        DateTimeOffset RecordedAt,
        NodeRef Study,
        NodeRef Protocol,
        NodeRef StudySite,
        NodeRef Original,
        NodeRef Corrected,
        NodeRef Derived);
}
