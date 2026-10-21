using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Security.Authentication;

/// <summary>Payload claims stored inside a signed biometric liveness token.</summary>
/// <param name="SubjectPseudonym">Subject pseudonym associated with the liveness verification.</param>
/// <param name="AntiSpoofScore">Calculated anti-spoof score.</param>
/// <param name="IsLive">True if facial liveness is verified.</param>
/// <param name="Mode">Verification mode (camera or coordinator_fallback).</param>
/// <param name="IssuedAtUnix">Unix timestamp when token was issued.</param>
/// <param name="ExpiresAtUnix">Unix timestamp when token expires (15 minutes from issue).</param>
/// <param name="Jti">Unique token identifier.</param>
public sealed record BiometricLivenessTokenData(
    [property: JsonPropertyName("sub")] string SubjectPseudonym,
    [property: JsonPropertyName("score")] decimal AntiSpoofScore,
    [property: JsonPropertyName("is_live")] bool IsLive,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("iat")] long IssuedAtUnix,
    [property: JsonPropertyName("exp")] long ExpiresAtUnix,
    [property: JsonPropertyName("jti")] string Jti);

/// <summary>Service for issuing and verifying HMAC-SHA256 signed biometric liveness tokens.</summary>
public static class BiometricLivenessTokenService
{
    private static readonly byte[] DefaultSecret = Encoding.UTF8.GetBytes("CadenceBiometricsLivenessSecret2026Key32Bytes!");

    /// <summary>Issues a signed liveness verification token valid for 15 minutes.</summary>
    public static string IssueToken(
        string subjectPseudonym,
        decimal antiSpoofScore,
        bool isLive,
        string mode = "camera",
        byte[]? secret = null,
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null)
    {
        secret ??= DefaultSecret;
        DateTimeOffset currentTime = now ?? TimeProvider.System.GetUtcNow();
        TimeSpan ttl = lifetime ?? TimeSpan.FromMinutes(15);

        long iat = currentTime.ToUnixTimeSeconds();
        long exp = currentTime.Add(ttl).ToUnixTimeSeconds();

        var payload = new BiometricLivenessTokenData(
            SubjectPseudonym: subjectPseudonym,
            AntiSpoofScore: antiSpoofScore,
            IsLive: isLive,
            Mode: mode,
            IssuedAtUnix: iat,
            ExpiresAtUnix: exp,
            Jti: Guid.NewGuid().ToString());

        string jsonPayload = JsonSerializer.Serialize(payload);
        string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var hmac = new HMACSHA256(secret);
        byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
        string base64Sig = Convert.ToBase64String(sigBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{base64Payload}.{base64Sig}";
    }

    /// <summary>Verifies signature, expiration, and subject of a biometric liveness token.</summary>
    public static bool VerifyToken(
        string? token,
        string? expectedSubject,
        out BiometricLivenessTokenData? tokenData,
        byte[]? secret = null,
        DateTimeOffset? now = null)
    {
        tokenData = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        string base64Payload = parts[0];
        string base64Sig = parts[1];

        secret ??= DefaultSecret;
        using var hmac = new HMACSHA256(secret);
        byte[] expectedSigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
        string expectedBase64Sig = Convert.ToBase64String(expectedSigBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        byte[] sigA = Encoding.ASCII.GetBytes(base64Sig);
        byte[] sigB = Encoding.ASCII.GetBytes(expectedBase64Sig);

        if (sigA.Length != sigB.Length || !CryptographicOperations.FixedTimeEquals(sigA, sigB))
        {
            return false;
        }

        try
        {
            string paddedPayload = base64Payload.Replace('-', '+').Replace('_', '/');
            switch (paddedPayload.Length % 4)
            {
                case 2: paddedPayload += "=="; break;
                case 3: paddedPayload += "="; break;
            }

            byte[] jsonBytes = Convert.FromBase64String(paddedPayload);
            tokenData = JsonSerializer.Deserialize<BiometricLivenessTokenData>(jsonBytes);

            if (tokenData is null)
            {
                return false;
            }

            DateTimeOffset currentTime = now ?? TimeProvider.System.GetUtcNow();
            long currentUnix = currentTime.ToUnixTimeSeconds();

            if (tokenData.ExpiresAtUnix < currentUnix)
            {
                return false;
            }

            if (!tokenData.IsLive)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(expectedSubject) &&
                !string.Equals(tokenData.SubjectPseudonym, expectedSubject, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
