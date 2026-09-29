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
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        string json = JsonSerializer.Serialize(sortedDict, options);
        return Encoding.UTF8.GetBytes(json);
    }

    public static string GenerateGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        byte[] secret,
        string? changeReason = null,
        string? siteId = null,
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

    public static bool VerifyGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        string signature,
        byte[] secret,
        string? changeReason = null,
        string? siteId = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? tenantId = null,
        string? sigToken = null)
    {
        if (string.IsNullOrWhiteSpace(signature) || secret == null || secret.Length == 0)
        {
            return false;
        }

        // 1. Verify with canonical 9-field scope-aware payload (includes sig_token if present)
        string expected = GenerateGatewaySignature(
            userId, roles, timestamp, secret, changeReason, siteId, sponsorId, unblindedAccess, tenantId, sigToken);

        if (FixedTimeEqualsHex(expected, signature))
        {
            return true;
        }

        // Fallback 0: Try verifying with canonical 8-field scope-aware payload (no sig_token)
        if (sigToken != null)
        {
            string fallback8Field = GenerateGatewaySignature(
                userId, roles, timestamp, secret, changeReason, siteId, sponsorId, unblindedAccess, tenantId, null);
            if (FixedTimeEqualsHex(fallback8Field, signature))
            {
                return true;
            }
        }

        // Fallback 1: Try verifying with tenant_id = null
        bool hasOtherScopes = !string.IsNullOrWhiteSpace(siteId) || !string.IsNullOrWhiteSpace(sponsorId) || unblindedAccess;
        if (!string.IsNullOrWhiteSpace(tenantId) && hasOtherScopes)
        {
            string fallbackTenant = GenerateGatewaySignature(
                userId, roles, timestamp, secret, changeReason, siteId, sponsorId, unblindedAccess, null, sigToken);
            if (FixedTimeEqualsHex(fallbackTenant, signature))
            {
                return true;
            }

            if (sigToken != null)
            {
                string fallbackTenantNoSig = GenerateGatewaySignature(
                    userId, roles, timestamp, secret, changeReason, siteId, sponsorId, unblindedAccess, null, null);
                if (FixedTimeEqualsHex(fallbackTenantNoSig, signature))
                {
                    return true;
                }
            }
        }

        // 2. Scope-free fallbacks: Only permitted if no scope fields are present/active
        bool hasScopes = !string.IsNullOrWhiteSpace(siteId)
            || !string.IsNullOrWhiteSpace(sponsorId)
            || unblindedAccess
            || (!string.IsNullOrWhiteSpace(tenantId) && tenantId != "tenant_default");

        if (!hasScopes)
        {
            string noScopeExpected = GenerateGatewaySignature(
                userId, roles, timestamp, secret, changeReason, null, null, false, null, sigToken);
            if (FixedTimeEqualsHex(noScopeExpected, signature))
            {
                return true;
            }

            if (sigToken != null)
            {
                string noScopeNoSig = GenerateGatewaySignature(
                    userId, roles, timestamp, secret, changeReason, null, null, false, null, null);
                if (FixedTimeEqualsHex(noScopeNoSig, signature))
                {
                    return true;
                }
            }

            // Fallback 3: Legacy 4-field V2 payload
            var legacyPayload = new Dictionary<string, object?>
            {
                ["change_reason"] = changeReason ?? "",
                ["roles"] = roles ?? "",
                ["timestamp"] = timestamp ?? "",
                ["user_id"] = userId ?? ""
            };
            byte[] legacySerialized = CanonicalSerialize(legacyPayload);
            using var hmac = new HMACSHA256(secret);
            string legacyExpected = Convert.ToHexStringLower(hmac.ComputeHash(legacySerialized));
            if (FixedTimeEqualsHex(legacyExpected, signature))
            {
                return true;
            }
        }

        return false;
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
