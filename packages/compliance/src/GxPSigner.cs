using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Cadence.Compliance;

public static class GxPSigner
{
    public static string SignGxpMarkdown(
        string content,
        string? signerId = null,
        string signingReason = "GxP Qualification Execution Sign-Off",
        string? timestamp = null,
        RSA? privateKey = null,
        X509Certificate2? certificate = null)
    {
        signerId ??= Environment.GetEnvironmentVariable("GXP_SIGNER_ID") ?? "cadence-validation-runner";
        timestamp ??= DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC", System.Globalization.CultureInfo.InvariantCulture);

        var contentBytes = Encoding.UTF8.GetBytes(content.Trim());
        var sha256Hash = Convert.ToHexStringLower(SHA256.HashData(contentBytes));

        var footerText =
            $"{content.TrimEnd()}\n\n" +
            $"---\n\n" +
            $"## Electronic Signature Block\n\n" +
            $"- **Signer Identity:** {signerId}\n" +
            $"- **Timestamp:** {timestamp}\n" +
            $"- **Meaning / Purpose:** {signingReason}\n" +
            $"- **Cryptographic Hash (SHA-256):** {sha256Hash}\n\n";

        if (certificate == null || privateKey == null)
        {
            var creds = Pkcs7Signer.GenerateSigningCredentials(signerId);
            certificate ??= creds.Cert;
            privateKey ??= creds.Key;
        }

        var signer = new Pkcs7Signer(certificate, privateKey);
        var footerBytes = Encoding.UTF8.GetBytes(footerText.TrimEnd());
        var signedBytes = signer.SignDocument(footerBytes);

        return Encoding.UTF8.GetString(signedBytes);
    }
}
