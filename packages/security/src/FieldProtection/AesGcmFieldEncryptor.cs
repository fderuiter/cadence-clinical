using System;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// <see cref="IFieldEncryptor"/> using AES-256-GCM with the key from <see cref="FieldEncryptionOptions"/>.
/// <para>
/// Stored format: <c>enc:v1:&lt;keyId&gt;:&lt;base64(nonce ‖ ciphertext ‖ tag)&gt;</c>. A fresh 96-bit random nonce is
/// drawn for every value and the 128-bit tag authenticates both the ciphertext and the associated data
/// <c>enc:v1:&lt;keyId&gt;:&lt;purpose&gt;</c>, so a value moved to another column, or relabelled with another key id,
/// fails to decrypt. The key id lets a later key rotation tell which key protected a value.
/// </para>
/// @req:PRD-SYS-053
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    /// <summary>Prefix of every value this encryptor writes.</summary>
    public const string FormatPrefix = "enc:v1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeyIdLength = 16;

    private readonly byte[] _key;

    /// <summary>Creates the encryptor from bound options; a missing or invalid key throws.</summary>
    public AesGcmFieldEncryptor(IOptions<FieldEncryptionOptions> options)
        : this(options?.Value.KeyBase64)
    {
    }

    /// <summary>Creates the encryptor from a base64-encoded 32-byte key; a missing or invalid key throws.</summary>
    public AesGcmFieldEncryptor(string? base64Key)
    {
        _key = FieldProtectionKey.Decode(base64Key, FieldEncryptionOptions.ConfigurationKey);
        KeyId = FieldProtectionKey.ComputeKeyId(_key, "cadence.field-encryption.key-id.v1");
    }

    /// <inheritdoc />
    public string KeyId { get; }

    /// <summary>
    /// The largest stored length of a value whose plaintext is at most <paramref name="maxPlaintextChars"/>
    /// characters (assuming the worst case of 4 UTF-8 bytes per character). Use it to size encrypted columns.
    /// </summary>
    public static int GetMaxStoredLength(int maxPlaintextChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxPlaintextChars);
        int maxBytes = NonceSize + (maxPlaintextChars * 4) + TagSize;
        int base64Length = 4 * ((maxBytes + 2) / 3);
        return FormatPrefix.Length + KeyIdLength + 1 + base64Length;
    }

    /// <inheritdoc />
    public string Encrypt(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] blob = new byte[NonceSize + plaintextBytes.Length + TagSize];
        Span<byte> nonce = blob.AsSpan(0, NonceSize);
        Span<byte> ciphertext = blob.AsSpan(NonceSize, plaintextBytes.Length);
        Span<byte> tag = blob.AsSpan(NonceSize + plaintextBytes.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, AssociatedData(KeyId, purpose));
        }

        CryptographicOperations.ZeroMemory(plaintextBytes);
        return $"{FormatPrefix}{KeyId}:{Convert.ToBase64String(blob)}";
    }

    /// <inheritdoc />
    public string Decrypt(string protectedValue, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (!protectedValue.StartsWith(FormatPrefix, StringComparison.Ordinal))
        {
            throw new FieldDecryptionException(purpose, "the stored value is not in the encrypted field format");
        }

        int separator = protectedValue.IndexOf(':', FormatPrefix.Length);
        if (separator < 0)
        {
            throw new FieldDecryptionException(purpose, "the stored value has no key id");
        }

        string keyId = protectedValue[FormatPrefix.Length..separator];
        if (!string.Equals(keyId, KeyId, StringComparison.Ordinal))
        {
            throw new FieldDecryptionException(purpose, $"the value was encrypted under key '{keyId}', not the configured key '{KeyId}'");
        }

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(protectedValue[(separator + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new FieldDecryptionException(purpose, "the ciphertext is not valid base64", ex);
        }

        if (blob.Length < NonceSize + TagSize)
        {
            throw new FieldDecryptionException(purpose, "the ciphertext is truncated");
        }

        ReadOnlySpan<byte> nonce = blob.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> ciphertext = blob.AsSpan(NonceSize, blob.Length - NonceSize - TagSize);
        ReadOnlySpan<byte> tag = blob.AsSpan(blob.Length - TagSize, TagSize);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(keyId, purpose));
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new FieldDecryptionException(purpose, "authentication failed (wrong key or purpose, or the value was altered)", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(string keyId, string purpose) =>
        Encoding.UTF8.GetBytes($"{FormatPrefix}{keyId}:{purpose}");
}
