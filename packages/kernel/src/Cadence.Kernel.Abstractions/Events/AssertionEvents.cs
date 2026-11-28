using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Events;

/// <summary>
/// An assertion was made about an observation or another assertion (spec §8.2, Evidence family): a verification, a
/// query or an answer. <c>Revision</c> names the revision a judgement of a value judged.
/// </summary>
[KernelEvent("AssertionMade", 1)]
public sealed record AssertionMade(
    TenantId TenantId,
    AssertionId AssertionId,
    AssertionKind Kind,
    StudyId StudyId,
    EnrollmentId EnrollmentId,
    NodeRef Target,
    int? Revision);

/// <summary>An assertion was retracted by <c>AssertionId</c> (spec §8.2, Evidence family); it stays readable.</summary>
[KernelEvent("AssertionRetracted", 1)]
public sealed record AssertionRetracted(
    TenantId TenantId,
    AssertionId AssertionId,
    AssertionId RetractedId,
    AssertionKind Kind,
    StudyId StudyId,
    EnrollmentId EnrollmentId);

/// <summary>A lock was applied at a scope (spec §10.5, Quality family); covered writes are refused from now on.</summary>
[KernelEvent("LockApplied", 1)]
public sealed record LockApplied(
    TenantId TenantId,
    AssertionId LockId,
    LockKind Kind,
    StudyId StudyId,
    NodeRef Scope,
    AttestationId AttestationId,
    LockMilestone Milestone = LockMilestone.InterimCut,
    DatabaseLockCertificateRecord? Certificate = null);

/// <summary>A lock was released by the retracting assertion <c>AssertionId</c> (spec §10.5, Quality family).</summary>
[KernelEvent("LockReleased", 1)]
public sealed record LockReleased(
    TenantId TenantId,
    AssertionId AssertionId,
    AssertionId LockId,
    LockKind Kind,
    StudyId StudyId,
    NodeRef Scope,
    AttestationId AttestationId,
    string? RevokedCertificateId = null);
