using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Identity;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.IdentityVault;

/// <summary>
/// Reads the IdentityVault through its functions, as the application role, on a connection scoped to one tenant.
/// </summary>
internal sealed class KernelIdentityVaultStore(KernelTenantDbContextFactory contexts) : IIdentityVaultStore
{
    public async Task<StoredIdentity?> FindAsync(TenantId tenant, IdentityLayer layer, Guid subjectId, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var row = await db.Database
            .SqlQuery<RecordRow>(
                $"""
                SELECT key_id AS "KeyId", wrapped_data_key AS "WrappedDataKey", ciphertext AS "Ciphertext",
                       blind_index AS "BlindIndex", recorded_at AS "RecordedAt", erased_at AS "ErasedAt"
                FROM identity_vault.get_record({IdentitySealer.LayerToken(layer)}, {subjectId})
                """)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row switch
        {
            null => null,
            { KeyId: { } keyId, WrappedDataKey: { } wrapped, Ciphertext: { } ciphertext } =>
                new StoredIdentity(new SealedIdentity(keyId, wrapped, ciphertext, row.BlindIndex), row.RecordedAt, row.ErasedAt),
            _ => new StoredIdentity(null, row.RecordedAt, row.ErasedAt),
        };
    }

    public async Task<PersonId?> LinkedPersonAsync(TenantId tenant, ParticipantId participant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var person = await db.Database
            .SqlQuery<Guid?>($"SELECT identity_vault.linked_person({participant.Value}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return person is { } id ? PersonId.FromGuid(id) : null;
    }

    public async Task<IReadOnlyList<ParticipantId>> LinkedParticipantsAsync(TenantId tenant, PersonId person, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var ids = await db.Database
            .SqlQuery<Guid>($"SELECT p AS \"Value\" FROM identity_vault.linked_participants({person.Value}) AS p")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. ids.Select(ParticipantId.FromGuid)];
    }

    public async Task<Guid?> FindSubjectByBlindIndexAsync(TenantId tenant, IdentityLayer layer, byte[] blindIndex, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blindIndex);
        await using var db = contexts.Create(tenant);
        return await db.Database
            .SqlQuery<Guid?>($"SELECT identity_vault.find_subject_by_blind_index({IdentitySealer.LayerToken(layer)}, {blindIndex}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record RecordRow(
        string? KeyId, byte[]? WrappedDataKey, byte[]? Ciphertext, byte[]? BlindIndex, DateTimeOffset RecordedAt, DateTimeOffset? ErasedAt);
}

/// <summary>
/// The development key hierarchy (ADR-2458): each tenant's key-encryption key and blind-index key are derived from one
/// 256-bit master key with HKDF-SHA256, salted with the tenant id, so no per-tenant key is stored anywhere. Production
/// replaces it with a KMS-backed <see cref="IVaultKeyProvider"/> behind the same port.
/// </summary>
internal sealed class LocalVaultKeyProvider : IVaultKeyProvider
{
    private const int KeySize = 32;
    private static readonly byte[] KekInfo = Encoding.UTF8.GetBytes("cadence.identity_vault.kek.v1");
    private static readonly byte[] BlindIndexInfo = Encoding.UTF8.GetBytes("cadence.identity_vault.blind_index.v1");

    private readonly byte[] _master;
    private readonly string _keyId;

    public LocalVaultKeyProvider(byte[] masterKey, string? region)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        if (masterKey.Length != KeySize)
        {
            throw new ArgumentException("The IdentityVault master key must be 32 bytes.", nameof(masterKey));
        }

        _master = [.. masterKey];
        _keyId = "local-v1-" + Convert.ToHexStringLower(SHA256.HashData([.. KekInfo, .. _master]))[..16];
        Region = region;
    }

    public string? Region { get; }

    public VaultKey CurrentKey(TenantId tenant) => new(_keyId, Derive(tenant, KekInfo));

    public VaultKey Key(TenantId tenant, string keyId) =>
        string.Equals(keyId, _keyId, StringComparison.Ordinal)
            ? CurrentKey(tenant)
            : throw new CryptographicException($"The IdentityVault holds no key '{keyId}'.");

    public byte[] BlindIndexKey(TenantId tenant) => Derive(tenant, BlindIndexInfo);

    private byte[] Derive(TenantId tenant, byte[] info) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _master, KeySize, tenant.Value.ToByteArray(bigEndian: true), info);
}
