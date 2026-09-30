using Cadence.Kernel.Commands;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Events;

/// <summary>A person signed an attestation (spec §9.5, Trust family).</summary>
[KernelEvent("AttestationRecorded", 1)]
public sealed record AttestationRecorded(
    TenantId TenantId,
    AttestationId AttestationId,
    string ActorId,
    AttestationMeaning Meaning,
    AttestationTarget Target);
