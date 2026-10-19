using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Identity;
using Cadence.Kernel.Modules;

namespace Cadence.Kernel.Application.Identity;

/// <summary>
/// A write to the IdentityVault that a command stages with <see cref="IdentityVaultStaging.StageVaultWrite"/>.
/// Kernel persistence applies it in the command's own transaction (K-TX-2), after the kernel rows and before the
/// commit, so the vault never drifts from the audited command (ADR-2253 driver 4).
/// </summary>
public interface IIdentityVaultWrite;

/// <summary>
/// The kernel unit of work's IdentityVault staging (ADR-2458). Only kernel persistence implements it, so a module's
/// unit of work cannot stage vault writes (ADR-2600).
/// </summary>
public interface IIdentityVaultStaging
{
    /// <summary>
    /// Stages a write to the IdentityVault, applied in this command's transaction after the kernel rows and before the
    /// commit (ADR-2458). Only the IdentityVault handlers stage vault writes.
    /// </summary>
    void StageVaultWrite(IIdentityVaultWrite write);
}

/// <summary>Lets an IdentityVault handler stage a vault write through its command's unit of work.</summary>
public static class IdentityVaultStaging
{
    /// <summary>Stages <paramref name="write"/> on the kernel unit of work (ADR-2458).</summary>
    /// <exception cref="InvalidOperationException">The unit of work is not the kernel's.</exception>
    public static void StageVaultWrite(this IKernelUnitOfWork work, IIdentityVaultWrite write)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (work is not IIdentityVaultStaging staging)
        {
            throw new InvalidOperationException("Only the kernel's unit of work stages IdentityVault writes (ADR-2458).");
        }

        staging.StageVaultWrite(write);
    }
}

/// <summary>Stores or replaces the sealed details of a subject in a layer.</summary>
public sealed record PutIdentityRecord(IdentityLayer Layer, Guid SubjectId, SealedIdentity Sealed) : IIdentityVaultWrite;

/// <summary>Crypto-shreds a subject's record in a layer: its data key and ciphertext are deleted.</summary>
public sealed record ShredIdentityRecord(IdentityLayer Layer, Guid SubjectId) : IIdentityVaultWrite;

/// <summary>Links a participant to a person (K-IDN-2).</summary>
public sealed record PutParticipantLink(ParticipantId ParticipantId, PersonId PersonId) : IIdentityVaultWrite;

/// <summary>A vault record as stored: the sealed details, or null once erased.</summary>
public sealed record StoredIdentity(SealedIdentity? Sealed, DateTimeOffset RecordedAt, DateTimeOffset? ErasedAt)
{
    public bool IsErased => ErasedAt is not null;
}

/// <summary>
/// Reads the IdentityVault for one tenant. Implemented by kernel persistence through the vault's functions, the only
/// access the application role has to the vault schema. Only the vault's handlers and <see cref="IIdentityVault"/>
/// use it; the details stay sealed until <see cref="IdentitySealer.Unseal"/>.
/// </summary>
public interface IIdentityVaultStore
{
    /// <summary>The subject's record in the layer, or null when none was ever recorded.</summary>
    Task<StoredIdentity?> FindAsync(TenantId tenant, IdentityLayer layer, Guid subjectId, CancellationToken cancellationToken);

    /// <summary>The person a participant is linked to, or null.</summary>
    Task<PersonId?> LinkedPersonAsync(TenantId tenant, ParticipantId participant, CancellationToken cancellationToken);

    /// <summary>Every participant linked to the person, in id order.</summary>
    Task<IReadOnlyList<ParticipantId>> LinkedParticipantsAsync(TenantId tenant, PersonId person, CancellationToken cancellationToken);
}
