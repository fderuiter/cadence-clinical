namespace Cadence.Security.FieldProtection;

/// <summary>
/// Computes a keyed hash (HMAC-SHA256, a "blind index") of a secret value so it can be stored and matched by equality
/// without storing the value itself. Without the key the stored hash cannot be reversed or brute-forced from a list
/// of candidate values. The same value and purpose always give the same hash under the same key.
/// @req:PRD-SYS-053
/// </summary>
public interface ILookupHasher
{
    /// <summary>Length of the returned hash: 64 lowercase hexadecimal characters.</summary>
    const int HashLength = 64;

    /// <summary>A non-secret identifier of the active key.</summary>
    string KeyId { get; }

    /// <summary>Returns the lowercase hex HMAC-SHA256 of <paramref name="value"/> under <paramref name="purpose"/>.</summary>
    string ComputeHash(string value, string purpose);
}
