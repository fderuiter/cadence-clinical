using System;
using System.Collections.Generic;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;

namespace Cadence.Kernel.Events;

/// <summary>An effectivity was declared for a protocol version in a scope (spec §7.1, Execution family).</summary>
[KernelEvent("EffectivityDeclared", 1)]
public sealed record EffectivityDeclared(
    TenantId TenantId,
    EffectivityId EffectivityId,
    ProtocolVersionId ProtocolVersionId,
    StudyId StudyId,
    EffectivityScope Scope,
    DateTimeOffset DeclaredAt);

/// <summary>An effectivity changed status, on the evidence named (spec §7.1, Execution family).</summary>
[KernelEvent("EffectivityStatusChanged", 1)]
public sealed record EffectivityStatusChanged(
    TenantId TenantId,
    EffectivityId EffectivityId,
    ProtocolVersionId ProtocolVersionId,
    EffectivityStatus From,
    EffectivityStatus To,
    DateTimeOffset EffectiveAt,
    IReadOnlyList<ArtifactVersionId> Evidence);

/// <summary>A study site was activated with the protocol versions in force there (spec §5.2, K-TIME-4).</summary>
[KernelEvent("StudySiteActivated", 1)]
public sealed record StudySiteActivated(
    TenantId TenantId,
    StudySiteId StudySiteId,
    StudyId StudyId,
    IReadOnlyList<ProtocolVersionId> EffectiveVersions);
