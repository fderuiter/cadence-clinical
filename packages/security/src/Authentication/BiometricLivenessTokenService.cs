using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Kernel.Time;
using Cadence.Security.FieldProtection;

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
    /// <summary>Maximum allowed liveness token lifetime (15 minutes).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Allowed clock skew between issuing and verifying services (5 seconds, matching <see cref="SigTokenVerifier.ClockSkew"/>).</summary>
    public static readonly TimeSpan ClockSkew = SigTokenVerifier.ClockSkew;

    /// <summary>Prefix prepended when recording single-use liveness token IDs in <see cref="ISigTokenUseStore"/>.</summary>
    public const string UseStoreKeyPrefix = "liveness:";

    private const int MaxJtiLength = 128 - 9; // ConsumedSignatureTokenConfiguration.MaxTokenIdLength minus "liveness:".Length

    /// <summary>Issues a signed liveness verification token valid for up to 15 minutes using the required 32-byte HMAC secret.</summary>
    public static string IssueToken(
        string subjectPseudonym,
        decimal antiSpoofScore,
        bool isLive,
        string mode = "camera",
        byte[]? secret = null,
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null)
    {
        ValidateSecret(secret);
        if (string.IsNullOrWhiteSpace(subjectPseudonym))
        {
            throw new ArgumentException("Subject pseudonym is required.", nameof(subjectPseudonym));
        }

        DateTimeOffset currentTime = now ?? SystemClock.Instance.UtcNow;
        TimeSpan ttl = lifetime ?? MaxLifetime;
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Token lifetime must be positive.");
        }

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

        using var hmac = new HMACSHA256(secret!);
        byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
        string base64Sig = Convert.ToBase64String(sigBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{base64Payload}.{base64Sig}";
    }

    /// <summary>Verifies HMAC-SHA256 signature, 15-minute acceptance window, JTI presence, and subject binding of a biometric liveness token.</summary>
    public static bool VerifyToken(
        string? token,
        string? expectedSubject,
        out BiometricLivenessTokenData? tokenData,
        byte[]? secret,
        DateTimeOffset? now = null)
    {
        tokenData = null;
        ValidateSecret(secret);

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

        using var hmac = new HMACSHA256(secret!);
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
            var parsed = JsonSerializer.Deserialize<BiometricLivenessTokenData>(jsonBytes);

            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.SubjectPseudonym)
                || string.IsNullOrWhiteSpace(parsed.Jti)
                || parsed.Jti.Length > MaxJtiLength)
            {
                return false;
            }

            DateTimeOffset currentTime = now ?? SystemClock.Instance.UtcNow;
            DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(parsed.IssuedAtUnix);
            DateTimeOffset expires = DateTimeOffset.FromUnixTimeSeconds(parsed.ExpiresAtUnix);

            if (!WithinAcceptanceWindow(issuedAt, expires, currentTime))
            {
                return false;
            }

            if (!parsed.IsLive)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(expectedSubject) &&
                !string.Equals(parsed.SubjectPseudonym, expectedSubject, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            tokenData = parsed;
            return true;
        }
        catch
        {
            tokenData = null;
            return false;
        }
    }

    private static bool WithinAcceptanceWindow(
        DateTimeOffset issuedAt,
        DateTimeOffset expires,
        DateTimeOffset now) =>
        expires > issuedAt
        && expires - issuedAt <= MaxLifetime
        && now >= issuedAt - ClockSkew
        && now < expires + ClockSkew;

    private static void ValidateSecret(byte[]? secret)
    {
        if (secret is null || secret.Length != FieldProtectionKey.KeySizeBytes)
        {
            throw new ArgumentException(
                $"A {FieldProtectionKey.KeySizeBytes}-byte HMAC secret ({BiometricLivenessOptions.ConfigurationKey}) is required.",
                nameof(secret));
        }
    }
}
