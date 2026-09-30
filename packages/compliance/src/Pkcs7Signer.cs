using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Cadence.Kernel.Time;

namespace Cadence.Compliance;

/// <summary>
/// Native .NET 10 PKCS7 / CMS signer for 21 CFR Part 11 electronic signatures.
/// </summary>
public class Pkcs7Signer
{
    private readonly X509Certificate2 _certificate;
    private readonly RSA? _privateKey;

    public Pkcs7Signer(X509Certificate2 certificate, RSA? privateKey = null)
    {
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _privateKey = privateKey;
    }

    public X509Certificate2 Certificate => _certificate;
    public RSA? PrivateKey => _privateKey;

    /// <summary>
    /// Computes a PKCS#7 / CMS detached signature byte array using System.Security.Cryptography.Pkcs.SignedCms.
    /// </summary>
    public byte[] ComputeDetachedSignature(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var contentInfo = new ContentInfo(data);
        var signedCms = new SignedCms(contentInfo, detached: true);

        X509Certificate2 certWithKey;
        if (_certificate.HasPrivateKey)
        {
            certWithKey = _certificate;
        }
        else if (_privateKey != null)
        {
            certWithKey = _certificate.CopyWithPrivateKey(_privateKey);
        }
        else
        {
            certWithKey = _certificate;
        }

        var cmsSigner = new CmsSigner(certWithKey)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") // SHA-256
        };

        signedCms.ComputeSignature(cmsSigner);
        return signedCms.Encode();
    }

    /// <summary>
    /// Signs a document payload and returns document + PEM certificate + Base64 signature block.
    /// </summary>
    public byte[] SignDocument(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var certPem = ExportCertificatePem(_certificate).Trim();
        var certPemBytes = Encoding.UTF8.GetBytes(certPem);

        var toSign = new byte[data.Length + certPemBytes.Length];
        Buffer.BlockCopy(data, 0, toSign, 0, data.Length);
        Buffer.BlockCopy(certPemBytes, 0, toSign, data.Length, certPemBytes.Length);

        byte[] sigBytes;
        if (_privateKey != null)
        {
            sigBytes = _privateKey.SignData(toSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        else if (_certificate.HasPrivateKey)
        {
            using var rsa = _certificate.GetRSAPrivateKey();
            if (rsa != null)
            {
                sigBytes = rsa.SignData(toSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            }
            else
            {
                sigBytes = ComputeDetachedSignature(toSign);
            }
        }
        else
        {
            throw new InvalidOperationException("No private key available for signing.");
        }

        var sigB64 = Convert.ToBase64String(sigBytes);
        var dataStr = Encoding.UTF8.GetString(data);

        var result = $"{dataStr}\n{certPem}\n-----BEGIN SIGNATURE-----\n{sigB64}\n-----END SIGNATURE-----\n";
        return Encoding.UTF8.GetBytes(result);
    }

    /// <summary>
    /// Signs a PDF document payload. Alias for SignDocument.
    /// </summary>
    public byte[] SignPdf(byte[] pdfBytes) => SignDocument(pdfBytes);

    /// <summary>
    /// Exports an X509Certificate2 to a PEM-formatted string.
    /// </summary>
    public static string ExportCertificatePem(X509Certificate2 cert)
    {
        ArgumentNullException.ThrowIfNull(cert);
        return cert.ExportCertificatePem();
    }

    /// <summary>
    /// Generates a temporary RSA key pair and self-signed X.509 certificate for GxP signing credentials.
    /// </summary>
    public static (X509Certificate2 Cert, RSA Key) GenerateSigningCredentials(
        string signerId = "cadence-validation-runner",
        IClock? clock = null)
    {
        DateTimeOffset now = (clock ?? SystemClock.Instance).UtcNow;
        var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            new X500DistinguishedName($"CN=Cadence GxP Validation Runner ({signerId}), O=Cadence Clinical Software"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss
        );

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                critical: true
            )
        );

        var cert = req.CreateSelfSigned(
            now.AddDays(-1),
            now.AddYears(1)
        );

        return (cert, rsa);
    }
}
