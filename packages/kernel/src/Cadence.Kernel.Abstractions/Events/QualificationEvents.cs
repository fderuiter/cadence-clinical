using System;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A qualification was recorded for an actor (spec §9.3, Trust family).</summary>
[KernelEvent("QualificationRecorded", 1)]
public sealed record QualificationRecorded(
    TenantId TenantId,
    QualificationId QualificationId,
    string Holder,
    QualificationKind Kind,
    string Code,
    ProtocolVersionId? ProtocolVersionId,
    ArtifactVersionId Evidence,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>A qualification's validity was ended (Trust family).</summary>
[KernelEvent("QualificationRevoked", 1)]
public sealed record QualificationRevoked(TenantId TenantId, QualificationId QualificationId, DateTimeOffset EffectiveAt);

/// <summary>A capability started to require a qualification within a scope (spec §9.3 step 3, Trust family).</summary>
[KernelEvent("QualificationRequirementDefined", 1)]
public sealed record QualificationRequirementDefined(
    TenantId TenantId,
    QualificationRequirementId RequirementId,
    string Capability,
    string QualificationCode,
    ProtocolVersionId? ProtocolVersionId,
    AuthorityScope Scope,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>A qualification requirement was ended (Trust family).</summary>
[KernelEvent("QualificationRequirementRetired", 1)]
public sealed record QualificationRequirementRetired(TenantId TenantId, QualificationRequirementId RequirementId, DateTimeOffset EffectiveAt);
