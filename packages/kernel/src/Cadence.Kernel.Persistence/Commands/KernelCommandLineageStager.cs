using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Persistence.Commands;

internal static class KernelCommandLineageStager
{
    /// <summary>
    /// Stages lineage nodes and edges for every aggregate the command wrote, verifying that every declared input and
    /// part attaches to a written object version (K-LIN-2).
    /// </summary>
    public static void StageAllLineage(
        KernelDbContext db,
        CommandExecution execution,
        IReadOnlyList<ChangedAggregate> changed,
        ChangedAggregate? primary,
        List<(NodeRef Input, LineageEdgeKind Kind, DateTimeOffset? ValidFrom, NodeRef? Target)> inputs,
        Dictionary<NodeRef, bool> parts,
        List<(NodeRef From, NodeRef To, LineageEdgeKind Kind)> partEdges,
        NodeRef? proposal)
    {
        if ((parts.Count > 0 || partEdges.Count > 0)
            && (primary?.Subject is not { Version: not null } || primary.Action == "Deleted"))
        {
            throw new InvalidOperationException(
                $"K-LIN-2: {execution.CommandType} declared lineage inputs or parts but wrote no object version to attach them to.");
        }

        if (inputs.Exists(i => !changed.Any(w => MatchesAggregate(i.Target, w, ReferenceEquals(w, primary)))))
        {
            throw new InvalidOperationException(
                $"K-LIN-2: {execution.CommandType} declared lineage inputs or parts but wrote no object version to attach them to.");
        }

        foreach (var written in changed)
        {
            StageLineage(
                db,
                execution,
                written,
                isPrimary: ReferenceEquals(written, primary),
                inputs,
                parts,
                partEdges,
                proposal);
        }
    }

    private static bool MatchesAggregate(NodeRef? target, ChangedAggregate written, bool isPrimary) =>
        written.Subject is { Version: { } version } subject
        && written.Action != "Deleted"
        && (target is null
            ? isPrimary
            : target.Unversioned() == subject.Unversioned() && (target.Version is null || target.Version == version));

    /// <summary>
    /// Records the written object version as a lineage node and its inbound edges (spec §9.6, K-LIN-2): the inputs the
    /// handler declared for this aggregate, plus <see cref="LineageEdgeKind.Supersedes"/> from the previous version on
    /// an update. A derived object without an inbound edge is a handler defect and aborts the commit (K-LIN-1).
    /// </summary>
    private static void StageLineage(
        KernelDbContext db,
        CommandExecution execution,
        ChangedAggregate written,
        bool isPrimary,
        List<(NodeRef Input, LineageEdgeKind Kind, DateTimeOffset? ValidFrom, NodeRef? Target)> inputs,
        Dictionary<NodeRef, bool> parts,
        List<(NodeRef From, NodeRef To, LineageEdgeKind Kind)> partEdges,
        NodeRef? proposal)
    {
        if (written.Subject is not { Version: { } version } subject || written.Action == "Deleted")
        {
            return;
        }

        var scoped = inputs.Where(i => MatchesAggregate(i.Target, written, isPrimary)).ToList();
        if (scoped.Exists(i => i.Input.Unversioned() == subject.Unversioned() && i.Input.Version >= version))
        {
            throw new InvalidOperationException(
                $"K-LIN: {execution.CommandType} declared {subject} as an input of itself or of an earlier version.");
        }

        var inbound = scoped.Select(i => (From: i.Input, i.Kind, i.ValidFrom)).ToList();
        if (written.Action == "Updated")
        {
            inbound.Add((subject.AtVersion(version - 1), LineageEdgeKind.Supersedes, null));
        }

        if (isPrimary && proposal is { } executedProposal && executedProposal.Unversioned() != subject.Unversioned())
        {
            inbound.Add((executedProposal, LineageEdgeKind.ExecutedUnder, null));
        }

        if (written.Derived && inbound.Count == 0)
        {
            throw new InvalidOperationException(
                $"K-LIN-1: {execution.CommandType} wrote derived object {subject} without declaring its inputs.");
        }

        var pinned = subject.AtVersion(version);
        db.LineageNodes.Add(new LineageNodeRecord
        {
            TenantId = execution.Command.TenantId,
            Kind = subject.Kind,
            NodeId = subject.Id,
            Version = version,
            Derived = written.Derived,
            ActivityId = execution.ActivityId,
            RecordedAt = execution.RecordedAt,
        });
        foreach (var (from, kind, validFrom) in inbound.DistinctBy(e => (e.From, e.Kind)))
        {
            AddEdge(db, execution, from, pinned, kind, validFrom);
        }

        if (isPrimary)
        {
            StageParts(db, execution, pinned, parts, partEdges);
        }
    }

    /// <summary>
    /// Writes the parts of the subject version (ADR-2474): a node for each new part version, the edge from the subject to
    /// every part, and the declared edges between parts, each of which must touch a new part.
    /// </summary>
    private static void StageParts(
        KernelDbContext db,
        CommandExecution execution,
        NodeRef subject,
        Dictionary<NodeRef, bool> parts,
        List<(NodeRef From, NodeRef To, LineageEdgeKind Kind)> partEdges)
    {
        foreach (var (part, newVersion) in parts)
        {
            if (part.Unversioned() == subject.Unversioned())
            {
                throw new InvalidOperationException($"K-LIN: {execution.CommandType} declared {part} as a part of itself.");
            }

            if (newVersion)
            {
                db.LineageNodes.Add(new LineageNodeRecord
                {
                    TenantId = execution.Command.TenantId,
                    Kind = part.Kind,
                    NodeId = part.Id,
                    Version = part.Version!.Value,
                    Derived = false,
                    ActivityId = execution.ActivityId,
                    RecordedAt = execution.RecordedAt,
                });
            }

            AddEdge(db, execution, subject, part, LineageEdgeKind.GovernedBy, null);
        }

        foreach (var (from, to, kind) in partEdges)
        {
            if (!parts.GetValueOrDefault(from) && !parts.GetValueOrDefault(to))
            {
                throw new InvalidOperationException(
                    $"K-LIN-2: {execution.CommandType} declared the edge {from} → {to}, but neither is a part it writes.");
            }

            AddEdge(db, execution, from, to, kind, null);
        }
    }

    private static void AddEdge(
        KernelDbContext db,
        CommandExecution execution,
        NodeRef from,
        NodeRef to,
        LineageEdgeKind kind,
        DateTimeOffset? validFrom) =>
        db.LineageEdges.Add(new LineageEdgeRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = execution.Command.TenantId,
            FromKind = from.Kind,
            FromId = from.Id,
            FromVersion = from.Version!.Value,
            ToKind = to.Kind,
            ToId = to.Id,
            ToVersion = to.Version!.Value,
            Kind = kind,
            ActivityId = execution.ActivityId,
            RecordedAt = execution.RecordedAt,
            ValidFrom = validFrom,
        });
}
