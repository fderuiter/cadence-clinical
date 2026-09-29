using System;

using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical Attestation aggregate root capturing 21 CFR Part 11 electronic signature ceremony and audit manifest.
/// @req:PRD-SYS-053
/// </summary>
public class Attestation : AggregateRoot<AttestationId>
{
    public TenantId TenantId { get; private set; }
    public PersonId SignerId { get; private set; }
    public string Meaning { get; private set; } = string.Empty;
    public DateTimeOffset SignedAt { get; private set; }
    public string SignatureManifest { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    protected Attestation() { }

    public Attestation(
        AttestationId id,
        TenantId tenantId,
        PersonId signerId,
        string meaning,
        DateTimeOffset signedAt,
        string signatureManifest,
        string createdBy,
        string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        SignerId = signerId;
        Meaning = meaning;
        SignedAt = signedAt;
        SignatureManifest = signatureManifest;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}
