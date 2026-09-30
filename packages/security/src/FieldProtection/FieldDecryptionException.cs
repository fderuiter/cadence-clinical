using System;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// Thrown when an encrypted column value cannot be decrypted. Reading fails loudly rather than returning the stored
/// value or <c>null</c>, so a wrong key or a tampered row is never mistaken for real data.
/// @req:PRD-SYS-053
/// </summary>
public sealed class FieldDecryptionException : Exception
{
    public FieldDecryptionException()
    {
    }

    public FieldDecryptionException(string message)
        : base(message)
    {
    }

    public FieldDecryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FieldDecryptionException(string purpose, string reason, Exception? innerException = null)
        : base($"Encrypted field '{purpose}' could not be decrypted: {reason}.", innerException)
    {
        Purpose = purpose;
    }

    /// <summary>The purpose (column) whose value failed to decrypt.</summary>
    public string? Purpose { get; }
}
