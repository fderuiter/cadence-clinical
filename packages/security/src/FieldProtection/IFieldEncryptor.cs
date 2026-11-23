using System.Collections.Generic;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// Encrypts individual sensitive column values (payment processor references, signer e-mail addresses) before they
/// reach the database, and decrypts them on read. Every value is bound to a <c>purpose</c> string (typically
/// <c>&lt;service&gt;.&lt;table&gt;.&lt;column&gt;</c>), so ciphertext copied into another column fails to decrypt.
/// Encryption is randomised: the same plaintext never produces the same stored value, so encrypted columns cannot be
/// searched by equality. Use <see cref="ILookupHasher"/> for values that must be looked up.
/// @req:PRD-SYS-053
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>A non-secret identifier of the active key, embedded in every value this encryptor produces.</summary>
    string KeyId { get; }

    /// <summary>Encrypts <paramref name="plaintext"/> for storage.</summary>
    string Encrypt(string plaintext, string purpose);

    /// <summary>
    /// Encrypts a batch of <c>(Plaintext, Purpose)</c> pairs for storage, reusing one cipher context for the batch
    /// while drawing a fresh random nonce for every value.
    /// </summary>
    IReadOnlyList<string> EncryptBatch(IReadOnlyList<(string Plaintext, string Purpose)> items);

    /// <summary>
    /// Decrypts a value produced by <see cref="Encrypt"/> with the same key and purpose.
    /// </summary>
    /// <exception cref="FieldDecryptionException">
    /// The value is not in the protected format, was produced under another key or purpose, or was tampered with.
    /// </exception>
    string Decrypt(string protectedValue, string purpose);
}
