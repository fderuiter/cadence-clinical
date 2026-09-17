using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Compliance;

public static class GxPSigner
{
    public static string SignGxpMarkdown(
        string content,
        string? signerId = null,
        string signingReason = "GxP Qualification Execution Sign-Off",
        string? timestamp = null)
    {
        signerId ??= Environment.GetEnvironmentVariable("GXP_SIGNER_ID") ?? "cadence-validation-runner";
        timestamp ??= DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

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

        var dummyCertPem = "-----BEGIN CERTIFICATE-----\nMIIC...DUMMY\n-----END CERTIFICATE-----";
        var dummySig = Convert.ToBase64String(Encoding.UTF8.GetBytes(sha256Hash));

        return $"{footerText}{dummyCertPem}\n-----BEGIN SIGNATURE-----\n{dummySig}\n-----END SIGNATURE-----\n";
    }
}
