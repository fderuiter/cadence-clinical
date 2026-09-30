using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Kernel.Identity;

namespace Cadence.Kernel.Application.Identity;

/// <summary>
/// A key-encryption key (KEK) of one tenant: the key that wraps the tenant's per-record data keys. <see cref="Id"/> is
/// stored beside each wrapped data key, so a rotated KEK can still unwrap what the old one wrapped.
/// </summary>
public sealed record VaultKey(string Id, byte[] Material);

/// <summary>
/// The key hierarchy of the IdentityVault (ADR-2253, ADR-2458): per-tenant key-encryption keys and blind-index keys,
/// held by a KMS in production and derived from a local master key in development. The kernel never stores a KEK.
/// Every call returns fresh key material that the caller owns: <see cref="IdentitySealer"/> zeroes it after use.
/// </summary>
public interface IVaultKeyProvider
{
    /// <summary>
    /// The data-residency region this vault serves, or null when it serves no particular region. A tenant with a
    /// residency is served only by a vault of the same region (spec §13).
    /// </summary>
    string? Region { get; }

    /// <summary>The tenant's current KEK, used to wrap new data keys.</summary>
    VaultKey CurrentKey(TenantId tenant);

    /// <summary>The tenant's KEK with id <paramref name="keyId"/>, to unwrap a stored data key. Throws for an unknown id.</summary>
    VaultKey Key(TenantId tenant, string keyId);

    /// <summary>The tenant's key for blind indexes (keyed HMAC), separate from its KEKs.</summary>
    byte[] BlindIndexKey(TenantId tenant);
}

/// <summary>
/// Identity details as the vault stores them: the ciphertext under a per-record data key, that data key wrapped by the
/// tenant's KEK <see cref="KeyId"/>, and the blind index of the layer's lookup value. None of it is readable without the
/// KEK; deleting <see cref="WrappedDataKey"/> and <see cref="Ciphertext"/> erases the record (crypto-shredding).
/// </summary>
public sealed record SealedIdentity(string KeyId, byte[] WrappedDataKey, byte[] Ciphertext, byte[]? BlindIndex);

/// <summary>
/// Envelope encryption of identity details (ADR-2458). Each record gets a fresh 256-bit data key; the details are
/// encrypted with AES-256-GCM under it and the data key is wrapped with AES-256-GCM under the tenant's KEK. Both bind
/// the tenant, layer and subject as associated data, so sealed bytes copied to another record, layer or tenant fail
/// to open. The blind index is HMAC-SHA256 of the normalized lookup value under the tenant's blind-index key.
/// </summary>
public sealed class IdentitySealer(IVaultKeyProvider keys)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DataKeySize = 32;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The region of the vault behind this sealer; see <see cref="IVaultKeyProvider.Region"/>.</summary>
    public string? Region => keys.Region;

    /// <summary>True when a tenant with <paramref name="residency"/> may keep identifiers in this vault.</summary>
    public bool Serves(string? residency) => residency is null || string.Equals(residency, keys.Region, StringComparison.Ordinal);

    /// <summary>Seals <paramref name="details"/> for one subject and layer of a tenant.</summary>
    public SealedIdentity Seal(TenantId tenant, IdentityLayer layer, Guid subjectId, IdentityDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var associated = AssociatedData(tenant, layer, subjectId);
        var kek = keys.CurrentKey(tenant);
        var dataKey = RandomNumberGenerator.GetBytes(DataKeySize);
        byte[] plaintext = [];
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(StoredDetails.From(details), Json); // deid-ignore: a serializer call, not a device serial
            return new SealedIdentity(
                kek.Id,
                Encrypt(kek.Material, dataKey, associated),
                Encrypt(dataKey, plaintext, associated),
                details.LookupValue(layer) is { } lookup ? BlindIndex(tenant, layer, lookup) : null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(kek.Material);
        }
    }

    /// <summary>Opens a record sealed by <see cref="Seal"/>. Throws <see cref="CryptographicException"/> when it was altered or moved.</summary>
    internal IdentityDetails Unseal(TenantId tenant, IdentityLayer layer, Guid subjectId, SealedIdentity sealedIdentity)
    {
        ArgumentNullException.ThrowIfNull(sealedIdentity);
        var associated = AssociatedData(tenant, layer, subjectId);
        var kek = keys.Key(tenant, sealedIdentity.KeyId).Material;
        byte[] dataKey;
        try
        {
            dataKey = Decrypt(kek, sealedIdentity.WrappedDataKey, associated);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }

        byte[] plaintext = [];
        try
        {
            plaintext = Decrypt(dataKey, sealedIdentity.Ciphertext, associated);
            return (JsonSerializer.Deserialize<StoredDetails>(plaintext, Json)
                ?? throw new CryptographicException("The sealed identity holds no details.")).ToDetails();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>The blind index of a normalized lookup value: equal values in one tenant and layer give equal indexes.</summary>
    public byte[] BlindIndex(TenantId tenant, IdentityLayer layer, string normalizedValue)
    {
        ArgumentNullException.ThrowIfNull(normalizedValue);
        var key = keys.BlindIndexKey(tenant);
        try
        {
            return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{LayerToken(layer)}\n{normalizedValue}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>The layer's token as the vault stores it: <c>person</c> or <c>site_local</c>.</summary>
    public static string LayerToken(IdentityLayer layer) => layer switch
    {
        IdentityLayer.Person => "person",
        IdentityLayer.SiteLocal => "site_local",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown identity layer."),
    };

    private static byte[] AssociatedData(TenantId tenant, IdentityLayer layer, Guid subjectId) =>
        Encoding.UTF8.GetBytes($"cadence.identity_vault.v1|{tenant.Value:D}|{LayerToken(layer)}|{subjectId:D}");

    /// <summary>AES-256-GCM; the output is <c>nonce ‖ ciphertext ‖ tag</c>.</summary>
    private static byte[] Encrypt(byte[] key, byte[] plaintext, byte[] associated)
    {
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize, plaintext.Length), output.AsSpan(NonceSize + plaintext.Length), associated);
        return output;
    }

    private static byte[] Decrypt(byte[] key, byte[] sealedBytes, byte[] associated)
    {
        if (sealedBytes.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The sealed value is truncated.");
        }

        var length = sealedBytes.Length - NonceSize - TagSize;
        var plaintext = new byte[length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            sealedBytes.AsSpan(0, NonceSize),
            sealedBytes.AsSpan(NonceSize, length),
            sealedBytes.AsSpan(NonceSize + length),
            plaintext,
            associated);
        return plaintext;
    }

    /// <summary>The serialized form inside the ciphertext, independent of <see cref="IdentityDetails"/>' shape.</summary>
    private sealed record StoredDetails(
        string? GivenName, string? FamilyName, DateOnly? BirthDate, string? Email, string? Phone, string? SiteLocalNumber)
    {
        public static StoredDetails From(IdentityDetails d) =>
            new(d.GivenName, d.FamilyName, d.BirthDate, d.Email, d.Phone, d.SiteLocalNumber);

        public IdentityDetails ToDetails() => new()
        {
            GivenName = GivenName,
            FamilyName = FamilyName,
            BirthDate = BirthDate,
            Email = Email,
            Phone = Phone,
            SiteLocalNumber = SiteLocalNumber,
        };
    }
}
