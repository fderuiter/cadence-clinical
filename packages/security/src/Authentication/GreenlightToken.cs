using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Kernel.Time;

namespace Cadence.Security.Authentication;

/// <summary>
/// Strong-typed HMAC-SHA256 signed Greenlight Activation Token payload.
/// Compatible with packages/security/signing.py's generate_greenlight_token/verify_greenlight_token.
/// </summary>
public sealed record GreenlightTokenPayload(
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("certified_by")] string CertifiedBy,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("token_version")] string TokenVersion,
    [property: JsonPropertyName("is_overridden")] bool IsOverridden,
    [property: JsonPropertyName("issued_at")] long IssuedAt,
    [property: JsonPropertyName("expires_at")] long ExpiresAt,
    [property: JsonPropertyName("facet_hash")] string? FacetHash = null
);

/// <summary>
/// Issues and verifies cryptographic HMAC SHA-256 Site Greenlight Activation Tokens.
/// @req:PRD-SSU-001, PRD-SSU-003, 21 CFR Part 11
/// </summary>
public static class GreenlightToken
{
    public const string TokenType = "SITE_GREENLIGHT_ACTIVATION";
    public const string TokenVersion = "1";

    /// <summary>
    /// Generates an HMAC SHA-256 signed site greenlight token.
    /// </summary>
    public static string Generate(
        string studyId,
        string siteId,
        string certifiedBy,
        bool isOverridden = false,
        string countryCode = "US",
        string? facetHash = null,
        TimeSpan? lifetime = null,
        byte[]? secret = null,
        IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        byte[] keyBytes = secret is { Length: > 0 } ? secret : GetSecretFromEnv();
        long nowSeconds = (clock ?? SystemClock.Instance).UtcNow.ToUnixTimeSeconds();
        long expiresSeconds = nowSeconds + (long)(lifetime ?? TimeSpan.FromDays(365)).TotalSeconds;

        var payload = new GreenlightTokenPayload(
            SiteId: siteId,
            StudyId: studyId,
            CountryCode: countryCode.ToUpperInvariant(),
            CertifiedBy: certifiedBy,
            TokenType: TokenType,
            TokenVersion: TokenVersion,
            IsOverridden: isOverridden,
            IssuedAt: nowSeconds,
            ExpiresAt: expiresSeconds,
            FacetHash: facetHash ?? string.Empty
        );

        var dict = new Dictionary<string, object?>
        {
            ["site_id"] = payload.SiteId,
            ["study_id"] = payload.StudyId,
            ["country_code"] = payload.CountryCode,
            ["certified_by"] = payload.CertifiedBy,
            ["token_type"] = payload.TokenType,
            ["token_version"] = payload.TokenVersion,
            ["is_overridden"] = payload.IsOverridden,
            ["issued_at"] = payload.IssuedAt,
            ["expires_at"] = payload.ExpiresAt,
        };

        byte[] serializedJson = HmacSigner.CanonicalSerialize(dict);
        string b64Payload = Convert.ToBase64String(serializedJson)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        string signature = Convert.ToHexStringLower(hash);

        return $"{b64Payload}.{signature}";
    }

    /// <summary>
    /// Verifies HMAC SHA-256 signature, expiry, and study/site binding of a greenlight token.
    /// </summary>
    public static GreenlightTokenPayload? Verify(
        string? token,
        string? expectedStudyId = null,
        string? expectedSiteId = null,
        byte[]? secret = null,
        DateTimeOffset? now = null,
        IClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        string b64Payload = parts[0];
        string signature = parts[1];

        byte[] keyBytes = secret is { Length: > 0 } ? secret : GetSecretFromEnv();

        using var hmac = new HMACSHA256(keyBytes);
        byte[] computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        string expectedSignature = Convert.ToHexStringLower(computedHash);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant())))
        {
            return null;
        }

        try
        {
            string paddedB64 = b64Payload.Replace('-', '+').Replace('_', '/');
            int mod4 = paddedB64.Length % 4;
            if (mod4 > 0)
            {
                paddedB64 += new string('=', 4 - mod4);
            }

            byte[] jsonBytes = Convert.FromBase64String(paddedB64);
            var payload = JsonSerializer.Deserialize<GreenlightTokenPayload>(jsonBytes);

            if (payload == null)
            {
                return null;
            }

            if (payload.TokenType != TokenType)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(expectedStudyId) &&
                !string.Equals(payload.StudyId, expectedStudyId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(expectedSiteId) &&
                !string.Equals(payload.SiteId, expectedSiteId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            long currentSeconds = (now ?? (clock ?? SystemClock.Instance).UtcNow).ToUnixTimeSeconds();
            if (payload.ExpiresAt < currentSeconds)
            {
                return null;
            }

            return payload;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] GetSecretFromEnv()
    {
        string? raw = Environment.GetEnvironmentVariable("GATEWAY_SECRET")
            ?? Environment.GetEnvironmentVariable("INTERNAL_GATEWAY_SECRET")
            ?? "internal-gateway-secret-abcdefabcdef!!";
        return Encoding.UTF8.GetBytes(raw);
    }
}
