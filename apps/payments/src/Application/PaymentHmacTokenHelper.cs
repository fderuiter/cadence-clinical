using System;
using System.Text;
using System.Text.Json;

using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.Extensions.Options;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Server-signed single-use HMAC token issuance and verification for receipt OCR scans and ePRO diary syncs.
/// </summary>
public static class PaymentHmacTokenHelper
{
    public const string OcrTokenConsumedAuditAction = "OCR_TOKEN_CONSUMED";
    public const string DiarySyncTokenConsumedAuditAction = "DIARY_SYNC_TOKEN_CONSUMED";
    public const string ReceiptDigestRecordedAuditAction = "EXPENSE_RECEIPT_DIGEST_RECORDED";

    public static readonly TimeSpan OcrTokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DiarySyncTokenLifetime = TimeSpan.FromMinutes(30);

    public sealed record DiarySyncVerificationTokenPayload(
        string TokenId,
        string CallerUserId,
        string StudyCode,
        string? DiaryId,
        string? AssignmentId,
        int? SequenceNumber,
        long IssuedAtUnix,
        long ExpiresAtUnix);

    public sealed record ReceiptOcrTokenPayload(
        string ScanId,
        string CallerUserId,
        string ReceiptSha256,
        bool OcrReadable,
        string MerchantName,
        string ExpenseDate,
        decimal GrossAmount,
        string Category,
        double ConfidenceScore,
        long IssuedAtUnix,
        long ExpiresAtUnix);

    public static string ResolveConfiguredGatewaySecret(IOptions<GatewayAuthenticationOptions> gatewayAuthentication)
    {
        ArgumentNullException.ThrowIfNull(gatewayAuthentication);
        return gatewayAuthentication.Value.GatewaySecret is { Length: >= GatewayAuthenticationOptions.MinimumSecretLength } secret
            ? secret
            : throw new InvalidOperationException(
                $"{CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey} must be set to a secret of at least {GatewayAuthenticationOptions.MinimumSecretLength} characters; payment tokens cannot be signed.");
    }

    public static string IssueReceiptOcrToken(ReceiptOcrTokenPayload payload, string? gatewaySecret)
    {
        if (gatewaySecret is not { Length: >= GatewayAuthenticationOptions.MinimumSecretLength })
        {
            throw new InvalidOperationException(
                $"{CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey} must be set to a secret of at least {GatewayAuthenticationOptions.MinimumSecretLength} characters; receipt OCR tokens cannot be signed.");
        }

        return SignHmacTokenPayload(payload, DeriveOcrTokenSigningKey(gatewaySecret));
    }

    public static string IssueDiarySyncVerificationToken(
        string callerUserId,
        string studyCode,
        string? diaryId,
        string? assignmentId,
        int? sequenceNumber,
        DateTimeOffset issuedAt,
        string? gatewaySecret,
        TimeSpan? lifetime = null,
        string? tokenId = null)
    {
        if (gatewaySecret is not { Length: >= GatewayAuthenticationOptions.MinimumSecretLength })
        {
            throw new InvalidOperationException(
                $"{CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey} must be set to a secret of at least {GatewayAuthenticationOptions.MinimumSecretLength} characters; diary sync verification tokens cannot be signed.");
        }

        string resolvedTokenId = string.IsNullOrWhiteSpace(tokenId)
            ? "DIARY_SYNC_" + Guid.NewGuid().ToString("N")
            : tokenId.Trim();
        TimeSpan resolvedLifetime = lifetime ?? DiarySyncTokenLifetime;
        var payload = new DiarySyncVerificationTokenPayload(
            TokenId: resolvedTokenId,
            CallerUserId: callerUserId,
            StudyCode: studyCode,
            DiaryId: NormalizeOptionalTokenField(diaryId),
            AssignmentId: NormalizeOptionalTokenField(assignmentId),
            SequenceNumber: sequenceNumber,
            IssuedAtUnix: issuedAt.ToUnixTimeSeconds(),
            ExpiresAtUnix: issuedAt.Add(resolvedLifetime).ToUnixTimeSeconds());

        return SignHmacTokenPayload(payload, DeriveDiarySyncTokenSigningKey(gatewaySecret));
    }

    public static bool TryVerifyReceiptOcrToken(
        string? token,
        string? gatewaySecret,
        string expectedCallerUserId,
        DateTimeOffset now,
        out ReceiptOcrTokenPayload? payload)
    {
        payload = null;
        if (gatewaySecret is not { Length: >= GatewayAuthenticationOptions.MinimumSecretLength }
            || !TryDecodeAndVerifyHmacTokenPayload(token, DeriveOcrTokenSigningKey(gatewaySecret), out ReceiptOcrTokenPayload? parsed)
            || parsed is null)
        {
            return false;
        }

        if (parsed.ExpiresAtUnix < now.ToUnixTimeSeconds()
            || !string.Equals(parsed.CallerUserId, expectedCallerUserId, StringComparison.Ordinal))
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    public static bool TryVerifyDiarySyncVerificationToken(
        string? token,
        string? gatewaySecret,
        string expectedCallerUserId,
        string expectedStudyCode,
        string? expectedDiaryId,
        string? expectedAssignmentId,
        int? expectedSequenceNumber,
        DateTimeOffset now,
        out DiarySyncVerificationTokenPayload? payload)
    {
        payload = null;
        if (gatewaySecret is not { Length: >= GatewayAuthenticationOptions.MinimumSecretLength }
            || !TryDecodeAndVerifyHmacTokenPayload(token, DeriveDiarySyncTokenSigningKey(gatewaySecret), out DiarySyncVerificationTokenPayload? parsed)
            || parsed is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.TokenId)
            || parsed.ExpiresAtUnix < now.ToUnixTimeSeconds()
            || !string.Equals(parsed.CallerUserId, expectedCallerUserId, StringComparison.Ordinal)
            || !string.Equals(parsed.StudyCode, expectedStudyCode, StringComparison.Ordinal)
            || !string.Equals(NormalizeOptionalTokenField(parsed.DiaryId), NormalizeOptionalTokenField(expectedDiaryId), StringComparison.Ordinal)
            || !string.Equals(NormalizeOptionalTokenField(parsed.AssignmentId), NormalizeOptionalTokenField(expectedAssignmentId), StringComparison.Ordinal)
            || parsed.SequenceNumber != expectedSequenceNumber)
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    public static byte[] DecodeReceiptImageBytes(string? imageBase64)
    {
        if (string.IsNullOrWhiteSpace(imageBase64)
            || imageBase64.Length > ReceiptOcrEngine.MaxEncodedReceiptPayloadChars)
        {
            return [];
        }

        return ReceiptOcrEngine.TryDecodeReceiptPayload(imageBase64, out byte[] bytes)
            ? bytes
            : [];
    }

    public static string? TryExtractOcrTokenFromObject(object? ocrData)
    {
        if (ocrData is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("ocrToken", out var camelProp) && camelProp.ValueKind == JsonValueKind.String)
        {
            return camelProp.GetString();
        }

        if (element.TryGetProperty("ocr_token", out var snakeProp) && snakeProp.ValueKind == JsonValueKind.String)
        {
            return snakeProp.GetString();
        }

        return null;
    }

    private static string DeriveOcrTokenSigningKey(string gatewaySecret) =>
        HmacSigner.ComputeSignature("cadence-payments-receipt-ocr-token-v1", gatewaySecret);

    private static string DeriveDiarySyncTokenSigningKey(string gatewaySecret) =>
        HmacSigner.ComputeSignature("cadence-payments-diary-sync-token-v1", gatewaySecret);

    private static string NormalizeOptionalTokenField(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string SignHmacTokenPayload<TPayload>(TPayload payload, string signingKey)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        string encodedPayload = Convert.ToBase64String(jsonBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string signature = HmacSigner.ComputeSignature(encodedPayload, signingKey);
        return $"{encodedPayload}.{signature}";
    }

    private static bool TryDecodeAndVerifyHmacTokenPayload<TPayload>(
        string? token,
        string signingKey,
        out TPayload? payload)
        where TPayload : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        string encodedPayload = parts[0];
        string signature = parts[1];
        if (!HmacSigner.VerifySignature(encodedPayload, signingKey, signature))
        {
            return false;
        }

        try
        {
            string padded = encodedPayload.Replace('-', '+').Replace('_', '/');
            int remainder = padded.Length % 4;
            if (remainder == 2)
            {
                padded += "==";
            }
            else if (remainder == 3)
            {
                padded += "=";
            }
            else if (remainder != 0)
            {
                return false;
            }

            byte[] jsonBytes = Convert.FromBase64String(padded);
            payload = JsonSerializer.Deserialize<TPayload>(jsonBytes);
            return payload is not null;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
