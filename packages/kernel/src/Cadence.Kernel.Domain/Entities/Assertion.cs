using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A statement by an actor with authority about an observation, another assertion or a lock scope (spec §8.2, §10.5):
/// a verification, a query or its answer, or a lock. Assertions are append-only (K-FACT-1): nothing about one changes
/// after it is recorded, and a later assertion of the same kind retracts it (<see cref="RetractsId"/>).
/// <list type="bullet">
/// <item>A judgement of a value names the revision it judged, so it never applies to a later correction (ADR-2621).</item>
/// <item>A query and a lock name only their target's id.</item>
/// <item>A lock and its release each carry an attestation that signs <see cref="LockStatement"/>.</item>
/// </list>
/// </summary>
public class Assertion : AggregateRoot<AssertionId>
{
    protected Assertion() { }

    private Assertion(
        AssertionId id,
        TenantId tenantId,
        AssertionKind kind,
        StudyId studyId,
        EnrollmentId? enrollmentId,
        NodeRef target,
        string assertedBy,
        DateTimeOffset assertedAt)
        : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        StudyId = studyId;
        EnrollmentId = enrollmentId;
        TargetKind = target.Kind;
        TargetId = target.Id;
        AssertedBy = assertedBy;
        AssertedAt = KernelTimestamps.ToStoredPrecision(assertedAt);
    }

    public TenantId TenantId { get; private set; }

    public AssertionKind Kind { get; private set; }

    /// <summary>The study the target belongs to; every assertion is authorized and read within it.</summary>
    public StudyId StudyId { get; private set; }

    /// <summary>The enrollment of the target; null for a lock of a whole study or study site.</summary>
    public EnrollmentId? EnrollmentId { get; private set; }

    /// <summary>The NodeRef kind of the target: an observation, an assertion or a lock scope.</summary>
    public string TargetKind { get; private set; } = string.Empty;

    public Guid TargetId { get; private set; }

    /// <summary>The revision a judgement of a value judged; null otherwise.</summary>
    public int? TargetRevision { get; private set; }

    /// <summary>A query's or answer's text.</summary>
    public string? Text { get; private set; }

    /// <summary>The assertion this one retracts, of the same kind; null for an assertion that states something.</summary>
    public AssertionId? RetractsId { get; private set; }

    /// <summary>The signature of a lock or its release.</summary>
    public AttestationId? AttestationId { get; private set; }

    public string AssertedBy { get; private set; } = string.Empty;

    public DateTimeOffset AssertedAt { get; private set; }

    /// <summary>What the assertion is about, without a version.</summary>
    public NodeRef Target => new(TargetKind, TargetId);

    /// <summary>True for a <see cref="AssertionKind.Locked"/> or <see cref="AssertionKind.Frozen"/> assertion.</summary>
    public bool IsLock => Kind is AssertionKind.Locked or AssertionKind.Frozen;

    /// <summary>Applies a lock of <paramref name="kind"/> at <paramref name="scope"/>, signed by <paramref name="attestation"/>.</summary>
    public static Assertion Lock(
        AssertionId id,
        TenantId tenant,
        LockKind kind,
        StudyId study,
        EnrollmentId? enrollment,
        NodeRef scope,
        AttestationId attestation,
        string assertedBy,
        DateTimeOffset assertedAt)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new Assertion(id, tenant, (AssertionKind)kind, study, enrollment, scope, assertedBy, assertedAt) { AttestationId = attestation };
    }

    /// <summary>Verifies revision <paramref name="revision"/> of <paramref name="observation"/> against source.</summary>
    public static Assertion Verify(AssertionId id, Observation observation, int revision, string assertedBy, DateTimeOffset assertedAt)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return new Assertion(
            id, observation.TenantId, AssertionKind.Verified, observation.StudyId, observation.EnrollmentId,
            NodeRef.For(observation.Id), assertedBy, assertedAt)
        {
            TargetRevision = revision,
        };
    }

    /// <summary>Raises a query about <paramref name="observation"/>, or returns why the text or revision is refused.</summary>
    public static CommandResult<Assertion> Query(
        AssertionId id,
        Observation observation,
        string? text,
        string assertedBy,
        DateTimeOffset assertedAt,
        int? revision = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!IsValidText(text))
        {
            return AssertionErrors.TextInvalid;
        }

        int targetRevision = revision ?? observation.Revision;
        if (targetRevision < 1 || targetRevision > observation.Revision)
        {
            return AssertionErrors.RevisionStale;
        }

        return CommandResult<Assertion>.Success(new Assertion(
            id, observation.TenantId, AssertionKind.Queried, observation.StudyId, observation.EnrollmentId,
            NodeRef.For(observation.Id), assertedBy, assertedAt)
        {
            TargetRevision = targetRevision,
            Text = text,
        });
    }

    /// <summary>Answers <paramref name="query"/>, or returns why it cannot be answered.</summary>
    public static CommandResult<Assertion> Answer(
        AssertionId id,
        Assertion query,
        string? text,
        string assertedBy,
        DateTimeOffset assertedAt,
        int? revision = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Kind != AssertionKind.Queried || query.RetractsId is not null)
        {
            return AssertionErrors.NotAQuery;
        }

        if (!IsValidText(text))
        {
            return AssertionErrors.TextInvalid;
        }

        int? targetRevision = revision ?? query.TargetRevision;
        if (targetRevision is < 1)
        {
            return AssertionErrors.RevisionStale;
        }

        return CommandResult<Assertion>.Success(new Assertion(
            id, query.TenantId, AssertionKind.QueryAnswered, query.StudyId, query.EnrollmentId,
            NodeRef.For(query.Id), assertedBy, assertedAt)
        {
            TargetRevision = targetRevision,
            Text = text,
        });
    }

    /// <summary>
    /// Retracts <paramref name="retracted"/>: an assertion of the same kind, about the same target, that names it. A
    /// lock's release carries its own <paramref name="attestation"/>.
    /// </summary>
    public static Assertion Retract(AssertionId id, Assertion retracted, AttestationId? attestation, string assertedBy, DateTimeOffset assertedAt)
    {
        ArgumentNullException.ThrowIfNull(retracted);
        return new Assertion(
            id, retracted.TenantId, retracted.Kind, retracted.StudyId, retracted.EnrollmentId, retracted.Target, assertedBy, assertedAt)
        {
            TargetRevision = retracted.TargetRevision,
            RetractsId = retracted.Id,
            AttestationId = attestation,
        };
    }

    private static bool IsValidText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= AssertionText.MaxLength;
}
