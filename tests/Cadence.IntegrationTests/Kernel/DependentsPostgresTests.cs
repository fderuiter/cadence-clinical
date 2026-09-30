using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// <c>Dependents(node)</c> (Canvas D2, #4841) and amendment impact (K-PROT-2) as its caller, against PostgreSQL as the
/// application role. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DependentsPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private static readonly Actor Reader = Actor.Person("reader@cadence.test");

    private readonly KernelProtocolScenario _scenario = new(fixture);

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.Visibility.Masked.Clear();
        }
    }

    /// <summary>
    /// Dependents are grouped by kind in ordinal order and counted as objects: two versions of one observation count
    /// once, listed at the latest version reached and at the nearest distance. The start is not its own dependent,
    /// and an object that is only upstream of a dependent (the study site) is not one.
    /// </summary>
    [Fact(DisplayName = "Dependents_GroupsByKind_CountingObjectsNotVersions")]
    public async Task DependentsGroupsByKindCountingObjectsNotVersions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);

        var report = await fixture.Lineage.DependentsAsync(chain.Tenant, Reader, new DependentsQuery(chain.Study.Unversioned()), ct);

        report.Start.Should().Be(chain.Study.Unversioned().ToString());
        report.Total.Should().Be(3);
        report.Truncated.Should().BeFalse();
        report.Groups.Select(g => (g.Kind, g.Count)).Should().Equal((NodeKinds.Observation, 2), (NodeKinds.ProtocolVersion, 1));
        report.Groups[0].Objects.Select(o => (o.Ref, o.Distance)).Should().Equal((chain.Corrected, 2), (chain.Derived, 4));
        report.Groups[1].Objects.Should().ContainSingle().Which.Ref.Should().Be(chain.Protocol);
    }

    /// <summary>
    /// A masked dependent is counted and listed masked (D1): the counts are the same whoever reads, and nothing in the
    /// masked entry names the object.
    /// </summary>
    [Fact(DisplayName = "Dependents_MaskedObject_IsCountedAndMasked")]
    public async Task DependentsMaskedObjectIsCountedAndMasked()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);
        fixture.Visibility.Masked[chain.Original.Unversioned()] = MaskReason.Blinded;

        var report = await fixture.Lineage.DependentsAsync(chain.Tenant, Reader, new DependentsQuery(chain.Study.Unversioned()), ct);

        report.Total.Should().Be(3);
        var observations = report.Groups.Single(g => g.Kind == NodeKinds.Observation);
        observations.Count.Should().Be(2);
        observations.Objects[0].Should().BeEquivalentTo(new { Handle = "masked:1", Ref = (NodeRef?)null, Mask = MaskReason.Blinded, Distance = 2 });
        observations.Objects[1].Ref.Should().Be(chain.Derived);
        report.Groups.SelectMany(g => g.Objects).Select(o => o.Handle)
            .Should().NotContain(h => h.Contains(chain.Original.Id.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// A versioned start leaves out every version of itself (the correction of an observation is not its dependent) and
    /// keeps what was produced from it.
    /// </summary>
    [Fact(DisplayName = "Dependents_VersionedStart_ExcludesItsOwnLaterVersions")]
    public async Task DependentsVersionedStartExcludesItsOwnLaterVersions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);

        var report = await fixture.Lineage.DependentsAsync(chain.Tenant, Reader, new DependentsQuery(chain.Original), ct);

        report.Total.Should().Be(1);
        report.Groups.Should().ContainSingle().Which.Objects.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Ref = chain.Derived, Distance = 2 });
    }

    /// <summary>Depth and edge kinds limit the walk; a depth that cuts the graph off reports it as truncated.</summary>
    [Fact(DisplayName = "Dependents_DepthAndEdgeKinds_LimitTheWalk")]
    public async Task DependentsDepthAndEdgeKindsLimitTheWalk()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var chain = await SeedChainAsync(ct);

        var shallow = await fixture.Lineage.DependentsAsync(chain.Tenant, Reader, new DependentsQuery(chain.Study, Depth: 1), ct);
        var requiredOnly = await fixture.Lineage.DependentsAsync(
            chain.Tenant, Reader, new DependentsQuery(chain.Study, EdgeKinds: new HashSet<LineageEdgeKind> { LineageEdgeKind.RequiredBy }), ct);

        shallow.Total.Should().Be(1);
        shallow.Truncated.Should().BeTrue();
        requiredOnly.Total.Should().Be(1);
        requiredOnly.Truncated.Should().BeFalse();
        requiredOnly.Groups.Should().ContainSingle().Which.Kind.Should().Be(NodeKinds.ProtocolVersion);
    }

    /// <summary>
    /// K-PROT-2 through the pipeline: forking records the fork as derived from the approved parent, and the amendment's
    /// impact is the parent's dependents without the fork itself. Another open amendment of the same parent is listed.
    /// </summary>
    [Fact(DisplayName = "AmendmentImpact_IsTheParentsDependents_WithoutTheAmendedFork")]
    public async Task AmendmentImpactIsTheParentsDependentsWithoutTheAmendedFork()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (parent, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var parentVersion = await ParentVersionAsync(tenant, parent.ProtocolVersionId, ct);
        var observation = NodeRef.For(ObservationId.New(), 1);
        await fixture.SeedAsync(Edge(tenant, parentVersion, observation, LineageEdgeKind.GovernedBy));

        var forkId = ProtocolVersionId.New();
        var otherId = ProtocolVersionId.New();
        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent.ProtocolVersionId, forkId, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment 1"), ct);
        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent.ProtocolVersionId, otherId, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment 1b"), ct);

        await using (var db = fixture.CreateContext(tenant))
        {
            var edge = await db.LineageEdges.AsNoTracking().SingleAsync(e => e.ToId == forkId.Value, ct);
            (edge.FromId, edge.FromVersion, edge.Kind).Should().Be((parent.ProtocolVersionId.Value, parentVersion.Version!.Value, LineageEdgeKind.DerivedFrom));
        }

        var impact = await fixture.AmendmentImpact.ComputeAsync(tenant, Reader, forkId, cancellationToken: ct);

        impact.Should().NotBeNull();
        impact!.Parent.Should().Be(parent.ProtocolVersionId);
        impact.ImpactedElements.Should().Equal(KernelProtocolScenario.Amendment.ImpactedElements);
        impact.Dependents.Start.Should().Be(NodeRef.For(parent.ProtocolVersionId).ToString());
        impact.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref)
            .Should().BeEquivalentTo([observation, NodeRef.For(otherId, 1)]);
    }

    /// <summary>A first version amends nothing, and a version the tenant does not have has no impact.</summary>
    [Fact(DisplayName = "AmendmentImpact_FirstOrUnknownVersion_IsRefusedOrNull")]
    public async Task AmendmentImpactFirstOrUnknownVersionIsRefusedOrNull()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (first, _) = await _scenario.ApprovedAsync(tenant, study, ct);

        var act = () => fixture.AmendmentImpact.ComputeAsync(tenant, Reader, first.ProtocolVersionId, cancellationToken: ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.AmendmentImpact.ComputeAsync(tenant, Reader, ProtocolVersionId.New(), cancellationToken: ct)).Should().BeNull();
    }

    private async Task<NodeRef> ParentVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var version = await db.ProtocolVersions.AsNoTracking().Where(v => v.Id == id).Select(v => v.VersionIndex).SingleAsync(ct);
        return NodeRef.For(id, version);
    }

    private async Task<Chain> SeedChainAsync(CancellationToken ct)
    {
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        var study = NodeRef.For(StudyId.New(), 1);
        var protocol = NodeRef.For(ProtocolVersionId.New(), 1);
        var studySite = NodeRef.For(StudySiteId.New(), 1);
        var observation = ObservationId.New();
        var original = NodeRef.For(observation, 1);
        var corrected = NodeRef.For(observation, 2);
        var derived = NodeRef.For(ObservationId.New(), 1);
        await fixture.SeedAsync(
            Edge(tenant, study, protocol, LineageEdgeKind.RequiredBy),
            Edge(tenant, protocol, original, LineageEdgeKind.GovernedBy),
            Edge(tenant, studySite, original, LineageEdgeKind.CollectedBy),
            Edge(tenant, original, corrected, LineageEdgeKind.Supersedes),
            Edge(tenant, corrected, derived, LineageEdgeKind.DerivedFrom));
        return new Chain(tenant, study, protocol, original, corrected, derived);
    }

    private LineageEdgeRecord Edge(TenantId tenant, NodeRef from, NodeRef to, LineageEdgeKind kind) => new()
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
        RecordedAt = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow),
    };

    private sealed record Chain(TenantId Tenant, NodeRef Study, NodeRef Protocol, NodeRef Original, NodeRef Corrected, NodeRef Derived);
}
