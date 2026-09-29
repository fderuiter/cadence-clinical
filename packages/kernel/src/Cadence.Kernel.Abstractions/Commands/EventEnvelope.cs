using System;
using System.Text.Json;

namespace Cadence.Kernel.Commands;

/// <summary>
/// The versioned integration envelope every kernel event travels in (spec §5.2). Schemas are additive-only within a
/// major <see cref="SchemaVersion"/> and consumers tolerate unknown fields (K-EVT-1); events are the integration
/// contract, never replayed to rebuild clinical state (K-EVT-2).
/// @req:PRD-SYS-053
/// </summary>
public sealed record EventEnvelope(
    Guid EventId,
    string Type,
    int SchemaVersion,
    TenantId Tenant,
    DateTimeOffset? OccurredValid,
    DateTimeOffset Recorded,
    string Actor,
    Guid ActivityId,
    Guid CorrelationId,
    Guid? CausationId,
    JsonElement Payload);

/// <summary>
/// Names a kernel event type and its schema version. Every event raised through the command pipeline must carry it,
/// so the wire name never depends on a CLR type name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KernelEventAttribute(string type, int schemaVersion) : Attribute
{
    public string Type { get; } = type;

    public int SchemaVersion { get; } = schemaVersion;
}
