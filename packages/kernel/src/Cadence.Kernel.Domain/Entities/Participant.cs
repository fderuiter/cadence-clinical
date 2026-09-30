using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A participant: a pseudonymous kernel identity in a tenant (spec §7.4, §9.2). It holds no study, site, code or
/// status; each study membership is an <see cref="Enrollment"/>, and identifying details belong to the IdentityVault
/// (#4782). EDC's subject, eCOA's user and RTSM's randomized subject are views of the same <see cref="ParticipantId"/>.
/// </summary>
public class Participant : AggregateRoot<ParticipantId>
{
    protected Participant() { }

    private Participant(ParticipantId id, TenantId tenantId)
        : base(id) => TenantId = tenantId;

    public TenantId TenantId { get; private set; }

    /// <summary>Registers a participant identity in <paramref name="tenantId"/>.</summary>
    public static Participant Register(ParticipantId id, TenantId tenantId) => new(id, tenantId);
}
