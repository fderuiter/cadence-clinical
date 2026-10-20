using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Security.Authentication;

/// <summary>
/// High-throughput HMAC-SHA256 signature generation and validation for inter-service Gateway authentication and canonical serialization.
/// @req:PRD-SYS-053
/// </summary>
public static class HmacSigner
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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

    public static byte[] CanonicalSerialize(IDictionary<string, object?> payload)
    {
        var sortedDict = new SortedDictionary<string, object?>(payload, StringComparer.Ordinal);
        string json = JsonSerializer.Serialize(sortedDict, CanonicalJsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    public static string GenerateGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        byte[] secret,
        string? changeReason = null,
        string? siteId = null,
        string? studyIds = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? tenantId = null,
        string? sigToken = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["change_reason"] = changeReason ?? "",
            ["roles"] = roles ?? "",
            ["timestamp"] = timestamp ?? "",
            ["user_id"] = userId ?? "",
            ["site_id"] = siteId ?? "",
            ["study_ids"] = studyIds ?? "",
            ["sponsor_id"] = sponsorId ?? "",
            ["unblinded_access"] = unblindedAccess,
            ["tenant_id"] = tenantId ?? ""
        };

        if (sigToken != null)
        {
            payload["sig_token"] = sigToken;
        }

        byte[] serialized = CanonicalSerialize(payload);
        using var hmac = new HMACSHA256(secret);
        byte[] hash = hmac.ComputeHash(serialized);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Verifies a gateway identity signature against the one canonical payload
    /// <see cref="GenerateGatewaySignature"/> produces. There are deliberately no fallback payload shapes: every
    /// signed field must match exactly, so a caller cannot drop a scope field (site, study, sponsor, tenant, unblinded
    /// access) and still pass verification.
    /// </summary>
    public static bool VerifyGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        string signature,
        byte[] secret,
        string? changeReason = null,
        string? siteId = null,
        string? studyIds = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? tenantId = null,
        string? sigToken = null)
    {
        if (string.IsNullOrWhiteSpace(signature) || secret == null || secret.Length == 0)
        {
            return false;
        }

        string expected = GenerateGatewaySignature(
            userId, roles, timestamp, secret, changeReason, siteId, studyIds, sponsorId, unblindedAccess, tenantId, sigToken);

        return FixedTimeEqualsHex(expected, signature);
    }

    public static string ComputeSha256Hash(string input)
    {
        if (input == null) return string.Empty;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeSha256Hash(byte[] input)
    {
        if (input == null || input.Length == 0) return string.Empty;
        byte[] hash = SHA256.HashData(input);
        return Convert.ToHexStringLower(hash);
    }

    private static bool FixedTimeEqualsHex(string sig1, string sig2)
    {
        if (string.IsNullOrWhiteSpace(sig1) || string.IsNullOrWhiteSpace(sig2)) return false;
        try
        {
            byte[] b1 = Encoding.UTF8.GetBytes(sig1.ToLowerInvariant());
            byte[] b2 = Encoding.UTF8.GetBytes(sig2.ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(b1, b2);
        }
        catch
        {
            return false;
        }
    }
}
