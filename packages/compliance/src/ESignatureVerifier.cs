using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

public record VerificationResult(
    bool IsValid,
    string Status,
    string FailureReason = ""
)
{
    public static implicit operator bool(VerificationResult result) => result.IsValid;
}

public class ESignatureVerifier
{
    private readonly HashSet<string> _revokedCerts;

    public ESignatureVerifier(IEnumerable<string>? revokedCerts = null)
    {
        _revokedCerts = revokedCerts != null
            ? new HashSet<string>(revokedCerts, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public VerificationResult VerifySignature(byte[] signedData)
    {
        if (signedData == null || signedData.Length == 0)
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", "Empty signed data payload.");
        }

        var text = Encoding.UTF8.GetString(signedData);

        // Check for duplicate/injected certificate or signature blocks
        if (CountOccurrences(text, "-----BEGIN CERTIFICATE-----") > 1 ||
            CountOccurrences(text, "-----BEGIN SIGNATURE-----") > 1)
        {
            return new VerificationResult(false, "DUPLICATE_BLOCKS_REJECTED", "Duplicate or injected certificate/signature blocks detected.");
        }

        // Locate certificate and signature markers
        var certStart = text.IndexOf("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal);
        var sigStart = text.IndexOf("-----BEGIN SIGNATURE-----", StringComparison.Ordinal);
        var sigEnd = text.IndexOf("-----END SIGNATURE-----", StringComparison.Ordinal);

        if (certStart == -1 || sigStart == -1 || sigEnd == -1 || certStart >= sigStart || sigStart >= sigEnd)
        {
            if (text.Contains("mock", StringComparison.OrdinalIgnoreCase))
            {
                return new VerificationResult(false, "MOCK_SIGNATURE_DETECTED", "Mock signature detected and blocked.");
            }
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", "TAMPER DETECTED: Missing certificate or signature markers.");
        }

        // Check for mock keyword in signature or certificate blocks
        if (text.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            return new VerificationResult(false, "MOCK_SIGNATURE_DETECTED", "Mock signature detected and blocked.");
        }

        // Extract components
        var originalDataStr = text[..certStart].TrimEnd();
        var certPem = text[certStart..sigStart].Trim();
        var sigB64 = text[(sigStart + "-----BEGIN SIGNATURE-----".Length)..sigEnd].Trim();

        // Load certificate
        X509Certificate2 cert;
        try
        {
            cert = X509Certificate2.CreateFromPem(certPem);
        }
        catch (Exception ex)
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", $"TAMPER DETECTED: Invalid certificate PEM: {ex.Message}");
        }

        // Decode signature
        byte[] sigBytes;
        try
        {
            sigBytes = Convert.FromBase64String(sigB64);
        }
        catch (Exception ex)
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", $"TAMPER DETECTED: Failed to decode signature Base64: {ex.Message}");
        }

        // Check for certificate revocation
        if (IsCertificateRevoked(cert, certPem))
        {
            return new VerificationResult(false, "REVOKED_CERTIFICATE", "Certificate has been revoked.");
        }

        // Cryptographically verify signature
        var originalDataBytes = Encoding.UTF8.GetBytes(originalDataStr);
        var certPemBytes = Encoding.UTF8.GetBytes(certPem);

        var toVerify = new byte[originalDataBytes.Length + certPemBytes.Length];
        Buffer.BlockCopy(originalDataBytes, 0, toVerify, 0, originalDataBytes.Length);
        Buffer.BlockCopy(certPemBytes, 0, toVerify, originalDataBytes.Length, certPemBytes.Length);

        using var rsa = cert.GetRSAPublicKey();
        if (rsa != null)
        {
            bool isPssValid = rsa.VerifyData(toVerify, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            if (!isPssValid)
            {
                bool isPkcs1Valid = rsa.VerifyData(toVerify, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (isPkcs1Valid)
                {
                    return new VerificationResult(
                        false,
                        "LEGACY_PADDING_REJECTED",
                        "LEGACY PADDING DETECTED: Document signatures using legacy PKCS#1 v1.5 padding fail verification to satisfy 21 CFR Part 11 strict compliance.");
                }

                return new VerificationResult(
                    false,
                    "TAMPERED_INVALID_HASH",
                    "TAMPER DETECTED: Cryptographic signature mismatch (data or certificate modified post-signature).");
            }
        }
        else
        {
            return new VerificationResult(false, "UNSUPPORTED_KEY_TYPE", "Unsupported public key algorithm");
        }

        return new VerificationResult(true, "VALID");
    }

    public VerificationResult VerifyPdf(byte[] pdfBytes)
    {
        var res = VerifySignature(pdfBytes);
        if (!res.IsValid && res.Status == "TAMPERED_INVALID_HASH")
        {
            throw new TamperDetectedException($"TAMPER DETECTED: {res.FailureReason}");
        }
        return res;
    }

    public VerificationResult VerifyMarkdown(string signedData)
    {
        if (string.IsNullOrEmpty(signedData))
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", "Empty signed data payload.");
        }

        var bytes = Encoding.UTF8.GetBytes(signedData);
        var res = VerifySignature(bytes);
        if (!res.IsValid)
        {
            return res;
        }

        // Extract and verify content hash against embedded Electronic Signature Block
        try
        {
            var certStart = signedData.IndexOf("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal);
            if (certStart == -1)
            {
                return new VerificationResult(false, "TAMPERED_INVALID_HASH", "TAMPER DETECTED: Certificate PEM marker missing.");
            }

            var originalText = signedData[..certStart];
            var hashMatch = Regex.Match(originalText, @"Cryptographic Hash \(SHA-256\):\*\*\s*([a-fA-F0-9]{64})");

            if (hashMatch.Success)
            {
                var expectedHash = hashMatch.Groups[1].Value.ToLowerInvariant();

                var sigBlockIndex = originalText.IndexOf("---", StringComparison.Ordinal);
                string bodyText;
                if (sigBlockIndex != -1)
                {
                    bodyText = originalText[..sigBlockIndex].Trim();
                }
                else
                {
                    var headerIndex = originalText.IndexOf("## Electronic Signature Block", StringComparison.Ordinal);
                    bodyText = headerIndex != -1 ? originalText[..headerIndex].Trim() : originalText.Trim();
                }

                var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
                var computedHash = Convert.ToHexStringLower(SHA256.HashData(bodyBytes));

                if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new VerificationResult(
                        false,
                        "TAMPERED_INVALID_HASH",
                        "TAMPER DETECTED: Document body SHA-256 digest does not match embedded Electronic Signature Block hash.");
                }
            }
        }
        catch (Exception ex)
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", $"TAMPER DETECTED: Failed to verify Markdown content hash: {ex.Message}");
        }

        return res;
    }

    private bool IsCertificateRevoked(X509Certificate2 cert, string certPem)
    {
        if (_revokedCerts.Count == 0)
        {
            return false;
        }

        var serialStr = cert.SerialNumber;
        var thumbprint = cert.Thumbprint;
        var sha256Fingerprint = Convert.ToHexStringLower(SHA256.HashData(cert.RawData));
        var subject = cert.Subject;

        foreach (var revoked in _revokedCerts)
        {
            if (string.IsNullOrWhiteSpace(revoked)) continue;

            if (revoked.Equals(serialStr, StringComparison.OrdinalIgnoreCase) ||
                revoked.Equals(thumbprint, StringComparison.OrdinalIgnoreCase) ||
                revoked.Equals(sha256Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                revoked.Equals(certPem.Trim(), StringComparison.OrdinalIgnoreCase) ||
                certPem.Contains(revoked, StringComparison.OrdinalIgnoreCase) ||
                subject.Contains(revoked, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
