using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Core.Domain.Supply;

public static class CgtBarcodeEngine
{
    public const int SecretKeySizeBytes = 32;

    public static (bool IsValid, string? FailureReason, Dictionary<string, string?> DecodedData) VerifyCryptoBarcode(
        string barcodePayload,
        byte[] secretKey,
        string? expectedSubjectId = null,
        string? expectedDin = null,
        string? expectedBatchId = null)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        if (secretKey.Length != SecretKeySizeBytes)
        {
            throw new ArgumentException($"CGT barcode HMAC key must be {SecretKeySizeBytes} bytes.", nameof(secretKey));
        }

        var decodedData = new Dictionary<string, string?>();
        if (string.IsNullOrWhiteSpace(barcodePayload))
        {
            return (false, "Barcode payload is empty.", decodedData);
        }

        try
        {
            string jsonString;
            if (barcodePayload.TrimStart().StartsWith('{'))
            {
                jsonString = barcodePayload;
            }
            else
            {
                string base64 = barcodePayload.Replace('-', '+').Replace('_', '/');
                switch (base64.Length % 4)
                {
                    case 2: base64 += "=="; break;
                    case 3: base64 += "="; break;
                }

                byte[] bytes = Convert.FromBase64String(base64);
                jsonString = Encoding.UTF8.GetString(bytes);
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(jsonString);
            if (dict == null)
            {
                return (false, "Malformed barcode JSON payload.", decodedData);
            }

            decodedData = dict;

            if (!dict.TryGetValue("barcode_id", out var barcodeId) || string.IsNullOrEmpty(barcodeId) ||
                !dict.TryGetValue("study_id", out var studyId) ||
                !dict.TryGetValue("site_id", out var siteId) ||
                !dict.TryGetValue("subject_id", out var subjectId) ||
                !dict.TryGetValue("din", out var din) ||
                !dict.TryGetValue("created_at", out var createdAt) ||
                !dict.TryGetValue("crypto_hash", out var claimedHash))
            {
                return (false, "Missing required barcode canonical payload attributes.", decodedData);
            }

            dict.TryGetValue("batch_id", out var batchId);

            if (claimedHash is not { Length: 64 } || !TryDecodeSha256Hex(claimedHash, out byte[] claimedHashBytes))
            {
                return (false, "Cryptographic signature verification failed: barcode signature is not a valid SHA-256 digest.", decodedData);
            }

            string canonicalMsg = $"BARCODE_ID:{barcodeId}|STUDY:{studyId}|SITE:{siteId}|SUBJ:{subjectId}|DIN:{din}|BATCH:{batchId ?? ""}|TIME:{createdAt}";
            byte[] expectedHashBytes = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(canonicalMsg));

            if (!CryptographicOperations.FixedTimeEquals(claimedHashBytes, expectedHashBytes))
            {
                return (false, "Cryptographic signature verification failed: barcode payload has been tampered with.", decodedData);
            }

            if (!string.IsNullOrEmpty(expectedSubjectId) && !string.Equals(subjectId, expectedSubjectId, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Subject identity mismatch: barcode specifies subject '{subjectId}' but expected '{expectedSubjectId}'.", decodedData);
            }

            if (!string.IsNullOrEmpty(expectedDin) && !string.Equals(din, expectedDin, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Donation ID mismatch: barcode specifies DIN '{din}' but expected '{expectedDin}'.", decodedData);
            }

            if (!string.IsNullOrEmpty(expectedBatchId) && !string.Equals(batchId, expectedBatchId, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Batch mismatch: barcode specifies batch '{batchId}' but expected '{expectedBatchId}'.", decodedData);
            }

            return (true, null, decodedData);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to parse barcode payload: {ex.Message}", decodedData);
        }
    }

    private static bool TryDecodeSha256Hex(string hex, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(hex);
            return bytes.Length == SecretKeySizeBytes;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
