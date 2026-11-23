using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Navigation;

/// <summary>
/// What an inspector, palette row or breadcrumb shows for one kernel object (ADR-2481, Canvas PX-2): its reference,
/// a human title, and its human-facing code and state when it has them.
/// </summary>
/// <param name="Ref">The object, pinned to the version that was read.</param>
/// <param name="Title">Display title (for example a study's title or a site's name).</param>
/// <param name="Code">Human-facing code (<c>StudyCode</c>, <c>SiteCode</c>, <c>SubjectCode</c>), when the kind has one.</param>
/// <param name="Status">Lifecycle state as the kernel records it, when the kind has one.</param>
public sealed record NodeSummary(NodeRef Ref, string Title, string? Code = null, string? Status = null);

/// <summary>
/// One level of a zoom path. Most levels are objects; a grouping level (for example a country between a study and
/// its study sites) has a label but no <see cref="Ref"/>.
/// </summary>
/// <param name="Level">The level's kind (<see cref="NodeKinds"/>) or grouping name (<see cref="ZoomLevels"/>).</param>
/// <param name="Label">What the breadcrumb shows.</param>
/// <param name="Ref">The object at this level, or null for a grouping level.</param>
public sealed record ZoomStep(string Level, string Label, NodeRef? Ref);

/// <summary>A child of a scope node, with the number of its own children when that is known.</summary>
/// <param name="Node">The child.</param>
/// <param name="ChildCount">How many children the child has, or null when that level is not queryable yet.</param>
/// <param name="Group">The grouping level the child sits under (for example its country), or null.</param>
public sealed record ScopeChild(NodeSummary Node, int? ChildCount, string? Group = null);

/// <summary>An enrollment in a study, with its subject code and responsible study site when assigned.</summary>
/// <param name="EnrollmentId">The enrollment.</param>
/// <param name="SubjectCode">The participant's human-facing subject code in the study.</param>
/// <param name="StudySiteId">The responsible study site, or null when the enrollment sits directly under the study.</param>
/// <param name="SiteCode">The human-facing code of the responsible site, when assigned.</param>
public sealed record StudyEnrollmentNode(
    EnrollmentId EnrollmentId,
    string SubjectCode,
    StudySiteId? StudySiteId = null,
    string? SiteCode = null);

/// <summary>Names of zoom levels that group objects without being objects themselves.</summary>
public static class ZoomLevels
{
    /// <summary>A study's sites grouped by the country of the site (ISO 3166-1 code as recorded on the site).</summary>
    public const string Country = "country";
}

/// <summary>
/// Kernel navigation (ADR-2481, Canvas PX-1 and PX-2): resolve a <see cref="NodeRef"/> to its summary, compute its
/// presentation zoom path, and list the children of a scope node with counts. The zoom path is presentation only:
/// it never grants or implies authority (spec §9.3), and a participant in a decentralised trial with no responsible
/// study site resolves directly under its study.
/// </summary>
public interface IKernelNavigator
{
    /// <summary>The object's summary, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="nodes"/> in one batch, keyed by unversioned <see cref="NodeRef"/>; unknown objects are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<NodeRef, NodeSummary>> ResolveManyAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var resolved = new Dictionary<NodeRef, NodeSummary>();
        foreach (var node in nodes)
        {
            var key = node.Unversioned();
            if (resolved.ContainsKey(key))
            {
                continue;
            }

            if (await ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is { } summary)
            {
                resolved[key] = summary;
            }
        }

        return resolved;
    }

    /// <summary>
    /// The path from the tenant's portfolio down to the object (for a participant: tenant, study, country, study site,
    /// participant), or null when the object does not exist.
    /// </summary>
    Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default);

    /// <summary>The object's children in the zoom hierarchy, or null when the object does not exist.</summary>
    Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the governing <see cref="EnrollmentId"/> for any <c>collection_instance</c> or <c>observation</c> in
    /// <paramref name="nodes"/> so read endpoints inherit enrollment scope visibility (ADR-2457).
    /// </summary>
    Task<IReadOnlyDictionary<NodeRef, EnrollmentId>> EnrollmentsOfAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<NodeRef, EnrollmentId>>(new Dictionary<NodeRef, EnrollmentId>());

    /// <summary>
    /// Returns the governing <see cref="EnrollmentId"/> for any observation in <paramref name="nodes"/> that records a
    /// blinded treatment or intervention concept (K-AUTH-4), so read endpoints can enforce <c>enrollment.unblind</c>.
    /// </summary>
    Task<IReadOnlyDictionary<NodeRef, EnrollmentId>> BlindedObservationsAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<NodeRef, EnrollmentId>>(new Dictionary<NodeRef, EnrollmentId>());

    /// <summary>
    /// Every enrollment in <paramref name="study"/> with its site code and subject code, or null when the study does not
    /// exist in <paramref name="tenant"/>.
    /// </summary>
    async Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken = default)
    {
        var studyNode = NodeRef.For(study);
        if (await ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var enrollments = new List<StudyEnrollmentNode>();
        var studyChildren = await GetChildrenAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) ?? [];
        foreach (var node in studyChildren.Select(child => child.Node))
        {
            if (node.Ref.Kind == NodeKinds.StudySite)
            {
                var siteId = StudySiteId.FromGuid(node.Ref.Id);
                var siteCode = node.Code ?? node.Title;
                var siteChildren = await GetChildrenAsync(tenant, node.Ref, cancellationToken).ConfigureAwait(false) ?? [];
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
