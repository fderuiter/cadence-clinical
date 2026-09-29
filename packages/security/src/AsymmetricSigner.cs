using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Cadence.Security;

/// <summary>
/// Provides RSA-PSS and ECDSA asymmetric signing and verification using PEM keys or certificates.
/// </summary>
public static class AsymmetricSigner
{
    public static string AsymmetricSign(byte[] data, string privateKeyPem, byte[]? password = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        char[]? passwordChars = password != null && password.Length > 0 ? Encoding.UTF8.GetChars(password) : null;

        // 1. Try importing as RSA
        using var rsa = RSA.Create();
        bool rsaImported = false;
        try
        {
            if (passwordChars != null)
            {
                rsa.ImportFromEncryptedPem(privateKeyPem.AsSpan(), passwordChars);
            }
            else
            {
                rsa.ImportFromPem(privateKeyPem.AsSpan());
            }
            rsaImported = true;
        }
        catch
        {
            rsaImported = false;
        }

        if (rsaImported)
        {
            byte[] signatureBytes = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            return Convert.ToBase64String(signatureBytes);
        }

        // 2. Try importing as ECDsa
        using var ecdsa = ECDsa.Create();
        if (passwordChars != null)
        {
            ecdsa.ImportFromEncryptedPem(privateKeyPem.AsSpan(), passwordChars);
        }
        else
        {
            ecdsa.ImportFromPem(privateKeyPem.AsSpan());
        }

        byte[] ecSigBytes = ecdsa.SignData(data, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(ecSigBytes);
    }

    public static bool AsymmetricVerify(byte[] data, string signatureB64, string publicKeyOrCertPem)
    {
        if (data == null || string.IsNullOrWhiteSpace(signatureB64) || string.IsNullOrWhiteSpace(publicKeyOrCertPem))
        {
            return false;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureB64);
        }
        catch
        {
            return false;
        }

        // 1. Try loading as X509 certificate
        try
        {
            using var cert = X509Certificate2.CreateFromPem(publicKeyOrCertPem);
            using var rsaCert = cert.GetRSAPublicKey();
            if (rsaCert != null && rsaCert.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            {
                return true;
            }

            using var ecdsaCert = cert.GetECDsaPublicKey();
            if (ecdsaCert != null && ecdsaCert.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256))
            {
                return true;
            }
        }
        catch
        {
            // Ignore certificate load errors and try public key directly
        }

        // 2. Try loading as RSA public key directly
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyOrCertPem.AsSpan());
            if (rsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        // 3. Try loading as ECDsa public key directly
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyOrCertPem.AsSpan());
            if (ecdsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256))
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }
}
