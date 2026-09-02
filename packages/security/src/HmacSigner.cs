using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Security;

/// <summary>
/// Provides high-throughput HMAC-SHA256 signature generation and validation for inter-service Gateway authentication.
/// @req:PRD-SYS-053
/// </summary>
public static class HmacSigner
{
    public static string ComputeSignature(string payload, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexStringLower(hash);
    }

    public static bool VerifySignature(string payload, string secret, string expectedSignature)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(expectedSignature))
        {
            return false;
        }

        string actualSignature = ComputeSignature(payload, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualSignature),
            Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant())
        );
    }
}

