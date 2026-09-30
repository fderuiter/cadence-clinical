using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// Reads the lineage graph with one recursive CTE per walk (ADR-2255, ADR-2471), through a context bound to the
/// tenant, so Row-Level Security limits every row to that tenant (K-ID-2). The walk is bounded by depth and by
/// <see cref="MaxEdges"/>; either limit sets <see cref="LineageGraph.Truncated"/>. Visibility is decided once per
/// read by the host's <see cref="INodeVisibility"/>, and unreadable objects are masked, never dropped.
/// </summary>
internal sealed class KernelLineageStore(
    KernelTenantDbContextFactory contexts,
    INodeVisibility visibility,
    IClock clock) : IKernelLineage
{
    /// <summary>Edges one walk returns at most; a larger graph comes back truncated.</summary>
    internal const int MaxEdges = 10_000;

    // The walk expands one edge further than asked (depth + 1) so that it can tell a graph that ends at the depth
    // limit from one that continues past it. Rows past the limit are only counted, never returned.
    private const string BackwardSql = """
        WITH RECURSIVE walk (edge_id, kind, node_id, version, depth) AS (
            SELECT e.id, e.from_kind, e.from_id, e.from_version, 1
            FROM kernel.lineage_edges e
            WHERE e.tenant_id = @tenant AND e.to_id = @node AND e.to_kind = @kind
              AND (@version IS NULL OR e.to_version = @version)
              AND e.recorded_at <= @known_at AND (e.valid_from IS NULL OR e.valid_from <= @valid_at)
              AND (@kinds IS NULL OR e.kind = ANY (@kinds))
            UNION
            SELECT e.id, e.from_kind, e.from_id, e.from_version, w.depth + 1
            FROM walk w
            JOIN kernel.lineage_edges e
              ON e.tenant_id = @tenant AND e.to_id = w.node_id AND e.to_kind = w.kind AND e.to_version = w.version
            WHERE w.depth <= @depth
              AND e.recorded_at <= @known_at AND (e.valid_from IS NULL OR e.valid_from <= @valid_at)
              AND (@kinds IS NULL OR e.kind = ANY (@kinds))
        ),
        reached AS (SELECT edge_id, min(depth) AS depth FROM walk GROUP BY edge_id)
        SELECT e.id AS "Id", e.from_kind AS "FromKind", e.from_id AS "FromId", e.from_version AS "FromVersion",
               e.to_kind AS "ToKind", e.to_id AS "ToId", e.to_version AS "ToVersion", e.kind AS "Kind",
               e.activity_id AS "ActivityId", e.recorded_at AS "RecordedAt", e.valid_from AS "ValidFrom", r.depth AS "Depth"
        FROM reached r JOIN kernel.lineage_edges e ON e.id = r.edge_id
        ORDER BY r.depth, e.recorded_at, e.id
        LIMIT @limit
        """;

    private const string ForwardSql = """
        WITH RECURSIVE walk (edge_id, kind, node_id, version, depth) AS (
            SELECT e.id, e.to_kind, e.to_id, e.to_version, 1
            FROM kernel.lineage_edges e
            WHERE e.tenant_id = @tenant AND e.from_id = @node AND e.from_kind = @kind
              AND (@version IS NULL OR e.from_version = @version)
              AND e.recorded_at <= @known_at AND (e.valid_from IS NULL OR e.valid_from <= @valid_at)
              AND (@kinds IS NULL OR e.kind = ANY (@kinds))
            UNION
            SELECT e.id, e.to_kind, e.to_id, e.to_version, w.depth + 1
            FROM walk w
            JOIN kernel.lineage_edges e
              ON e.tenant_id = @tenant AND e.from_id = w.node_id AND e.from_kind = w.kind AND e.from_version = w.version
            WHERE w.depth <= @depth
              AND e.recorded_at <= @known_at AND (e.valid_from IS NULL OR e.valid_from <= @valid_at)
              AND (@kinds IS NULL OR e.kind = ANY (@kinds))
        ),
        reached AS (SELECT edge_id, min(depth) AS depth FROM walk GROUP BY edge_id)
        SELECT e.id AS "Id", e.from_kind AS "FromKind", e.from_id AS "FromId", e.from_version AS "FromVersion",
               e.to_kind AS "ToKind", e.to_id AS "ToId", e.to_version AS "ToVersion", e.kind AS "Kind",
               e.activity_id AS "ActivityId", e.recorded_at AS "RecordedAt", e.valid_from AS "ValidFrom", r.depth AS "Depth"
        FROM reached r JOIN kernel.lineage_edges e ON e.id = r.edge_id
        ORDER BY r.depth, e.recorded_at, e.id
        LIMIT @limit
        """;

    public async Task<LineageGraph> TraceAsync(TenantId tenant, Actor reader, LineageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(query);
        var (validAt, knownAt) = ResolveTimes(query.ValidAt, query.KnownAt);

        await using var db = contexts.Create(tenant);
        var walk = await WalkAsync(db, tenant, query, validAt, knownAt, cancellationToken).ConfigureAwait(false);
        var masks = await MaskAsync(tenant, reader, [walk], cancellationToken).ConfigureAwait(false);
        return Build(walk, query, validAt, knownAt, new HandleBook(masks));
    }

    public async Task<LineageDossier> DossierAsync(
        TenantId tenant,
        Actor reader,
        NodeRef node,
        int depth = LineageQuery.DefaultDepth,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var (valid, known) = ResolveTimes(validAt, knownAt);
        var backwardQuery = new LineageQuery(node, LineageDirection.Backward, depth, ValidAt: valid, KnownAt: known);
        var forwardQuery = backwardQuery with { Direction = LineageDirection.Forward };

        await using var db = contexts.Create(tenant);
        var backwardWalk = await WalkAsync(db, tenant, backwardQuery, valid, known, cancellationToken).ConfigureAwait(false);
        var forwardWalk = await WalkAsync(db, tenant, forwardQuery, valid, known, cancellationToken).ConfigureAwait(false);

        // One handle book for both walks, so a masked object has the same handle on both sides of the dossier.
        var masks = await MaskAsync(tenant, reader, [backwardWalk, forwardWalk], cancellationToken).ConfigureAwait(false);
        var handles = new HandleBook(masks);
        var backward = Build(backwardWalk, backwardQuery, valid, known, handles);
        var forward = Build(forwardWalk, forwardQuery, valid, known, handles);

        var byKind = backward.Nodes.Concat(forward.Nodes)
            .DistinctBy(n => n.Handle, StringComparer.Ordinal)
            .GroupBy(n => n.Kind, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LineageNode>)[.. g], StringComparer.Ordinal);
        return new LineageDossier(backward, forward, byKind);
    }

    public Task<DependentsReport> DependentsAsync(TenantId tenant, Actor reader, DependentsQuery query, CancellationToken cancellationToken = default) =>
        DependentsAsync(tenant, reader, query, excluded: FrozenSet<NodeRef>.Empty, followFromStart: null, cancellationToken);

    /// <summary>
    /// Dependents of <see cref="DependentsQuery.Node"/>, not walking through any version of an object in
    /// <paramref name="excluded"/>: those objects and whatever is reachable only through them are left out. When
    /// <paramref name="followFromStart"/> is set, an edge leaving the start object is followed only when it returns true
    /// for the edge's downstream object; edges further on are all followed.
    /// </summary>
    internal async Task<DependentsReport> DependentsAsync(
        TenantId tenant,
        Actor reader,
        DependentsQuery query,
        IReadOnlySet<NodeRef> excluded,
        Func<NodeRef, bool>? followFromStart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(excluded);
        var (validAt, knownAt) = ResolveTimes(query.ValidAt, query.KnownAt);
        var lineageQuery = new LineageQuery(query.Node, LineageDirection.Forward, query.Depth, query.EdgeKinds, validAt, knownAt);

        Walk walk;
        await using (var db = contexts.Create(tenant))
        {
            walk = await WalkAsync(db, tenant, lineageQuery, validAt, knownAt, cancellationToken).ConfigureAwait(false);
        }

        var start = query.Node.Unversioned();
        var reached = Reach(walk, excluded, followFromStart);
        var masks = await MaskAsync(tenant, reader, [walk], cancellationToken).ConfigureAwait(false);
        var handles = new HandleBook(masks);
        var startHandle = StartHandle(query.Node, walk, handles);

        // One entry per dependent object: its latest version reached and its nearest distance, in walk order so masked
        // handles are numbered by position.
        var objects = reached
            .Where(pair => pair.Key.Unversioned() != start)
            .GroupBy(pair => pair.Key.Unversioned())
            .Select(g => (
                Latest: g.MaxBy(pair => pair.Key.Version)!.Key,
                Distance: g.Min(pair => pair.Value.Distance),
                Order: g.Min(pair => pair.Value.Order)))
            .OrderBy(o => o.Distance)
            .ThenBy(o => o.Order)
            .Select(o => handles.Node(o.Latest, o.Distance))
            .ToList();
        var groups = objects
            .GroupBy(n => n.Kind, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new DependentGroup(g.Key, g.Count(), [.. g]))
            .ToList();
        return new DependentsReport(startHandle, query.Depth, validAt, knownAt, groups, objects.Count, walk.Truncated);
    }

    /// <summary>
    /// Every node a forward walk reaches from its start without passing through an excluded object, with its fewest
    /// edges from the start and the index of the first edge that reached it. Edges leaving the start object are followed
    /// only when <paramref name="followFromStart"/> (when set) accepts their downstream object.
    /// </summary>
    private static Dictionary<NodeRef, (int Distance, int Order)> Reach(Walk walk, IReadOnlySet<NodeRef> excluded, Func<NodeRef, bool>? followFromStart)
    {
        var start = walk.Query.Node.Unversioned();
        var outgoing = walk.Rows
            .Select((row, index) => (row, index))
            .ToLookup(pair => pair.row.From);
        var reached = new Dictionary<NodeRef, (int Distance, int Order)>();
        var frontier = new Queue<NodeRef>();
        foreach (var origin in walk.StartNodes())
        {
            reached[origin] = (0, -1);
            frontier.Enqueue(origin);
        }

        while (frontier.TryDequeue(out var node))
        {
            var distance = reached[node].Distance;
            foreach (var (row, index) in outgoing[node])
            {
                if (excluded.Contains(row.To.Unversioned()) || reached.ContainsKey(row.To)
                    || (followFromStart is not null && node.Unversioned() == start && !followFromStart(row.To)))
                {
                    continue;
                }

                reached[row.To] = (distance + 1, index);
                frontier.Enqueue(row.To);
            }
        }

        return reached;
    }

    /// <summary>
    /// The start's handle: the pinned reference, or for an unversioned start the object's reference, masked when any
    /// version of it is.
    /// </summary>
    private static string StartHandle(NodeRef node, Walk walk, HandleBook handles)
    {
        if (node.Version is not null)
        {
            return handles.Handle(node);
        }

        return handles.IsMaskedObject(node) ? handles.Handle(walk.StartNodes()[0]) : node.ToString();
    }

    private (DateTimeOffset ValidAt, DateTimeOffset KnownAt) ResolveTimes(DateTimeOffset? validAt, DateTimeOffset? knownAt)
    {
        var known = knownAt ?? clock.UtcNow;
        return (validAt ?? known, known);
    }

    private static async Task<Walk> WalkAsync(
        KernelDbContext db, TenantId tenant, LineageQuery query, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken)
    {
        if (query.Depth is < 1 or > LineageQuery.MaxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Depth, $"Depth must be 1 to {LineageQuery.MaxDepth}.");
        }

        if (query.EdgeKinds is { } requested && requested.Any(k => !Enum.IsDefined(k)))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Unknown lineage edge kind.");
        }

        string[]? kinds = query.EdgeKinds is { Count: > 0 } set ? [.. set.Select(k => k.ToString())] : null;
        object[] parameters =
        [
            new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant.Value },
            new NpgsqlParameter("node", NpgsqlDbType.Uuid) { Value = query.Node.Id },
            new NpgsqlParameter("kind", NpgsqlDbType.Varchar) { Value = query.Node.Kind },
            new NpgsqlParameter("version", NpgsqlDbType.Bigint) { Value = (object?)query.Node.Version ?? DBNull.Value },
            new NpgsqlParameter("known_at", NpgsqlDbType.TimestampTz) { Value = knownAt.ToUniversalTime() },
            new NpgsqlParameter("valid_at", NpgsqlDbType.TimestampTz) { Value = validAt.ToUniversalTime() },
            new NpgsqlParameter("kinds", (object?)kinds ?? DBNull.Value) { DataTypeName = "character varying[]" },
            new NpgsqlParameter("depth", NpgsqlDbType.Integer) { Value = query.Depth },
            new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = MaxEdges + 1 },
        ];

        var sql = query.Direction == LineageDirection.Backward ? BackwardSql : ForwardSql;
        var rows = await db.Database.SqlQueryRaw<EdgeRow>(sql, parameters).ToListAsync(cancellationToken).ConfigureAwait(false);

        // Rows come back in depth order and at most MaxEdges + 1, so either an edge past the depth limit or one past
        // the edge limit means the graph goes on.
        var truncated = rows.Count > MaxEdges || rows.Exists(r => r.Depth > query.Depth);
        var within = rows.Where(r => r.Depth <= query.Depth).Take(MaxEdges).ToList();
        return new Walk(query, within, truncated);
    }

    private async Task<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant, Actor reader, IEnumerable<Walk> walks, CancellationToken cancellationToken)
    {
        var nodes = new HashSet<NodeRef>();
        foreach (var walk in walks)
        {
            nodes.UnionWith(walk.StartNodes());
            foreach (var row in walk.Rows)
            {
                nodes.Add(row.From);
                nodes.Add(row.To);
            }
        }

        var masked = await visibility.MaskAsync(tenant, reader, nodes, cancellationToken).ConfigureAwait(false);

        // Masking hides an object, not one version of it: another visible version would show the same id.
        var byObject = masked.GroupBy(m => m.Key.Unversioned()).ToDictionary(g => g.Key, g => g.First().Value);
        var result = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in nodes)
        {
            if (byObject.TryGetValue(node.Unversioned(), out var reason))
            {
                result[node] = masked.TryGetValue(node, out var own) ? own : reason;
            }
        }

        return result;
    }

    private static LineageGraph Build(Walk walk, LineageQuery query, DateTimeOffset validAt, DateTimeOffset knownAt, HandleBook handles)
    {
        var distances = new Dictionary<NodeRef, int>();
        foreach (var origin in walk.StartNodes())
        {
            distances[origin] = 0;
        }

        foreach (var row in walk.Rows)
        {
            var far = query.Direction == LineageDirection.Backward ? row.From : row.To;
            if (!distances.TryGetValue(far, out var known) || row.Depth < known)
            {
                distances[far] = row.Depth;
            }
        }

        // Nodes in walk order (distance, then the order their edges were recorded), so masked handles are numbered
        // by position in the trace and say nothing about the objects' identifiers.
        var firstSeen = new Dictionary<NodeRef, int>();
        for (var i = 0; i < walk.Rows.Count; i++)
        {
            firstSeen.TryAdd(walk.Rows[i].From, i);
            firstSeen.TryAdd(walk.Rows[i].To, i);
        }

        var nodes = distances
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => firstSeen.GetValueOrDefault(pair.Key, -1))
            .Select(pair => handles.Node(pair.Key, pair.Value))
            .ToList();
        var edges = walk.Rows
            .Select(row =>
            {
                var from = handles.Handle(row.From);
                var to = handles.Handle(row.To);
                var masked = handles.IsMasked(row.From) || handles.IsMasked(row.To);
                return new LineageEdge(from, to, Enum.Parse<LineageEdgeKind>(row.Kind), masked ? null : row.ActivityId, row.RecordedAt, row.ValidFrom);
            })
            .ToList();

        // An unversioned start names the object; when any of its versions is masked, so is the start.
        var start = StartHandle(query.Node, walk, handles);
        return new LineageGraph(start, query.Direction, query.Depth, validAt, knownAt, nodes, edges, walk.Truncated);
    }

    /// <summary>One row of the walk: an edge and its fewest-edges distance from the start.</summary>
    private sealed record EdgeRow(
        Guid Id,
        string FromKind,
        Guid FromId,
        long FromVersion,
        string ToKind,
        Guid ToId,
        long ToVersion,
        string Kind,
        Guid ActivityId,
        DateTimeOffset RecordedAt,
        DateTimeOffset? ValidFrom,
        int Depth)
    {
        public NodeRef From => new(FromKind, FromId, FromVersion);

        public NodeRef To => new(ToKind, ToId, ToVersion);
    }

    private sealed record Walk(LineageQuery Query, List<EdgeRow> Rows, bool Truncated)
    {
        /// <summary>
        /// The start's versions: the named one, or for an unversioned start every version the walk left from (or the
        /// unversioned reference itself when no edge touches the object).
        /// </summary>
        public IReadOnlyList<NodeRef> StartNodes()
        {
            if (Query.Node.Version is not null)
            {
                return [Query.Node];
            }

            var near = Rows.Where(r => r.Depth == 1)
                .Select(r => Query.Direction == LineageDirection.Backward ? r.To : r.From)
                .Distinct()
                .ToList();
            return near.Count > 0 ? near : [Query.Node];
        }
    }

    /// <summary>
    /// Gives every node in one read its handle: the reference string when visible, <c>masked:n</c> when masked, with
    /// <c>n</c> counted in the order the read reaches masked objects.
    /// </summary>
    private sealed class HandleBook(IReadOnlyDictionary<NodeRef, MaskReason> masks)
    {
        private readonly Dictionary<NodeRef, string> _masked = [];

        public bool IsMasked(NodeRef node) => masks.ContainsKey(node);

        public bool IsMaskedObject(NodeRef node) => masks.Keys.Any(m => m.Unversioned() == node.Unversioned());

        public string Handle(NodeRef node)
        {
            if (!masks.ContainsKey(node))
            {
                return node.ToString();
            }

            if (!_masked.TryGetValue(node, out var handle))
            {
                handle = $"masked:{_masked.Count + 1}";
                _masked[node] = handle;
            }

            return handle;
        }

        public LineageNode Node(NodeRef node, int distance) =>
            masks.TryGetValue(node, out var reason)
                ? new LineageNode(Handle(node), node.Kind, null, reason, distance)
                : new LineageNode(node.ToString(), node.Kind, node, null, distance);
    }
}
