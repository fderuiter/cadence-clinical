using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Navigation;

/// <summary>One object in the zoom hierarchy, as kernel persistence reads it.</summary>
/// <param name="Summary">The object's summary.</param>
/// <param name="ZoomParent">The object one level up (see <see cref="ZoomRules"/>), or null for the tenant root.</param>
/// <param name="Group">The grouping label between the parent and this object (a study site's country), or null.</param>
public sealed record ScopeNode(NodeSummary Summary, NodeRef? ZoomParent, string? Group = null);

/// <summary>
/// Read port over the kernel's scope hierarchy, implemented by kernel persistence. Reads are tenant-bound (K-ID-2)
/// and never write.
/// </summary>
public interface IKernelScopeGraph
{
    /// <summary>The object, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<ScopeNode?> FindAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves <paramref name="nodes"/> in one batch, keyed by unversioned <see cref="NodeRef"/>; unknown objects are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<NodeRef, ScopeNode>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var found = new Dictionary<NodeRef, ScopeNode>();
        foreach (var node in nodes)
        {
            var key = node.Unversioned();
            if (found.ContainsKey(key))
            {
                continue;
            }

            if (await FindAsync(tenant, node, cancellationToken).ConfigureAwait(false) is { } scopeNode)
            {
                found[key] = scopeNode;
            }
        }

        return found;
    }

    /// <summary>The object's zoom children with their own child counts. Empty for a leaf kind.</summary>
    Task<IReadOnlyList<ScopeChild>> ChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken);

    /// <summary>
    /// Every enrollment in <paramref name="study"/> with its site code and subject code, or null when the study does not
    /// exist in <paramref name="tenant"/>.
    /// </summary>
    async Task<IReadOnlyList<StudyEnrollmentNode>?> StudyEnrollmentsAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        var studyNode = NodeRef.For(study);
        if (await FindAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var enrollments = new List<StudyEnrollmentNode>();
        var studyChildren = await ChildrenAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false);
        foreach (var node in studyChildren.Select(child => child.Node))
        {
            if (node.Ref.Kind == NodeKinds.StudySite)
            {
                var siteId = StudySiteId.FromGuid(node.Ref.Id);
                var siteCode = node.Code ?? node.Title;
                var siteChildren = await ChildrenAsync(tenant, node.Ref.Unversioned(), cancellationToken).ConfigureAwait(false);
                foreach (var memberNode in siteChildren.Select(member => member.Node).Where(m => m.Ref.Kind == NodeKinds.Enrollment))
                {
                    enrollments.Add(new StudyEnrollmentNode(
                        EnrollmentId.FromGuid(memberNode.Ref.Id),
                        memberNode.Code ?? memberNode.Title,
                        siteId,
                        siteCode));
                }
            }
            else if (node.Ref.Kind == NodeKinds.Enrollment)
            {
                enrollments.Add(new StudyEnrollmentNode(
                    EnrollmentId.FromGuid(node.Ref.Id),
                    node.Code ?? node.Title));
            }
        }

        return enrollments;
    }
}

/// <summary>
/// The presentation hierarchy the zoom path follows (ADR-2481 §4.3):
/// <c>tenant → study → [country] → study_site → enrollment</c>, and <c>tenant → organization → site</c>. Protocol
/// versions, study standards, validation runs and findings sit under their study (ADR-2462), and an attestation under
/// the object it signs (ADR-2485).
/// It is presentation only and never implies authority.
/// </summary>
public static class ZoomRules
{
    /// <summary>
    /// A participant appears in a study as its enrollment (ADR-2453), which sits under its responsible study site, or
    /// directly under its study when it has none (a decentralised trial, ICH E6(R3) Annex 2). Activity is never
    /// assumed to happen at a site.
    /// </summary>
    public static NodeRef EnrollmentParent(StudyId study, StudySiteId? responsibleStudySite) =>
        responsibleStudySite is { IsEmpty: false } site ? NodeRef.For(site) : NodeRef.For(study);
}

/// <summary>
/// Computes summaries, zoom paths and children for <see cref="IKernelNavigator"/> from <see cref="IKernelScopeGraph"/>.
/// </summary>
public sealed class KernelNavigator(IKernelScopeGraph graph) : IKernelNavigator
{
    /// <summary>Deeper than any hierarchy the kernel defines; reaching it means the hierarchy has a cycle.</summary>
    internal const int MaxDepth = 16;

    /// <inheritdoc />
    public async Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return (await graph.FindAsync(tenant, node, cancellationToken).ConfigureAwait(false))?.Summary;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<NodeRef, NodeSummary>> ResolveManyAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var found = await graph.FindManyAsync(tenant, nodes, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(pair => pair.Key, pair => pair.Value.Summary);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (await graph.FindAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } current)
        {
            return null;
        }

        var reversed = new List<ZoomStep>();
        var depth = 1;
        while (true)
        {
            var summary = current.Summary;
            reversed.Add(new ZoomStep(summary.Ref.Kind, summary.Code ?? summary.Title, summary.Ref));
            if (current.Group is { } group)
            {
                reversed.Add(new ZoomStep(ZoomLevels.Country, group, Ref: null));
            }

            if (current.ZoomParent is not { } parent)
            {
                reversed.Reverse();
                return reversed;
            }

            if (++depth > MaxDepth)
            {
                throw new InvalidOperationException($"The zoom hierarchy above {node} does not end at a tenant within {MaxDepth} levels.");
            }

            current = await graph.FindAsync(tenant, parent, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"{summary.Ref} names parent {parent}, which does not exist.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (await graph.FindAsync(tenant, node, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return await graph.ChildrenAsync(tenant, node.Unversioned(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken = default) =>
        graph.StudyEnrollmentsAsync(tenant, study, cancellationToken);
}

/// <summary>Registers kernel navigation.</summary>
public static class KernelNavigationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKernelNavigator"/>. Kernel persistence provides <see cref="IKernelScopeGraph"/>.
    /// </summary>
    public static IServiceCollection AddKernelNavigation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKernelNavigator, KernelNavigator>();
        return services;
    }
}
