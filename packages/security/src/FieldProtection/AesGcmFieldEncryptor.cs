using System;
using System.Buffers;
using System.Collections.Generic;
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
    private const int MaxStackPlaintextBytes = 512;
    private const int MaxStackPurposeBytes = 512;

    private readonly byte[] _key;
    private readonly string _entryPrefix;
    private readonly byte[] _associatedDataPrefix;

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
        _entryPrefix = $"{FormatPrefix}{KeyId}:";
        _associatedDataPrefix = Encoding.UTF8.GetBytes(_entryPrefix);
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

        using var aes = new AesGcm(_key, TagSize);
        Span<byte> scratchPlaintext = stackalloc byte[MaxStackPlaintextBytes];
        Span<byte> scratchAd = stackalloc byte[_associatedDataPrefix.Length + MaxStackPurposeBytes];
        Span<byte> scratchBlob = stackalloc byte[NonceSize + MaxStackPlaintextBytes + TagSize];
        Span<char> scratchChars = stackalloc char[_entryPrefix.Length + (4 * ((NonceSize + MaxStackPlaintextBytes + TagSize + 2) / 3))];
        _associatedDataPrefix.CopyTo(scratchAd);
        _entryPrefix.AsSpan().CopyTo(scratchChars);

        return EncryptCore(aes, plaintext, purpose, scratchPlaintext, scratchAd, scratchBlob, scratchChars);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EncryptBatch(IReadOnlyList<(string Plaintext, string Purpose)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return [];
        }

        for (int i = 0; i < items.Count; i++)
        {
            var (plaintext, purpose) = items[i];
            ArgumentNullException.ThrowIfNull(plaintext);
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        }

        var results = new string[items.Count];
        using var aes = new AesGcm(_key, TagSize);

        Span<byte> scratchPlaintext = stackalloc byte[MaxStackPlaintextBytes];
        Span<byte> scratchAd = stackalloc byte[_associatedDataPrefix.Length + MaxStackPurposeBytes];
        Span<byte> scratchBlob = stackalloc byte[NonceSize + MaxStackPlaintextBytes + TagSize];
        Span<char> scratchChars = stackalloc char[_entryPrefix.Length + (4 * ((NonceSize + MaxStackPlaintextBytes + TagSize + 2) / 3))];
        _associatedDataPrefix.CopyTo(scratchAd);
        _entryPrefix.AsSpan().CopyTo(scratchChars);

        for (int i = 0; i < items.Count; i++)
        {
            var (plaintext, purpose) = items[i];
            results[i] = EncryptCore(aes, plaintext, purpose, scratchPlaintext, scratchAd, scratchBlob, scratchChars);
        }

        return results;
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

    private string EncryptCore(
        AesGcm aes,
        string plaintext,
        string purpose,
        Span<byte> scratchPlaintext,
        Span<byte> scratchAd,
        Span<byte> scratchBlob,
        Span<char> scratchChars)
    {
        int plaintextByteCount = Encoding.UTF8.GetByteCount(plaintext);
        int purposeByteCount = Encoding.UTF8.GetByteCount(purpose);
        if (plaintextByteCount <= MaxStackPlaintextBytes && purposeByteCount <= MaxStackPurposeBytes)
        {
            Span<byte> plaintextSpan = scratchPlaintext[..plaintextByteCount];
            int adLength = _associatedDataPrefix.Length + purposeByteCount;
            int blobLength = NonceSize + plaintextByteCount + TagSize;
            Span<byte> blobSpan = scratchBlob[..blobLength];

            try
            {
                Encoding.UTF8.GetBytes(plaintext.AsSpan(), plaintextSpan);
                Encoding.UTF8.GetBytes(purpose.AsSpan(), scratchAd.Slice(_associatedDataPrefix.Length, purposeByteCount));
                EncryptIntoBlob(aes, plaintextSpan, scratchAd[..adLength], blobSpan);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintextSpan);
            }

            Convert.TryToBase64Chars(blobSpan, scratchChars[_entryPrefix.Length..], out int charsWritten);
            return new string(scratchChars[..(_entryPrefix.Length + charsWritten)]);
        }

        return EncryptRented(aes, plaintext, purpose, plaintextByteCount, purposeByteCount);
    }

    private string EncryptRented(AesGcm aes, string plaintext, string purpose, int plaintextByteCount, int purposeByteCount)
    {
        int adLength = _associatedDataPrefix.Length + purposeByteCount;
        int blobLength = NonceSize + plaintextByteCount + TagSize;
        int outputLength = _entryPrefix.Length + (4 * ((blobLength + 2) / 3));

        byte[] rentedPlaintext = ArrayPool<byte>.Shared.Rent(plaintextByteCount);
        byte[] rentedAd = ArrayPool<byte>.Shared.Rent(adLength);
        byte[] rentedBlob = ArrayPool<byte>.Shared.Rent(blobLength);
        char[] rentedChars = ArrayPool<char>.Shared.Rent(outputLength);

        try
        {
            Span<byte> plaintextSpan = rentedPlaintext.AsSpan(0, plaintextByteCount);
            Span<byte> adSpan = rentedAd.AsSpan(0, adLength);
            Span<byte> blobSpan = rentedBlob.AsSpan(0, blobLength);

            Encoding.UTF8.GetBytes(plaintext.AsSpan(), plaintextSpan);
            _associatedDataPrefix.CopyTo(adSpan);
            Encoding.UTF8.GetBytes(purpose.AsSpan(), adSpan[_associatedDataPrefix.Length..]);

            EncryptIntoBlob(aes, plaintextSpan, adSpan, blobSpan);

            _entryPrefix.AsSpan().CopyTo(rentedChars);
            Convert.TryToBase64Chars(blobSpan, rentedChars.AsSpan(_entryPrefix.Length), out int charsWritten);
            return new string(rentedChars, 0, _entryPrefix.Length + charsWritten);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rentedPlaintext.AsSpan(0, plaintextByteCount));
            ArrayPool<byte>.Shared.Return(rentedPlaintext);
            ArrayPool<byte>.Shared.Return(rentedAd);
            ArrayPool<byte>.Shared.Return(rentedBlob);
            ArrayPool<char>.Shared.Return(rentedChars);
        }
    }

    private static void EncryptIntoBlob(AesGcm aes, ReadOnlySpan<byte> plaintextBytes, ReadOnlySpan<byte> associatedData, Span<byte> blob)
    {
        Span<byte> nonce = blob[..NonceSize];
        Span<byte> ciphertext = blob.Slice(NonceSize, plaintextBytes.Length);
        Span<byte> tag = blob.Slice(NonceSize + plaintextBytes.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);
    }

    private static byte[] AssociatedData(string keyId, string purpose) =>
        Encoding.UTF8.GetBytes($"{FormatPrefix}{keyId}:{purpose}");
}
