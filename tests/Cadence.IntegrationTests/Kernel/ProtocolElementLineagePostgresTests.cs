using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Protocol design elements as lineage nodes against PostgreSQL (ADR-2474): approving a version writes its elements and
/// their references in the approval's transaction, an amendment writes only what it changes, and amendment impact
/// follows only the changed elements. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProtocolElementLineagePostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>Two objectives, each with a primary endpoint; the first endpoint has an estimand over arm A.</summary>
    private static readonly ProtocolDesign Design = ProtocolDesignSamples.MinimalDesign() with
    {
        Objectives =
        [
            new Objective("obj.1", ObjectiveLevel.Primary, "Show that the drug lowers systolic blood pressure."),
            new Objective("obj.2", ObjectiveLevel.Secondary, "Describe the drug's safety."),
        ],
        Endpoints =
        [
            new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Change in systolic blood pressure at week 12."),
            new Endpoint("ep.2", "obj.2", ObjectiveLevel.Secondary, "Adverse events to week 12."),
        ],
        Populations = [new Population("pop.itt", "Intention to treat", [])],
        Estimands =
        [
            new Estimand("est.1", "ep.1", "pop.itt", ["arm.A"], "Change in SBP at week 12", [], "Difference in means"),
        ],
    };

    private static readonly string[] ElementIds = ["arm.A", "arm.B", "ep.1", "ep.2", "est.1", "obj.1", "obj.2", "pop.itt"];

    /// <summary>
    /// Approving the first version writes each design element as a node at version 1, governed by the approved version,
    /// with the scientific chain between them, so a forward trace from an objective reaches its endpoint and estimand.
    /// </summary>
    [Fact(DisplayName = "ApprovingAVersion_WritesItsElementsAsLineageNodes")]
    public async Task ApprovingAVersionWritesItsElementsAsLineageNodes()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);

        var version = await ApproveFirstAsync(tenant, study, ct);

        await using (var db = fixture.CreateContext(tenant))
        {
            var nodes = await db.LineageNodes.AsNoTracking().Where(n => n.Kind == NodeKinds.ProtocolElement).ToListAsync(ct);
            nodes.Select(n => (n.NodeId, n.Version)).Should().BeEquivalentTo(ElementIds.Select(id => (Element(study, id).Id, 1L)));
            var approval = await db.LineageNodes.AsNoTracking()
                .Where(n => n.Kind == NodeKinds.ProtocolVersion && n.NodeId == version.Value)
                .OrderByDescending(n => n.Version)
                .FirstAsync(ct);
            nodes.Should().OnlyContain(n => n.ActivityId == approval.ActivityId, "the elements are written in the approval's transaction (K-LIN-2)");
            var governed = await db.LineageEdges.AsNoTracking()
                .Where(e => e.FromId == version.Value && e.ToKind == NodeKinds.ProtocolElement)
                .ToListAsync(ct);
            governed.Should().HaveCount(ElementIds.Length).And.OnlyContain(e => e.Kind == LineageEdgeKind.GovernedBy && e.FromVersion == approval.Version);
        }

        var trace = await fixture.Lineage.TraceAsync(
            tenant, fixture.CurrentActor.Actor!, new LineageQuery(Element(study, "obj.1").AtVersion(1), LineageDirection.Forward), ct);
        trace.Nodes.Select(n => n.Ref).Should().BeEquivalentTo(
            [Element(study, "obj.1").AtVersion(1), Element(study, "ep.1").AtVersion(1), Element(study, "est.1").AtVersion(1)]);
    }

    /// <summary>
    /// Approving an amendment that rewords one objective writes that objective at the amendment's number, superseding
    /// its earlier version and linked to the unchanged endpoint; the approved amendment governs every element.
    /// </summary>
    [Fact(DisplayName = "ApprovingAnAmendment_WritesOnlyTheElementsItChanges")]
    public async Task ApprovingAnAmendmentWritesOnlyTheElementsItChanges()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var first = await ApproveFirstAsync(tenant, study, ct);

        var amendment = await ForkAsync(tenant, first, Reworded(), ct);
        await ApproveAsync(tenant, amendment, ct);

        await using var db = fixture.CreateContext(tenant);
        var nodes = await db.LineageNodes.AsNoTracking().Where(n => n.Kind == NodeKinds.ProtocolElement).ToListAsync(ct);
        nodes.Where(n => n.Version == 2).Select(n => n.NodeId).Should().Equal(Element(study, "obj.1").Id);
        nodes.Should().HaveCount(ElementIds.Length + 1);
        var objective = Element(study, "obj.1");
        var endpoint = Element(study, "ep.1");
        (await db.LineageEdges.AsNoTracking().Where(e => e.ToId == objective.Id && e.ToVersion == 2).Select(e => new { e.FromId, e.FromVersion, e.Kind }).ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromId = objective.Id, FromVersion = 1L, Kind = LineageEdgeKind.Supersedes },
                new { FromId = amendment.Value, FromVersion = await ApprovedVersionIndexAsync(db, amendment, ct), Kind = LineageEdgeKind.GovernedBy },
            ]);
        (await db.LineageEdges.AsNoTracking().CountAsync(e => e.FromId == objective.Id && e.FromVersion == 2 && e.ToId == endpoint.Id && e.ToVersion == 1, ct))
            .Should().Be(1);
        (await db.LineageEdges.AsNoTracking().CountAsync(e => e.FromId == amendment.Value && e.ToKind == NodeKinds.ProtocolElement, ct))
            .Should().Be(ElementIds.Length);
    }

    /// <summary>
    /// K-PROT-2 by element: an amendment that rewords the first objective impacts that objective, the endpoint and
    /// estimand that depend on it, and what depends on the parent version as a whole, but not the second objective, its
    /// endpoint or what was collected for that endpoint. The report lists the elements actually changed.
    /// </summary>
    [Fact(DisplayName = "AmendmentImpact_FollowsOnlyTheChangedElements")]
    public async Task AmendmentImpactFollowsOnlyTheChangedElements()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var parent = await ApproveFirstAsync(tenant, study, ct);
        var governed = NodeRef.For(ObservationId.New(), 1);
        var safetyData = NodeRef.For(ObservationId.New(), 1);
        long approvedAt;
        await using (var db = fixture.CreateContext(tenant))
        {
            approvedAt = await ApprovedVersionIndexAsync(db, parent, ct);
        }

        await fixture.SeedAsync(
            Edge(tenant, NodeRef.For(parent, approvedAt), governed, LineageEdgeKind.GovernedBy),
            Edge(tenant, Element(study, "ep.2").AtVersion(1), safetyData, LineageEdgeKind.CollectedBy));
        var amendment = await ForkAsync(tenant, parent, Reworded(), ct);

        var impact = await fixture.AmendmentImpact.ComputeAsync(tenant, fixture.CurrentActor.Actor!, amendment, cancellationToken: ct);

        impact!.ChangedElements.Should().BeEquivalentTo([new ElementChange("obj.1", null, ElementChangeKind.Changed, ["/text"])]);
        impact.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [
                governed,
                Element(study, "obj.1").AtVersion(1),
                Element(study, "ep.1").AtVersion(1),
                Element(study, "est.1").AtVersion(1),
            ]);
    }

    /// <summary>
    /// An element belongs to its study: the authority evaluator scopes it to the study's path, and it zooms under the
    /// protocol version that last governed it, at its latest version and labelled with its element id.
    /// </summary>
    [Fact(DisplayName = "ProtocolElement_IsScopedAndZoomedUnderItsStudy")]
    public async Task ProtocolElementIsScopedAndZoomedUnderItsStudy()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var amendment = await ForkAsync(tenant, await ApproveFirstAsync(tenant, study, ct), Reworded(), ct);
        await ApproveAsync(tenant, amendment, ct);
        var objective = Element(study, "obj.1");

        (await fixture.AuthorityStore.PathOfAsync(tenant, objective, ct)).Should().Be(await fixture.AuthorityStore.PathOfAsync(tenant, NodeRef.For(study), ct));
        (await fixture.AuthorityStore.PathOfAsync(tenant, NodeRef.For(ProtocolElementId.For(StudyId.New(), "obj.1")), ct)).Should().Be(ScopePath.TenantWide);

        var navigator = new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts));
        (await navigator.ResolveAsync(tenant, objective, ct)).Should().Be(new NodeSummary(objective.AtVersion(2), "Protocol element obj.1", "obj.1"));
        var path = await navigator.GetZoomPathAsync(tenant, objective, ct);
        path!.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, NodeKinds.ProtocolVersion, NodeKinds.ProtocolElement);
        path![2].Ref!.Unversioned().Should().Be(NodeRef.For(amendment));
    }

    private static string Reworded() => ProtocolDesignDocument.Write(Design with
    {
        Objectives = [Design.Objectives[0] with { Text = "Show that the drug lowers blood pressure." }, Design.Objectives[1]],
    });

    private static NodeRef Element(StudyId study, string element) => NodeRef.For(ProtocolElementId.For(study, element));

    private async Task<ProtocolVersionId> ApproveFirstAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, id, ct, ProtocolDesignDocument.Write(Design))).IsSuccess.Should().BeTrue();
        await ApproveAsync(tenant, id, ct);
        return id;
    }

    private async Task<ProtocolVersionId> ForkAsync(TenantId tenant, ProtocolVersionId parent, string content, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent, id, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment"), ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, id, content, null, KernelProtocolScenario.Key(), "Reword the objective"), ct);
        return id;
    }

    private async Task ApproveAsync(TenantId tenant, ProtocolVersionId id, CancellationToken ct)
    {
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready"), ct);
        var attestation = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.OkAsync(new ApproveProtocolVersion(tenant, id, attestation, KernelProtocolScenario.Key(), "Approved"), ct);
    }

    private static Task<long> ApprovedVersionIndexAsync(KernelDbContext db, ProtocolVersionId id, CancellationToken ct) =>
        db.ProtocolVersions.AsNoTracking().Where(v => v.Id == id).Select(v => v.VersionIndex).SingleAsync(ct);

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
}
