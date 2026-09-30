using System;

using Cadence.Kernel.Lineage;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// One version of a kernel object, recorded by the command that wrote it (spec §9.6). <see cref="Derived"/> copies the
/// aggregate's <see cref="DerivedObjectAttribute"/>, so the K-LIN-1 check can find derived versions without inputs.
/// Insert-only.
/// </summary>
internal sealed class LineageNodeRecord
{
    public TenantId TenantId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public Guid NodeId { get; init; }
    public long Version { get; init; }
    public bool Derived { get; init; }
    public Guid ActivityId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// A lineage edge from an upstream object version to a downstream one (spec §9.6), written in the downstream object's
/// command transaction (K-LIN-2). <see cref="RecordedAt"/> is transaction time and <see cref="ValidFrom"/> valid time,
/// so reads can ask as of <c>(valid_at, known_at)</c>. Insert-only.
/// </summary>
internal sealed class LineageEdgeRecord
{
    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public string FromKind { get; init; } = string.Empty;
    public Guid FromId { get; init; }
    public long FromVersion { get; init; }
    public string ToKind { get; init; } = string.Empty;
    public Guid ToId { get; init; }
    public long ToVersion { get; init; }
    public LineageEdgeKind Kind { get; init; }
    public Guid ActivityId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
}
