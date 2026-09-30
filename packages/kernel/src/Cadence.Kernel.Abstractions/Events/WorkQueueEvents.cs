using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A version of a work-queue definition was defined (Canvas D10, ADR-2486, Trust family).</summary>
[KernelEvent("WorkQueueDefined", 1)]
public sealed record WorkQueueDefined(
    TenantId TenantId,
    WorkQueueDefinitionId WorkQueueDefinitionId,
    string Code,
    int Version,
    string Capability,
    string Source);
