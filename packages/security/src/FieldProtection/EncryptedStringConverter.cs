using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// EF Core value converter that stores a string property encrypted by an <see cref="IFieldEncryptor"/> and decrypts
/// it when read. <c>null</c> stays <c>null</c>. Encrypted columns are not searchable: an equality filter on one
/// compares against a fresh ciphertext and never matches.
/// @req:PRD-SYS-053
/// </summary>
public sealed class EncryptedStringConverter(IFieldEncryptor encryptor, string purpose)
    : ValueConverter<string, string>(
        plaintext => encryptor.Encrypt(plaintext, purpose),
        stored => encryptor.Decrypt(stored, purpose));

/// <summary>Model-building helpers for encrypted columns.</summary>
public static class EncryptedPropertyBuilderExtensions
{
    /// <summary>
    /// Stores the property encrypted with <paramref name="encryptor"/> under <paramref name="purpose"/> and sizes the
    /// column for a plaintext of at most <paramref name="maxPlaintextLength"/> characters.
    /// </summary>
    public static PropertyBuilder<string?> IsEncrypted(
        this PropertyBuilder<string?> property,
        IFieldEncryptor encryptor,
        string purpose,
        int maxPlaintextLength)
    {
        property.Metadata.SetValueConverter(new EncryptedStringConverter(encryptor, purpose));
        return property.HasMaxLength(AesGcmFieldEncryptor.GetMaxStoredLength(maxPlaintextLength));
    }
}
