using System;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// Stand-in <see cref="IFieldEncryptor"/> for <c>IDesignTimeDbContextFactory</c> implementations. The <c>dotnet ef</c>
/// tools only build the model (column names, types and lengths) and never convert values, so they need no key.
/// Any attempt to encrypt or decrypt throws, so this type can never become a keyless fallback at runtime.
/// @req:PRD-SYS-053
/// </summary>
public sealed class DesignTimeFieldEncryptor : IFieldEncryptor
{
    /// <summary>Shared instance.</summary>
    public static DesignTimeFieldEncryptor Instance { get; } = new();

    private DesignTimeFieldEncryptor()
    {
    }

    /// <inheritdoc />
    public string KeyId => "design-time";

    /// <inheritdoc />
    public string Encrypt(string plaintext, string purpose) => throw NotAvailable();

    /// <inheritdoc />
    public string Decrypt(string protectedValue, string purpose) => throw NotAvailable();

    private static InvalidOperationException NotAvailable() =>
        new($"The design-time field encryptor cannot encrypt or decrypt values; configure {FieldEncryptionOptions.ConfigurationKey} and resolve the context from dependency injection.");
}
