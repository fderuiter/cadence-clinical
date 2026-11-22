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

/// <summary>Initial protocol applicability was established for an enrollment (spec §7.5, K-TIME-6).</summary>
[KernelEvent("ApplicabilityEstablished", 1)]
public sealed record ApplicabilityEstablished(
    TenantId TenantId,
    ApplicabilityId ApplicabilityId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    DateTimeOffset ValidFrom);

/// <summary>An enrollment transitioned to a new governing protocol version (spec §7.5, K-TIME-6).</summary>
[KernelEvent("ParticipantProtocolTransitioned", 1)]
public sealed record ParticipantProtocolTransitioned(
    TenantId TenantId,
    ApplicabilityId ApplicabilityId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ProtocolVersionId? FromVersionId,
    ProtocolVersionId ToVersionId,
    DateTimeOffset EffectiveAt);

/// <summary>An amendment requires participant re-consent before advancing applicability (spec §7.5, PRD-SUB-007).</summary>
[KernelEvent("ParticipantReconsentRequired", 1)]
public sealed record ParticipantReconsentRequired(
    TenantId TenantId,
    ApplicabilityId ApplicabilityId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ProtocolVersionId? FromVersionId,
    ProtocolVersionId TargetVersionId,
    EffectivityId EffectivityId,
    DateTimeOffset RequiredSince);

/// <summary>An applicability inconsistency was flagged or cleared on an enrollment (spec §7.5, K-TIME-6).</summary>
[KernelEvent("ApplicabilityInconsistencyFlagged", 1)]
public sealed record ApplicabilityInconsistencyFlagged(
    TenantId TenantId,
    ApplicabilityId ApplicabilityId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    bool IsInconsistent,
    string? Reason);

/// <summary>Bitemporal consent status was recorded for an enrollment (spec §9.4, K-CONS-1).</summary>
[KernelEvent("ConsentStatusRecorded", 1)]
public sealed record ConsentStatusRecorded(
    TenantId TenantId,
    ConsentStatusId ConsentStatusId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    bool Signed,
    DateTimeOffset SignedAt,
    ConsentBasisKind BasisKind,
    IReadOnlyList<ConsentScopeEntry> Scopes);
