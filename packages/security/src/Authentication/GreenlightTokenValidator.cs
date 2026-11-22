using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Kernel.Time;

namespace Cadence.Security.Authentication;

/// <summary>
/// Outcome of verifying a CTMS startup <c>GreenlightToken</c> before activating a clinical site (PRD-SSU-003).
/// </summary>
public sealed record GreenlightTokenValidationResult(
    bool IsValid,
    string? FailureReason = null,
    string? SiteCode = null,
    string? StudyCode = null,
    string? CountryCode = null,
    string? CertifiedBy = null,
    long? IssuedAt = null,
    long? ExpiresAt = null,
    bool IsOverridden = false)
{
    public static GreenlightTokenValidationResult Invalid(string reason) => new(false, FailureReason: reason);
}

/// <summary>
/// Verifies HMAC-SHA256 startup greenlight tokens issued by CTMS (<c>SITE_GREENLIGHT_ACTIVATION</c>, PRD-SSU-003).
/// </summary>
public interface IGreenlightTokenValidator
{
    GreenlightTokenValidationResult Validate(string? token, string expectedSiteCode, string expectedStudyCode);
}

/// <summary>
/// Constant-time HMAC-SHA256 verifier and canonical generator for CTMS startup greenlight tokens (PRD-SSU-003).
/// </summary>
public sealed class GreenlightTokenValidator : IGreenlightTokenValidator
{
    public const string ExpectedTokenType = "SITE_GREENLIGHT_ACTIVATION";

    private readonly byte[] _secretBytes;
    private readonly IClock _clock;

    public GreenlightTokenValidator(string secret, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(clock);
        _secretBytes = Encoding.UTF8.GetBytes(secret);
        _clock = clock;
    }

    public static string GenerateToken(
        string siteCode,
        string studyCode,
        string countryCode,
        string certifiedBy,
        string secret,
        long issuedAt,
        long expiresAt,
        bool isOverridden = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var payloadObj = new Dictionary<string, object?>
        {
            ["certified_by"] = certifiedBy,
            ["country_code"] = countryCode.ToUpperInvariant(),
            ["expires_at"] = expiresAt,
            ["is_overridden"] = isOverridden,
            ["issued_at"] = issuedAt,
            ["site_id"] = siteCode,
            ["study_id"] = studyCode,
            ["token_type"] = ExpectedTokenType,
            ["token_version"] = "1",
        };

        string json = JsonSerializer.Serialize(payloadObj);
        string b64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        string hexSig = Convert.ToHexStringLower(hashBytes);
        return $"{b64Payload}.{hexSig}";
    }

    public GreenlightTokenValidationResult Validate(string? token, string expectedSiteCode, string expectedStudyCode)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return GreenlightTokenValidationResult.Invalid("Missing startup greenlight certification token.");
        }

        int dotIndex = token.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex <= 0 || dotIndex != token.LastIndexOf('.'))
        {
            return GreenlightTokenValidationResult.Invalid("Malformed startup greenlight certification token.");
        }

        string b64Payload = token[..dotIndex];
        string hexSignature = token[(dotIndex + 1)..];
        if (hexSignature.Length != 64)
        {
            return GreenlightTokenValidationResult.Invalid("Malformed startup greenlight token signature.");
        }

        byte[] providedSigBytes;
        try
        {
            providedSigBytes = Convert.FromHexString(hexSignature);
        }
        catch (FormatException)
        {
            return GreenlightTokenValidationResult.Invalid("Malformed startup greenlight token signature.");
        }

        byte[] expectedSigBytes;
        using (var hmac = new HMACSHA256(_secretBytes))
        {
            expectedSigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedSigBytes, providedSigBytes))
        {
            return GreenlightTokenValidationResult.Invalid("Startup greenlight token HMAC signature verification failed.");
        }

        byte[] payloadBytes;
        try
        {
            string padded = b64Payload.Replace('-', '+').Replace('_', '/');
            int mod = padded.Length % 4;
            if (mod == 2)
            {
                padded += "==";
            }
            else if (mod == 3)
            {
                padded += "=";
            }
            else if (mod == 1)
            {
                return GreenlightTokenValidationResult.Invalid("Malformed startup greenlight token payload.");
            }

            payloadBytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return GreenlightTokenValidationResult.Invalid("Malformed startup greenlight token payload.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return GreenlightTokenValidationResult.Invalid("Startup greenlight token payload must be a JSON object.");
            }

            string? tokenType = root.TryGetProperty("token_type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString()
                : null;
            if (!string.Equals(tokenType, ExpectedTokenType, StringComparison.Ordinal))
            {
                return GreenlightTokenValidationResult.Invalid("Startup greenlight token has an unexpected token_type.");
            }

            string? siteCode = root.TryGetProperty("site_id", out var siteEl) && siteEl.ValueKind == JsonValueKind.String
                ? siteEl.GetString()
                : null;
            if (!string.Equals(siteCode, expectedSiteCode, StringComparison.Ordinal))
            {
                return GreenlightTokenValidationResult.Invalid($"Startup greenlight token site '{siteCode}' does not match '{expectedSiteCode}'.");
            }

            string? studyCode = root.TryGetProperty("study_id", out var studyEl) && studyEl.ValueKind == JsonValueKind.String
                ? studyEl.GetString()
                : null;
            if (!string.Equals(studyCode, expectedStudyCode, StringComparison.Ordinal))
            {
                return GreenlightTokenValidationResult.Invalid($"Startup greenlight token study '{studyCode}' does not match '{expectedStudyCode}'.");
            }

            if (!root.TryGetProperty("expires_at", out var expiresEl) || !expiresEl.TryGetInt64(out long expiresAt))
            {
                return GreenlightTokenValidationResult.Invalid("Startup greenlight token is missing expires_at.");
            }

            long nowUnix = _clock.UtcNow.ToUnixTimeSeconds();
            if (expiresAt <= nowUnix)
            {
                return GreenlightTokenValidationResult.Invalid("Startup greenlight certification token has expired.");
            }

            long? issuedAt = root.TryGetProperty("issued_at", out var issuedEl) && issuedEl.TryGetInt64(out long issuedVal)
                ? issuedVal
                : null;
            string? countryCode = root.TryGetProperty("country_code", out var countryEl) && countryEl.ValueKind == JsonValueKind.String
                ? countryEl.GetString()
                : null;
            string? certifiedBy = root.TryGetProperty("certified_by", out var certEl) && certEl.ValueKind == JsonValueKind.String
                ? certEl.GetString()
                : null;
            bool isOverridden = root.TryGetProperty("is_overridden", out var overrideEl) && overrideEl.ValueKind == JsonValueKind.True;

            return new GreenlightTokenValidationResult(
                IsValid: true,
                FailureReason: null,
                SiteCode: siteCode,
                StudyCode: studyCode,
                CountryCode: countryCode,
                CertifiedBy: certifiedBy,
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                IsOverridden: isOverridden);
        }
        catch (JsonException)
        {
            return GreenlightTokenValidationResult.Invalid("Startup greenlight token payload is not valid JSON.");
        }
    }
}
