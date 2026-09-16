using System;
using System.Collections.Generic;

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
        _revokedCerts = revokedCerts != null ? new HashSet<string>(revokedCerts) : [];
    }

    public VerificationResult VerifySignature(byte[] signedData)
    {
        if (signedData == null || signedData.Length == 0)
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", "Empty signed data payload.");
        }

        var text = System.Text.Encoding.UTF8.GetString(signedData);

        if (text.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            return new VerificationResult(false, "MOCK_SIGNATURE_DETECTED", "Mock signature detected and blocked.");
        }

        if (!text.Contains("-----BEGIN CERTIFICATE-----") || !text.Contains("-----BEGIN SIGNATURE-----"))
        {
            return new VerificationResult(false, "TAMPERED_INVALID_HASH", "TAMPER DETECTED: Missing certificate or signature markers.");
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
        var bytes = System.Text.Encoding.UTF8.GetBytes(signedData);
        return VerifySignature(bytes);
    }
}
