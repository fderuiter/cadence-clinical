using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// An electronic signature (spec §9.5, 21 CFR 11.50, 11.70): a person asserts <see cref="Meaning"/> about the exact
/// content named by <see cref="Target"/>, after authenticating at <see cref="AuthenticatedAt"/> by
/// <see cref="Method"/>. Append-only: the database rejects updates and deletes.
/// </summary>
public class Attestation : AggregateRoot<AttestationId>
{
    /// <summary>
    /// How long an authentication stays fresh enough to sign with. A signature after this needs the person to sign in
    /// again (21 CFR 11.200(a)(1)).
    /// </summary>
    public static readonly TimeSpan MaxAuthenticationAge = TimeSpan.FromMinutes(5);

    protected Attestation() { }

    private Attestation(
        AttestationId id,
        TenantId tenantId,
        string actorId,
        AttestationMeaning meaning,
        AttestationTarget target,
        DateTimeOffset authenticatedAt,
        string method,
        DateTimeOffset recordedAt,
        string reason)
        : base(id)
    {
        TenantId = tenantId;
        ActorId = actorId;
        Meaning = meaning;
        TargetKind = target.Kind;
        TargetId = target.Id;
        TargetContentHash = target.ContentHash;
        AuthenticatedAt = authenticatedAt;
        Method = method;
        RecordedAt = recordedAt;
        Reason = reason;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>The signing person's actor id (their identity-provider subject).</summary>
    public string ActorId { get; private set; } = string.Empty;

    public AttestationMeaning Meaning { get; private set; }

    /// <summary>The kind of the signed record (<see cref="AttestationTarget.Kind"/>).</summary>
    public string TargetKind { get; private set; } = string.Empty;

    /// <summary>The id of the signed record.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>The hash of the signed content.</summary>
    public string TargetContentHash { get; private set; } = string.Empty;

    /// <summary>The exact record and content this attestation signs.</summary>
    public AttestationTarget Target => new(TargetKind, TargetId, TargetContentHash);

    /// <summary>When the signer last authenticated before signing.</summary>
    public DateTimeOffset AuthenticatedAt { get; private set; }

    /// <summary>How the signer authenticated, for example <c>pwd+otp</c>.</summary>
    public string Method { get; private set; } = string.Empty;

    /// <summary>When the signature was recorded, from the kernel clock.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Why the signer signed.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// Records a signature by <paramref name="actor"/>, who must be a person whose authentication is at most
    /// <see cref="MaxAuthenticationAge"/> old at <paramref name="recordedAt"/>. The caller has checked the target.
    /// </summary>
    public static CommandResult<Attestation> Sign(
        AttestationId id,
        TenantId tenantId,
        Actor actor,
        AttestationMeaning meaning,
        AttestationTarget target,
        DateTimeOffset authenticatedAt,
        string method,
        DateTimeOffset recordedAt,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(target);
        if (actor.Kind != ActorKind.Person)
        {
            return TrustErrors.PersonRequired;
        }

        if (string.IsNullOrWhiteSpace(method))
        {
            return TrustErrors.MethodRequired;
        }

        authenticatedAt = KernelTimestamps.ToStoredPrecision(authenticatedAt);
        if (authenticatedAt > recordedAt || recordedAt - authenticatedAt > MaxAuthenticationAge)
        {
            return TrustErrors.AuthenticationStale;
        }

        if (!Enum.IsDefined(meaning) || string.IsNullOrWhiteSpace(target.Kind) || !Trust.ContentHash.IsWellFormed(target.ContentHash))
        {
            return TrustErrors.TargetNotSignable;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrors.ReasonForChangeRequired;
        }

        return CommandResult<Attestation>.Success(
            new Attestation(id, tenantId, actor.Id, meaning, target, authenticatedAt, method, recordedAt, reason));
    }

    /// <summary>
    /// Why this attestation does not satisfy a requirement that <paramref name="signer"/> signed
    /// <paramref name="meaning"/> on exactly <paramref name="target"/>, or null when it does. The person who acts on a
    /// signature must be the person who gave it (21 CFR 11.50, 11.200).
    /// </summary>
    public string? Mismatch(AttestationMeaning meaning, AttestationTarget target, Actor signer)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(signer);
        if (signer.Kind != ActorKind.Person || !string.Equals(ActorId, signer.Id, StringComparison.Ordinal))
        {
            return "The attestation was signed by someone else.";
        }

        if (Meaning != meaning)
        {
            return $"The attestation means {Meaning}, not {meaning}.";
        }

        if (!string.Equals(TargetKind, target.Kind, StringComparison.Ordinal) || TargetId != target.Id)
        {
            return "The attestation signs a different record.";
        }

        return string.Equals(TargetContentHash, target.ContentHash, StringComparison.Ordinal)
            ? null
            : "The attestation signs different content: the record changed after it was signed.";
    }
}
