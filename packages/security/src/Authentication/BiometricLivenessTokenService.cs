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
/// <param name="TenantScope">Verified tenant code bound to the liveness check (<c>tenant_id</c> claim).</param>
/// <param name="IssuerId">Verified caller identifier who initiated the liveness check (<c>issuer_id</c> claim).</param>
/// <param name="StudyScope">Optional verified study code bound to the liveness check (<c>study_id</c> claim).</param>
/// <param name="SiteScope">Optional verified site code bound to the liveness check (<c>site_id</c> claim).</param>
[JsonConverter(typeof(BiometricLivenessTokenDataJsonConverter))]
public sealed record BiometricLivenessTokenData(
    string SubjectPseudonym,
    decimal AntiSpoofScore,
    bool IsLive,
    string Mode,
    long IssuedAtUnix,
    long ExpiresAtUnix,
    string Jti,
    string TenantScope = "",
    string IssuerId = "",
    string? StudyScope = null,
    string? SiteScope = null);

/// <summary>Service for issuing and verifying HMAC-SHA256 signed biometric liveness tokens.</summary>
public static class BiometricLivenessTokenService
{
    /// <summary>Camera-based participant or coordinator liveness mode.</summary>
    public const string CameraMode = "camera";

    /// <summary>Coordinator-attested video fallback liveness mode.</summary>
    public const string CoordinatorFallbackMode = "coordinator_fallback";

    /// <summary>Maximum allowed liveness token lifetime (15 minutes).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Allowed clock skew between issuing and verifying services (5 seconds, matching <see cref="SigTokenVerifier.ClockSkew"/>).</summary>
    public static readonly TimeSpan ClockSkew = SigTokenVerifier.ClockSkew;

    /// <summary>Prefix prepended when recording single-use liveness token IDs in <see cref="ISigTokenUseStore"/>.</summary>
    public const string UseStoreKeyPrefix = "liveness:";

    private const int MaxJtiLength = 128 - 9; // ConsumedSignatureTokenConfiguration.MaxTokenIdLength minus "liveness:".Length

    /// <summary>
    /// Issues a signed liveness verification token valid for up to 15 minutes using the required 32-byte HMAC secret,
    /// binding <paramref name="tenantId"/>, <paramref name="issuerId"/>, and optional <paramref name="studyId"/> and <paramref name="siteId"/>.
    /// </summary>
    public static string IssueToken(
        string subjectPseudonym,
        decimal antiSpoofScore,
        bool isLive,
        string mode = CameraMode,
        byte[]? secret = null,
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null,
        string? tenantId = null,
        string? issuerId = null,
        string? studyId = null,
        string? siteId = null)
    {
        ValidateSecret(secret);
        if (string.IsNullOrWhiteSpace(subjectPseudonym))
        {
            throw new ArgumentException("Subject pseudonym is required.", nameof(subjectPseudonym));
        }

        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new ArgumentException("Liveness verification mode is required.", nameof(mode));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Verified tenant_id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(issuerId))
        {
            throw new ArgumentException("Verified issuer_id is required.", nameof(issuerId));
        }

        string normalizedMode = mode.Trim();
        string? normalizedStudyId = NormalizeOptionalScope(studyId);
        string? normalizedSiteId = NormalizeOptionalScope(siteId);

        if (string.Equals(normalizedMode, CoordinatorFallbackMode, StringComparison.OrdinalIgnoreCase)
            && (normalizedStudyId is null || normalizedSiteId is null))
        {
            throw new ArgumentException("Coordinator fallback liveness tokens must bind both study_id and site_id.");
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
            SubjectPseudonym: subjectPseudonym.Trim(),
            AntiSpoofScore: antiSpoofScore,
            IsLive: isLive,
            Mode: normalizedMode,
            IssuedAtUnix: iat,
            ExpiresAtUnix: exp,
            Jti: Guid.NewGuid().ToString(),
            TenantScope: tenantId.Trim(),
            IssuerId: issuerId.Trim(),
            StudyScope: normalizedStudyId,
            SiteScope: normalizedSiteId);

        string jsonPayload = JsonSerializer.Serialize(
            payload,
            BiometricLivenessJsonContext.Default.BiometricLivenessTokenData);
        string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var hmac = new HMACSHA256(secret!);
        byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload));
        string base64Sig = Convert.ToBase64String(sigBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{base64Payload}.{base64Sig}";
    }

    /// <summary>
    /// Verifies HMAC-SHA256 signature, 15-minute acceptance window, JTI presence, subject binding,
    /// fail-closed <paramref name="expectedTenantId"/> binding, and optional study/site bindings of a biometric liveness token.
    /// </summary>
    public static bool VerifyToken(
        string? token,
        string? expectedSubject,
        out BiometricLivenessTokenData? tokenData,
        byte[]? secret,
        DateTimeOffset? now = null,
        string? expectedTenantId = null,
        string? expectedStudyId = null,
        string? expectedSiteId = null)
    {
        tokenData = null;
        ValidateSecret(secret);

        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(expectedSubject)
            || string.IsNullOrWhiteSpace(expectedTenantId))
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
            var parsed = JsonSerializer.Deserialize(
                jsonBytes,
                BiometricLivenessJsonContext.Default.BiometricLivenessTokenData);

            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.SubjectPseudonym)
                || string.IsNullOrWhiteSpace(parsed.Mode)
                || string.IsNullOrWhiteSpace(parsed.Jti)
                || parsed.Jti.Length > MaxJtiLength
                || string.IsNullOrWhiteSpace(parsed.TenantScope)
                || string.IsNullOrWhiteSpace(parsed.IssuerId))
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

            if (!string.Equals(parsed.SubjectPseudonym.Trim(), expectedSubject.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(parsed.TenantScope.Trim(), expectedTenantId.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            string? tokenStudy = NormalizeOptionalScope(parsed.StudyScope);
            string? tokenSite = NormalizeOptionalScope(parsed.SiteScope);

            if (string.Equals(parsed.Mode.Trim(), CoordinatorFallbackMode, StringComparison.OrdinalIgnoreCase)
                && (tokenStudy is null || tokenSite is null))
            {
                return false;
            }

            if (tokenStudy is not null
                && (string.IsNullOrWhiteSpace(expectedStudyId)
                    || !string.Equals(tokenStudy, expectedStudyId.Trim(), StringComparison.Ordinal)))
            {
                return false;
            }

            if (tokenSite is not null
                && (string.IsNullOrWhiteSpace(expectedSiteId)
                    || !string.Equals(tokenSite, expectedSiteId.Trim(), StringComparison.OrdinalIgnoreCase)))
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

    private static string? NormalizeOptionalScope(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

internal sealed class BiometricLivenessTokenDataJsonConverter : JsonConverter<BiometricLivenessTokenData>
{
    public override BiometricLivenessTokenData? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected JSON object for biometric liveness token payload.");
        }

        string subjectPseudonym = string.Empty;
        decimal antiSpoofScore = 0m;
        bool isLive = false;
        string mode = string.Empty;
        long issuedAtUnix = 0;
        long expiresAtUnix = 0;
        string jti = string.Empty;
        string tenantScope = string.Empty;
        string issuerId = string.Empty;
        string? studyScope = null;
        string? siteScope = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new BiometricLivenessTokenData(
                    SubjectPseudonym: subjectPseudonym,
                    AntiSpoofScore: antiSpoofScore,
                    IsLive: isLive,
                    Mode: mode,
                    IssuedAtUnix: issuedAtUnix,
                    ExpiresAtUnix: expiresAtUnix,
                    Jti: jti,
                    TenantScope: tenantScope,
                    IssuerId: issuerId,
                    StudyScope: studyScope,
                    SiteScope: siteScope);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected JSON property name in biometric liveness token payload.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of JSON in biometric liveness token payload.");
            }

            switch (propertyName)
            {
                case "sub":
                    subjectPseudonym = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
                    break;
                case "score":
                    antiSpoofScore = reader.GetDecimal();
                    break;
                case "is_live":
                    isLive = reader.GetBoolean();
                    break;
                case "mode":
                    mode = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
                    break;
                case "iat":
                    issuedAtUnix = reader.GetInt64();
                    break;
                case "exp":
                    expiresAtUnix = reader.GetInt64();
                    break;
                case "jti":
                    jti = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
                    break;
                case "tenant_id":
                    tenantScope = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
                    break;
                case "issuer_id":
                    issuerId = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
                    break;
                case "study_id":
                    studyScope = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "site_id":
                    siteScope = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unterminated JSON object in biometric liveness token payload.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        BiometricLivenessTokenData value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("sub", value.SubjectPseudonym);
        writer.WriteNumber("score", value.AntiSpoofScore);
        writer.WriteBoolean("is_live", value.IsLive);
        writer.WriteString("mode", value.Mode);
        writer.WriteNumber("iat", value.IssuedAtUnix);
        writer.WriteNumber("exp", value.ExpiresAtUnix);
        writer.WriteString("jti", value.Jti);
        writer.WriteString("tenant_id", value.TenantScope);
        writer.WriteString("issuer_id", value.IssuerId);
        if (!string.IsNullOrWhiteSpace(value.StudyScope))
        {
            writer.WriteString("study_id", value.StudyScope);
        }

        if (!string.IsNullOrWhiteSpace(value.SiteScope))
        {
            writer.WriteString("site_id", value.SiteScope);
        }

        writer.WriteEndObject();
    }
}

[JsonSerializable(typeof(BiometricLivenessTokenData))]
internal sealed partial class BiometricLivenessJsonContext : JsonSerializerContext;
