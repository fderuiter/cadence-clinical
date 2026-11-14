using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Apps.Ctms.Domain;

public static class GreenlightTokenGenerator
{
    public static string GenerateToken(
        string siteId,
        string studyId,
        string countryCode,
        string certifiedBy,
        string secret,
        long issuedAt,
        long expiresAt,
        bool isOverridden = false)
    {
        var payloadObj = new Dictionary<string, object?>
        {
            ["certified_by"] = certifiedBy,
            ["country_code"] = countryCode.ToUpperInvariant(),
            ["expires_at"] = expiresAt,
            ["is_overridden"] = isOverridden,
            ["issued_at"] = issuedAt,
            ["site_id"] = siteId,
            ["study_id"] = studyId,
            ["token_type"] = "SITE_GREENLIGHT_ACTIVATION",
            ["token_version"] = "1"
        };

        string json = JsonSerializer.Serialize(payloadObj);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        string b64Payload = Convert.ToBase64String(jsonBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        string hexSig = Convert.ToHexStringLower(hashBytes);

        return $"{b64Payload}.{hexSig}";
    }
}
