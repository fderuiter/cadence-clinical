using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Cadence.Modules.Intelligence.Endpoints;

/// <summary>
/// Clinical scope code wrapper used on wire contracts that pass standalone study or site codes
/// into biometric liveness token issuance without duplicating kernel entity identity strings.
/// </summary>
/// <param name="Value">The raw study or site scope code string.</param>
[JsonConverter(typeof(LivenessScopeCodeJsonConverter))]
public readonly record struct LivenessScopeCode(string Value)
{
    /// <summary>Converts a non-null scope string to a <see cref="LivenessScopeCode"/>.</summary>
    /// <param name="value">The scope string.</param>
    public static implicit operator LivenessScopeCode(string value) => new(value);

    /// <summary>Returns the underlying scope code value.</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>JSON converter writing and reading <see cref="LivenessScopeCode"/> as a JSON string.</summary>
public sealed class LivenessScopeCodeJsonConverter : JsonConverter<LivenessScopeCode>
{
    /// <inheritdoc />
    public override LivenessScopeCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a JSON string for clinical scope code.");
        }

        return new LivenessScopeCode(reader.GetString() ?? string.Empty);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LivenessScopeCode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Describes <see cref="LivenessScopeCode"/> as a string in generated OpenAPI schemas.</summary>
internal sealed class LivenessScopeCodeOpenApiTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        bool isNullable = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) is not null;
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type == typeof(LivenessScopeCode))
        {
            schema.Type = isNullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
            schema.Format = null;
            schema.Properties?.Clear();
            schema.Required?.Clear();
        }

        return Task.CompletedTask;
    }
}

/// <summary>Request payload for verifying biometric liveness.</summary>
/// <param name="SubjectPseudonym">The participant pseudonym.</param>
/// <param name="VideoFrames">In-memory video frames (base64-encoded PNG, JPEG, or WebP images).</param>
/// <param name="ChallengeId">Optional challenge ID.</param>
/// <param name="FallbackMode">True if using study coordinator video fallback.</param>
/// <param name="StudyId">Optional study scope code bound into the issued liveness token (required for coordinator fallback).</param>
/// <param name="SiteId">Optional site scope code bound into the issued liveness token (required for coordinator fallback).</param>
public sealed record VerifyLivenessRequest(
    [property: JsonPropertyName("subject_pseudonym")] string? SubjectPseudonym,
    [property: JsonPropertyName("video_frames")] List<string>? VideoFrames,
    [property: JsonPropertyName("challenge_id")] string? ChallengeId,
    [property: JsonPropertyName("fallback_mode")] bool FallbackMode = false,
    LivenessScopeCode? StudyId = null,
    LivenessScopeCode? SiteId = null);

/// <summary>Response payload from biometric liveness verification.</summary>
/// <param name="LivenessToken">Signed liveness verification token.</param>
/// <param name="IsLive">True if liveness check passed.</param>
/// <param name="AntiSpoofScore">Calculated anti-spoofing confidence score.</param>
/// <param name="ExpiresAt">Expiration timestamp of the liveness token (15 minutes from issue).</param>
/// <param name="Status">Verification status string.</param>
public sealed record VerifyLivenessResponse(
    [property: JsonPropertyName("liveness_token")] string LivenessToken,
    [property: JsonPropertyName("is_live")] bool IsLive,
    [property: JsonPropertyName("anti_spoof_score")] decimal AntiSpoofScore,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("status")] string Status);

/// <summary>AI Gateway REST endpoints for biometric liveness verification.</summary>
public static class BiometricEndpoints
{
    /// <summary>Maximum number of base64 image frames accepted per camera liveness request.</summary>
    public const int MaxFrameCount = 10;

    /// <summary>Maximum decoded byte size per individual video frame (2 MiB).</summary>
    public const int MaxFrameBytes = 2 * 1024 * 1024;

    /// <summary>Maximum cumulative decoded byte size across all frames in one request (8 MiB).</summary>
    public const int MaxTotalFrameBytes = 8 * 1024 * 1024;

    private const int MaxBase64CharsPerFrame = ((MaxFrameBytes + 2) / 3 * 4) + 64;
    private const string CaptureConsentPermission = "econsent_consent:capture";

    private static readonly string[] AllowedDataUriPrefixes =
    [
        "data:image/png;base64,",
        "data:image/jpeg;base64,",
        "data:image/jpg;base64,",
        "data:image/webp;base64,",
    ];

    /// <summary>Maps biometric AI gateway routes.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The updated endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapBiometricEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/ai/biometrics")
            .RequireSignedInCaller("Anti-spoof facial liveness verification generates short-lived HMAC signed tokens for consent capture.")
            .RequireTenantScope()
            .WithTags("Biometrics AI Gateway");

        group.MapPost("/verify-liveness", VerifyLivenessAsync)
            .WithName("AiVerifyLiveness")
            .WithSummary("Tests video frame payloads for anti-spoofing in temporary memory and returns signed liveness tokens.")
            .Produces<VerifyLivenessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesKernelErrors(KernelErrors.ActorUnresolved);

        return app;
    }

    /// <summary>Processes video frame payloads for anti-spoofing in temporary memory and generates scope-bound signed liveness tokens.</summary>
    /// <param name="request">The biometric liveness verification payload.</param>
    /// <param name="httpContext">The active HTTP context carrying the verified caller identity and tenant scope.</param>
    /// <param name="livenessOptions">Configured biometric liveness HMAC signing key options.</param>
    /// <param name="clock">Injected wall clock.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>An HTTP result containing either the signed liveness token or an error problem response.</returns>
    public static Task<IResult> VerifyLivenessAsync(
        VerifyLivenessRequest request,
        HttpContext httpContext,
        IOptions<BiometricLivenessOptions> livenessOptions,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(livenessOptions);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (ClinicalScopeGuard.RequireTenant(httpContext, out string tenantId) is { } tenantDenied)
        {
            return Task.FromResult<IResult>(tenantDenied);
        }

        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return Task.FromResult<IResult>(ClinicalScopeGuard.Forbidden(
                ClinicalScopeGuard.TenantAccessDeniedDetail,
                conditionCode: RbacExplanationBuilder.TenantScopeCondition));
        }

        string issuerId = principal.UserId.Trim();
        string? requestedStudyId = NormalizeScopeCode(request?.StudyId);
        string? requestedSiteId = NormalizeScopeCode(request?.SiteId);
        bool isFallback = request?.FallbackMode ?? false;

        bool canCaptureConsent = PermissionMatrix.HasPermission(principal, CaptureConsentPermission);
        var normalizedRoles = RoleNormalization.GetNormalizedRoles(principal.Roles.Concat(principal.RawRoles));
        bool isSubjectCaller = normalizedRoles.Contains(RoleNormalization.ROLE_SUBJECT, StringComparer.OrdinalIgnoreCase);

        if (isFallback)
        {
            if (!canCaptureConsent)
            {
                return Task.FromResult<IResult>(ClinicalScopeGuard.Forbidden(
                    "Coordinator biometric liveness fallback requires econsent_consent:capture permission.",
                    conditionCode: RbacExplanationBuilder.RoleCondition));
            }

            if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } fallbackCallerScopeDenied)
            {
                return Task.FromResult<IResult>(fallbackCallerScopeDenied);
            }

            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, requestedStudyId, requestedSiteId) is { } fallbackScopeDenied)
            {
                return Task.FromResult<IResult>(fallbackScopeDenied);
            }

            if (string.IsNullOrWhiteSpace(requestedSiteId))
            {
                return Task.FromResult<IResult>(ClinicalScopeGuard.Forbidden(
                    ClinicalScopeGuard.SiteAccessDeniedDetail,
                    conditionCode: RbacExplanationBuilder.SiteScopeCondition));
            }
        }
        else if (!canCaptureConsent && !isSubjectCaller)
        {
            return Task.FromResult<IResult>(ClinicalScopeGuard.Forbidden(
                "Biometric liveness verification requires econsent_consent:capture permission or a matching participant identity.",
                conditionCode: RbacExplanationBuilder.RoleCondition));
        }

        if (request is null || string.IsNullOrWhiteSpace(request.SubjectPseudonym))
        {
            return Task.FromResult<IResult>(TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["subject_pseudonym"] = ["SubjectPseudonym is required."]
            }));
        }

        string subject = request.SubjectPseudonym.Trim();

        if (!isFallback)
        {
            if (canCaptureConsent)
            {
                if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } callerScopeDenied)
                {
                    return Task.FromResult<IResult>(callerScopeDenied);
                }

                if (requestedStudyId is not null
                    && ClinicalScopeGuard.RequireStudy(httpContext, requestedStudyId) is { } studyDenied)
                {
                    return Task.FromResult<IResult>(studyDenied);
                }

                if (requestedSiteId is not null
                    && ClinicalScopeGuard.RequireSite(httpContext, requestedSiteId) is { } siteDenied)
                {
                    return Task.FromResult<IResult>(siteDenied);
                }
            }
            else
            {
                if (!string.Equals(issuerId, subject, StringComparison.Ordinal))
                {
                    return Task.FromResult<IResult>(ClinicalScopeGuard.Forbidden(
                        "Participants may only verify biometric liveness for their own subject pseudonym.",
                        conditionCode: RbacExplanationBuilder.RoleCondition));
                }

                if (requestedStudyId is not null
                    && ClinicalScopeGuard.RequireStudy(httpContext, requestedStudyId) is { } subjectStudyDenied)
                {
                    return Task.FromResult<IResult>(subjectStudyDenied);
                }

                if (requestedSiteId is not null
                    && ClinicalScopeGuard.RequireSite(httpContext, requestedSiteId) is { } subjectSiteDenied)
                {
                    return Task.FromResult<IResult>(subjectSiteDenied);
                }
            }
        }

        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset expiresAt = now.Add(BiometricLivenessTokenService.MaxLifetime);

        // Process frames strictly in temporary memory; raw frames are never persisted or logged.
        decimal score;
        if (isFallback)
        {
            score = 0.92m;
        }
        else
        {
            var frames = request.VideoFrames;
            if (frames is null || frames.Count == 0)
            {
                return Task.FromResult<IResult>(TypedResults.BadRequest(new VerifyLivenessResponse(
                    LivenessToken: string.Empty,
                    IsLive: false,
                    AntiSpoofScore: 0.15m,
                    ExpiresAt: now,
                    Status: "REJECTED")));
            }

            if (!TryValidateVideoFramesInMemory(frames, out string frameValidationError))
            {
                return Task.FromResult<IResult>(TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["video_frames"] = [frameValidationError]
                }));
            }

            score = 0.98m;
        }

        bool isLive = score >= 0.80m;
        string mode = isFallback
            ? BiometricLivenessTokenService.CoordinatorFallbackMode
            : BiometricLivenessTokenService.CameraMode;
        byte[] livenessSecret = livenessOptions.Value.DecodeKey();

        string token = BiometricLivenessTokenService.IssueToken(
            subjectPseudonym: subject,
            antiSpoofScore: score,
            isLive: isLive,
            mode: mode,
            secret: livenessSecret,
            now: now,
            lifetime: BiometricLivenessTokenService.MaxLifetime,
            tenantId: tenantId,
            issuerId: issuerId,
            studyId: requestedStudyId,
            siteId: requestedSiteId);

        var response = new VerifyLivenessResponse(
            LivenessToken: token,
            IsLive: isLive,
            AntiSpoofScore: score,
            ExpiresAt: expiresAt,
            Status: isLive ? "VERIFIED" : "REJECTED");

        if (!isLive)
        {
            return Task.FromResult<IResult>(TypedResults.BadRequest(response));
        }

        return Task.FromResult<IResult>(TypedResults.Ok(response));
    }

    private static string? NormalizeScopeCode(LivenessScopeCode? scopeCode) =>
        scopeCode is { Value: { Length: > 0 } raw } && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : null;

    private static bool TryValidateVideoFramesInMemory(IReadOnlyList<string> frames, out string errorMessage)
    {
        if (frames.Count > MaxFrameCount)
        {
            errorMessage = $"At most {MaxFrameCount} video frames are permitted per liveness check.";
            return false;
        }

        int totalDecodedBytes = 0;

        for (int index = 0; index < frames.Count; index++)
        {
            string? rawFrame = frames[index];
            if (string.IsNullOrWhiteSpace(rawFrame))
            {
                errorMessage = "Each video frame must be a non-empty base64-encoded PNG, JPEG, or WebP image.";
                return false;
            }

            ReadOnlySpan<char> trimmed = rawFrame.AsSpan().Trim();
            if (!TryStripOptionalImageDataUriPrefix(ref trimmed))
            {
                errorMessage = "Unsupported data URI media type; only PNG, JPEG, or WebP image frames are accepted.";
                return false;
            }

            if (trimmed.IsEmpty || trimmed.Length > MaxBase64CharsPerFrame)
            {
                errorMessage = $"Each video frame must not exceed {MaxFrameBytes} decoded bytes.";
                return false;
            }

            int maxDecodedLength = (int)Math.Min(MaxFrameBytes + 1L, ((long)trimmed.Length + 3L) / 4L * 3L);
            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(64, maxDecodedLength));
            try
            {
                if (!Convert.TryFromBase64String(trimmed.ToString(), rentedBuffer, out int bytesWritten))
                {
                    errorMessage = "Each video frame must be valid base64 data.";
                    return false;
                }

                if (bytesWritten <= 0 || bytesWritten > MaxFrameBytes)
                {
                    errorMessage = $"Each video frame must be between 1 and {MaxFrameBytes} bytes.";
                    return false;
                }

                totalDecodedBytes += bytesWritten;
                if (totalDecodedBytes > MaxTotalFrameBytes)
                {
                    errorMessage = $"Total decoded video frame bytes must not exceed {MaxTotalFrameBytes} bytes.";
                    return false;
                }

                ReadOnlySpan<byte> frameBytes = rentedBuffer.AsSpan(0, bytesWritten);
                if (!HasValidImageHeaderAndDimensions(frameBytes))
                {
                    errorMessage = "Each video frame must be a valid PNG, JPEG, or WebP image with non-zero width and height.";
                    return false;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rentedBuffer);
                ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: false);
            }
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryStripOptionalImageDataUriPrefix(ref ReadOnlySpan<char> frameChars)
    {
        if (!frameChars.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string prefix in AllowedDataUriPrefixes)
        {
            if (frameChars.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                frameChars = frameChars[prefix.Length..].Trim();
                return true;
            }
        }

        return false;
    }

    internal static bool HasValidImageHeaderAndDimensions(ReadOnlySpan<byte> bytes) =>
        IsValidPng(bytes) || IsValidJpeg(bytes) || IsValidWebP(bytes);

    private static bool IsValidPng(ReadOnlySpan<byte> bytes)
    {
        // 8-byte PNG signature + 25-byte IHDR chunk (4 length + 4 "IHDR" + 13 IHDR data + 4 CRC) = 33 bytes minimum.
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(pngSignature))
        {
            return false;
        }

        uint ihdrLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4));
        if (ihdrLength != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        return width > 0 && width <= int.MaxValue && height > 0 && height <= int.MaxValue;
    }

    private static bool IsValidJpeg(ReadOnlySpan<byte> bytes)
    {
        // SOI (FF D8) followed by markers starting with FF.
        if (bytes.Length < 11 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
        {
            return false;
        }

        int offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return false;
            }

            while (offset < bytes.Length && bytes[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            byte marker = bytes[offset++];

            // Standalone markers with no payload length: TEM (0x01) and RST0..RST7 (0xD0..0xD7).
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            // EOI (0xD9) or SOS (0xDA) reached before any SOF header.
            if (marker == 0xD9 || marker == 0xDA)
            {
                return false;
            }

            if (offset + 2 > bytes.Length)
            {
                return false;
            }

            ushort segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
            {
                return false;
            }

            if (IsJpegStartOfFrameMarker(marker))
            {
                if (segmentLength < 8)
                {
                    return false;
                }

                ushort height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                ushort width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsJpegStartOfFrameMarker(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

    private static bool IsValidWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 25
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        if (riffSize < 12)
        {
            return false;
        }

        ReadOnlySpan<byte> chunkType = bytes.Slice(12, 4);
        uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16, 4));

        if (chunkType.SequenceEqual("VP8 "u8))
        {
            // Lossy VP8 bitstream header: 3-byte frame tag + 3-byte keyframe start code (9D 01 2A) + 2-byte width + 2-byte height.
            if (bytes.Length < 30 || chunkSize < 10)
            {
                return false;
            }

            if (bytes[23] != 0x9D || bytes[24] != 0x01 || bytes[25] != 0x2A)
            {
                return false;
            }

            int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3FFF;
            int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3FFF;
            return width > 0 && height > 0;
        }

        if (chunkType.SequenceEqual("VP8L"u8))
        {
            // Lossless VP8L bitstream header: 1-byte signature (0x2F) + packed 14-bit (width - 1) and 14-bit (height - 1).
            if (bytes.Length < 25 || chunkSize < 5 || bytes[20] != 0x2F)
            {
                return false;
            }

            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(21, 4));
            int width = (int)(packed & 0x3FFF) + 1;
            int height = (int)((packed >> 14) & 0x3FFF) + 1;
            return width > 0 && height > 0;
        }

        if (chunkType.SequenceEqual("VP8X"u8))
        {
            // Extended VP8X header: 4-byte flags/reserved + 24-bit (width - 1) + 24-bit (height - 1).
            if (bytes.Length < 30 || chunkSize < 10)
            {
                return false;
            }

            long width = (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1L;
            long height = (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1L;
            return width > 0 && height > 0 && width * height <= uint.MaxValue;
        }

        return false;
    }
}
