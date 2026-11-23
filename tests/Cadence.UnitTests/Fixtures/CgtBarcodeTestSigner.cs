using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.UnitTests.Fixtures;

internal static class CgtBarcodeTestSigner
{
    private static readonly byte[] DefaultTestKey = Encoding.UTF8.GetBytes("Cadence-CGT-HMAC-Secret-Key-2026!"); // pragma: allowlist secret

    public static string SignPayload(
        string studyId,
        string siteId,
        string subjectId,
        string din,
        string? batchId = null,
        DateTimeOffset? createdAt = null,
        byte[]? secretKey = null)
    {
        byte[] key = secretKey ?? DefaultTestKey;
        string barcodeId = $"BAR-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        DateTimeOffset timestampOffset = createdAt ?? DateTimeOffset.UnixEpoch;
        string timestamp = timestampOffset.ToString("o");

        string canonicalMsg = $"BARCODE_ID:{barcodeId}|STUDY:{studyId}|SITE:{siteId}|SUBJ:{subjectId}|DIN:{din}|BATCH:{batchId ?? ""}|TIME:{timestamp}";
        byte[] hashBytes = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonicalMsg));
        string cryptoHash = Convert.ToHexStringLower(hashBytes);

        var payloadDict = new Dictionary<string, string?>
        {
            ["barcode_id"] = barcodeId,
            ["study_id"] = studyId,
            ["site_id"] = siteId,
            ["subject_id"] = subjectId,
            ["din"] = din,
            ["batch_id"] = batchId,
            ["created_at"] = timestamp,
            ["crypto_hash"] = cryptoHash
        };

        string jsonPayload = JsonSerializer.Serialize(payloadDict);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
