using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Security;

/// <summary>
/// Canonicalizes, signs (kernel-side private PEM), and offline-verifies (SCE-side public PEM)
/// <see cref="DatabaseLockCertificateRecord"/> envelopes using <see cref="AsymmetricSigner"/>.
/// </summary>
public static class DatabaseLockCertificateCrypto
{
    public const string DefaultAlgorithm = "ECDSA-P256-SHA256";
    public const string RsaAlgorithm = "RSA-PSS-SHA256";

    public static byte[] BuildCanonicalPayloadBytes(
        string certificateId,
        string tenantId,
        string studyId,
        string studyCode,
        string lockId,
        string milestone,
        string attestationId,
        string signedBy,
        DateTimeOffset issuedAt,
        string keyId,
        string algorithm)
    {
        var json = new JsonObject
        {
            ["algorithm"] = algorithm,
            ["attestation_id"] = attestationId,
            ["certificate_id"] = certificateId,
            ["issued_at"] = issuedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["key_id"] = keyId,
            ["lock_id"] = lockId,
            ["milestone"] = milestone,
            ["schema"] = "cadence.kernel.database-lock-certificate.v1",
            ["signed_by"] = signedBy,
            ["study_code"] = studyCode,
            ["study_id"] = studyId,
            ["tenant_id"] = tenantId,
        }.ToJsonString();

        string canonical = ContentHash.Canonicalize(json);
        return Encoding.UTF8.GetBytes(canonical);
    }

    public static DatabaseLockCertificateRecord SignCertificate(
        string certificateId,
        string tenantId,
        string studyId,
        string studyCode,
        string lockId,
        LockMilestone milestone,
        string attestationId,
        string signedBy,
        DateTimeOffset issuedAt,
        string keyId,
        string privateKeyPem,
        string algorithm = DefaultAlgorithm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attestationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        string milestoneText = milestone.ToString();
        byte[] payloadBytes = BuildCanonicalPayloadBytes(
            certificateId,
            tenantId,
            studyId,
            studyCode,
            lockId,
            milestoneText,
            attestationId,
            signedBy,
            issuedAt,
            keyId,
            algorithm);

        string payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
        string signatureBase64 = AsymmetricSigner.AsymmetricSign(payloadBytes, privateKeyPem);
        string fingerprintSha256 = ComputeFingerprint(payloadSha256, signatureBase64);

        return new DatabaseLockCertificateRecord(
            CertificateId: certificateId,
            TenantId: tenantId,
            StudyId: studyId,
            StudyCode: studyCode,
            LockId: lockId,
            Milestone: milestone,
            AttestationId: attestationId,
            SignedBy: signedBy,
            IssuedAt: issuedAt,
            KeyId: keyId,
            Algorithm: algorithm,
            PayloadSha256: payloadSha256,
            FingerprintSha256: fingerprintSha256,
            SignatureBase64: signatureBase64,
            Revoked: false,
            RevokedAt: null,
            RevokedBy: null);
    }

    public static bool VerifyCertificate(
        DatabaseLockCertificateRecord? certificate,
        string publicKeyOrCertPem,
        out string? failureReason)
    {
        if (certificate is null)
        {
            failureReason = "Database lock certificate is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(publicKeyOrCertPem))
        {
            failureReason = "Kernel lock certificate public key PEM is not configured.";
            return false;
        }

        if (certificate.Revoked)
        {
            failureReason = $"Database lock certificate '{certificate.CertificateId}' has been revoked.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(certificate.CertificateId)
            || string.IsNullOrWhiteSpace(certificate.StudyId)
            || string.IsNullOrWhiteSpace(certificate.StudyCode)
            || string.IsNullOrWhiteSpace(certificate.LockId)
            || string.IsNullOrWhiteSpace(certificate.AttestationId)
            || string.IsNullOrWhiteSpace(certificate.SignedBy)
            || string.IsNullOrWhiteSpace(certificate.KeyId)
            || string.IsNullOrWhiteSpace(certificate.Algorithm)
            || string.IsNullOrWhiteSpace(certificate.SignatureBase64))
        {
            failureReason = "Database lock certificate is missing required claims.";
            return false;
        }

        byte[] payloadBytes = BuildCanonicalPayloadBytes(
            certificate.CertificateId,
            certificate.TenantId,
            certificate.StudyId,
            certificate.StudyCode,
            certificate.LockId,
            certificate.Milestone.ToString(),
            certificate.AttestationId,
            certificate.SignedBy,
            certificate.IssuedAt,
            certificate.KeyId,
            certificate.Algorithm);

        string recomputedPayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(recomputedPayloadSha256),
                Encoding.ASCII.GetBytes(certificate.PayloadSha256 ?? string.Empty)))
        {
            failureReason = "Database lock certificate payload SHA-256 digest mismatch.";
            return false;
        }

        string recomputedFingerprint = ComputeFingerprint(recomputedPayloadSha256, certificate.SignatureBase64);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(recomputedFingerprint),
                Encoding.ASCII.GetBytes(certificate.FingerprintSha256 ?? string.Empty)))
        {
            failureReason = "Database lock certificate fingerprint mismatch.";
            return false;
        }

        if (!AsymmetricSigner.AsymmetricVerify(payloadBytes, certificate.SignatureBase64, publicKeyOrCertPem))
        {
            failureReason = "Database lock certificate asymmetric signature failed verification.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static string ComputeFingerprint(string payloadSha256, string signatureBase64) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{payloadSha256}.{signatureBase64}")));
}

/// <summary>Configuration options for the kernel's asymmetric <see cref="DatabaseLockCertificateRecord"/> signer.</summary>
public sealed class KernelLockCertificateSignerOptions
{
    public const string PrivateKeyPemEnvironmentVariable = "CADENCE_KERNEL_LOCK_CERT_PRIVATE_KEY_PEM"; // pragma: allowlist secret
    public const string PublicKeyPemEnvironmentVariable = "CADENCE_KERNEL_LOCK_CERT_PUBLIC_KEY_PEM";
    public const string KeyIdEnvironmentVariable = "CADENCE_KERNEL_LOCK_CERT_KEY_ID";

    public string? PrivateKeyPem { get; set; }

    public string? KeyId { get; set; }
}

/// <summary>
/// Kernel-only asymmetric signer for <see cref="DatabaseLockCertificateRecord"/>. Uses the configured PEM private key
/// when provided, or an ephemeral runtime ECDSA P-256 keypair in local/test hosts where no private key PEM is set.
/// </summary>
public sealed class KernelLockCertificateSigner : ILockCertificateSigner
{
    private readonly string _privateKeyPem;
    private readonly string _algorithm;

    public KernelLockCertificateSigner(string? privateKeyPem = null, string? keyId = null)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _privateKeyPem = generated.ExportPkcs8PrivateKeyPem();
            PublicKeyPem = generated.ExportSubjectPublicKeyInfoPem();
            _algorithm = DatabaseLockCertificateCrypto.DefaultAlgorithm;
        }
        else
        {
            _privateKeyPem = privateKeyPem.Trim();
            (PublicKeyPem, _algorithm) = DerivePublicKeyAndAlgorithm(_privateKeyPem);
        }

        string thumbprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(PublicKeyPem)))[..12];
        KeyId = string.IsNullOrWhiteSpace(keyId) ? $"kernel-lock-{thumbprint}" : keyId.Trim();
    }

    /// <summary>SubjectPublicKeyInfo PEM corresponding to the active kernel signing key.</summary>
    public string PublicKeyPem { get; }

    /// <summary>Active key identifier embedded into signed certificates.</summary>
    public string KeyId { get; }

    public DatabaseLockCertificateRecord Sign(
        TenantId tenant,
        StudyId studyId,
        string studyCode,
        AssertionId lockId,
        LockMilestone milestone,
        AttestationId attestationId,
        string signedBy,
        DateTimeOffset issuedAt)
    {
        string certId = $"dblcert-{DeriveCertificateGuid(tenant, studyId, lockId):N}";
        return DatabaseLockCertificateCrypto.SignCertificate(
            certificateId: certId,
            tenantId: tenant.ToString(),
            studyId: studyId.ToString(),
            studyCode: studyCode,
            lockId: lockId.ToString(),
            milestone: milestone,
            attestationId: attestationId.ToString(),
            signedBy: signedBy,
            issuedAt: issuedAt,
            keyId: KeyId,
            privateKeyPem: _privateKeyPem,
            algorithm: _algorithm);
    }

    private static Guid DeriveCertificateGuid(TenantId tenant, StudyId studyId, AssertionId lockId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"cadence:dblcert:{tenant}:{studyId}:{lockId}"));
        Span<byte> bytes = stackalloc byte[16];
        digest.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static (string PublicKeyPem, string Algorithm) DerivePublicKeyAndAlgorithm(string privateKeyPem)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);
            return (ecdsa.ExportSubjectPublicKeyInfoPem(), DatabaseLockCertificateCrypto.DefaultAlgorithm);
        }
        catch (ArgumentException)
        {
            // Fall through to RSA.
        }
        catch (CryptographicException)
        {
            // Fall through to RSA.
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        return (rsa.ExportSubjectPublicKeyInfoPem(), DatabaseLockCertificateCrypto.RsaAlgorithm);
    }
}
