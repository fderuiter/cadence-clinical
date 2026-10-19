using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The kernel-computed zoom path and children (ADR-2481 §4.3, Canvas PX-1): portfolio → study → country → study
/// site → participant, with a site-less participant directly under its study.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelNavigatorTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly NodeRef TenantNode = NodeRef.For(Tenant);
    private static readonly StudyId Study = StudyId.New();
    private static readonly NodeRef StudyNode = NodeRef.For(Study);
    private static readonly StudySiteId StudySite = StudySiteId.New();
    private static readonly NodeRef StudySiteNode = NodeRef.For(StudySite);

    private readonly FakeScopeGraph _graph = new();

    public KernelNavigatorTests()
    {
        _graph.Add(new ScopeNode(new NodeSummary(TenantNode, "Acme Pharma", "ACME"), ZoomParent: null));
        _graph.Add(new ScopeNode(new NodeSummary(StudyNode, "A Phase 3 Study", "ABC-301", "Active"), TenantNode));
        _graph.Add(new ScopeNode(new NodeSummary(StudySiteNode, "Charité Berlin", "DE-017", "Active"), StudyNode, Group: "DE"));
    }

    /// <summary>
    /// A participant's enrollment at a study site zooms portfolio → study → country → study site → enrollment, and each object
    /// level carries its NodeRef.
    /// </summary>
    [Fact(DisplayName = "ZoomPath_EnrollmentAtSite")]
    [KernelRule("K-ID-7")]
    public async Task ZoomPathEnrollmentAtSite()
    {
        var enrollment = NodeRef.For(EnrollmentId.New(), version: 4);
        _graph.Add(new ScopeNode(new NodeSummary(enrollment, "Enrollment", "017-004"), ZoomRules.EnrollmentParent(Study, StudySite)));

        var path = await new KernelNavigator(_graph).GetZoomPathAsync(Tenant, enrollment, TestContext.Current.CancellationToken);

        path.Should().NotBeNull();
        path!.Select(s => (s.Level, s.Label)).Should().Equal(
            (NodeKinds.Tenant, "ACME"),
            (NodeKinds.Study, "ABC-301"),
            (ZoomLevels.Country, "DE"),
            (NodeKinds.StudySite, "DE-017"),
            (NodeKinds.Enrollment, "017-004"));
        path.Select(s => s.Ref).Should().Equal(TenantNode, StudyNode, null, StudySiteNode, enrollment);
    }

    /// <summary>
    /// An enrollment with no responsible study site (a decentralised trial) resolves directly under its study,
    /// without error and without a country level.
    /// </summary>
    [Fact(DisplayName = "ZoomPath_SitelessEnrollmentResolvesUnderStudy")]
    [KernelRule("K-ID-7")]
    public async Task ZoomPathSitelessEnrollmentResolvesUnderStudy()
    {
        var enrollment = NodeRef.For(EnrollmentId.New());
        ZoomRules.EnrollmentParent(Study, responsibleStudySite: null).Should().Be(StudyNode);
        ZoomRules.EnrollmentParent(Study, StudySiteId.Empty).Should().Be(StudyNode);
        _graph.Add(new ScopeNode(new NodeSummary(enrollment, "Enrollment", "DCT-001"), ZoomRules.EnrollmentParent(Study, null)));

        var path = await new KernelNavigator(_graph).GetZoomPathAsync(Tenant, enrollment, TestContext.Current.CancellationToken);

        path!.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, NodeKinds.Enrollment);
    }

    /// <summary>A missing object has no summary, path or children; the navigator never invents one.</summary>
    [Fact(DisplayName = "Navigator_UnknownNodeResolvesToNull")]
    public async Task NavigatorUnknownNodeResolvesToNull()
    {
        var navigator = new KernelNavigator(_graph);
        var missing = NodeRef.For(StudyId.New());
        var ct = TestContext.Current.CancellationToken;

        (await navigator.ResolveAsync(Tenant, missing, ct)).Should().BeNull();
        (await navigator.GetZoomPathAsync(Tenant, missing, ct)).Should().BeNull();
        (await navigator.GetChildrenAsync(Tenant, missing, ct)).Should().BeNull();
    }

    /// <summary>Children are asked for by the unversioned reference and returned with their counts.</summary>
    [Fact(DisplayName = "Navigator_ChildrenOfStudy")]
    public async Task NavigatorChildrenOfStudy()
    {
        var child = new ScopeChild(new NodeSummary(StudySiteNode, "Charité Berlin", "DE-017"), ChildCount: 12, Group: "DE");
        _graph.Children[StudyNode] = [child];

        var children = await new KernelNavigator(_graph).GetChildrenAsync(Tenant, StudyNode.AtVersion(3), TestContext.Current.CancellationToken);

        children.Should().Equal(child);
    }

    /// <summary>A hierarchy that points at a missing parent or loops is reported, not silently cut short.</summary>
    [Fact(DisplayName = "ZoomPath_BrokenHierarchyThrows")]
    public async Task ZoomPathBrokenHierarchyThrows()
    {
        var orphan = NodeRef.For(StudySiteId.New());
        _graph.Add(new ScopeNode(new NodeSummary(orphan, "Orphan"), NodeRef.For(StudyId.New())));
        var loopA = NodeRef.For(StudyId.New());
        var loopB = NodeRef.For(StudyId.New());
        _graph.Add(new ScopeNode(new NodeSummary(loopA, "A"), loopB));
        _graph.Add(new ScopeNode(new NodeSummary(loopB, "B"), loopA));
        var navigator = new KernelNavigator(_graph);
        var ct = TestContext.Current.CancellationToken;

        await FluentActions.Awaiting(() => navigator.GetZoomPathAsync(Tenant, orphan, ct)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => navigator.GetZoomPathAsync(Tenant, loopA, ct)).Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeScopeGraph : IKernelScopeGraph
    {
        private readonly Dictionary<NodeRef, ScopeNode> _nodes = [];

        public Dictionary<NodeRef, IReadOnlyList<ScopeChild>> Children { get; } = [];

        public void Add(ScopeNode node) => _nodes[node.Summary.Ref.Unversioned()] = node;

        public Task<ScopeNode?> FindAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant ? _nodes.GetValueOrDefault(node.Unversioned()) : null);

        public Task<IReadOnlyList<ScopeChild>> ChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult(Children.GetValueOrDefault(node) ?? []);
    }
}
