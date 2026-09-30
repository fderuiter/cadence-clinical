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
    /// The path from the tenant's portfolio down to the object (for a participant: tenant, study, country, study site,
    /// participant), or null when the object does not exist.
    /// </summary>
    Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default);

    /// <summary>The object's children in the zoom hierarchy, or null when the object does not exist.</summary>
    Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default);
}
