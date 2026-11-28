using System;
using System.Security.Cryptography;

using Cadence.Contracts;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Evidence;
using Cadence.Security;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Interop.Infrastructure.Security;

/// <summary>
/// Public-key verification options for kernel-signed <see cref="DatabaseLockCertificateContract"/> envelopes.
/// Interop SCE hosts hold ONLY the public verification key PEM (<c>SubjectPublicKeyInfo</c> or X.509 certificate),
/// never private signing key material.
/// </summary>
public sealed class KernelLockCertificateVerifierOptions
{
    public const string ConfigurationKey = KernelLockCertificateSignerOptions.PublicKeyPemEnvironmentVariable;
    public const string SectionKey = "KernelLockCertificates:PublicKeyPem";

    public string? PublicKeyPem { get; set; }
}

public interface IKernelLockCertificateVerifier
{
    bool TryVerifyForStudyLock(
        DatabaseLockCertificateContract? certificate,
        string expectedStudy,
        string expectedLockId,
        string? callerTenantCode,
        out string? failureReason);
}

public sealed class KernelLockCertificateVerifier(IOptions<KernelLockCertificateVerifierOptions> options)
    : IKernelLockCertificateVerifier
{
    private readonly string? _publicKeyPem = options.Value.PublicKeyPem?.Trim();

    public static bool IsAcceptableConfiguredPem(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return true;
        }

        if (pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            return true;
        }
        catch (ArgumentException)
        {
            // Fall through to RSA / X.509.
        }
        catch (CryptographicException)
        {
            // Fall through to RSA / X.509.
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public bool TryVerifyForStudyLock(
        DatabaseLockCertificateContract? certificate,
        string expectedStudy,
        string expectedLockId,
        string? callerTenantCode,
        out string? failureReason)
    {
        if (certificate is null)
        {
            failureReason = "Unblinded SCE authorization requires a kernel-signed DatabaseLockCertificate.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_publicKeyPem))
        {
            failureReason = $"{KernelLockCertificateVerifierOptions.ConfigurationKey} is not configured on the SCE host.";
            return false;
        }

        if (_publicKeyPem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "SCE host refuses private key material for lock certificate verification.";
            return false;
        }

        if (!Enum.TryParse<LockMilestone>(certificate.Milestone, ignoreCase: true, out var milestone)
            || !Enum.IsDefined(milestone))
        {
            failureReason = $"Invalid lock certificate milestone '{certificate.Milestone}'.";
            return false;
        }

        string normalizedTenant = NormalizePrefix(certificate.TenantCode, "tenant:");
        string normalizedStudyId = NormalizePrefix(certificate.StudyRef, "study:");
        string normalizedLockId = NormalizePrefix(certificate.LockId, "assertion:");
        string normalizedAttestationId = NormalizePrefix(certificate.AttestationId, "attestation:");

        var record = new DatabaseLockCertificateRecord(
            CertificateId: certificate.CertificateId,
            TenantId: normalizedTenant,
            StudyId: normalizedStudyId,
            StudyCode: certificate.StudyCode,
            LockId: normalizedLockId,
            Milestone: milestone,
            AttestationId: normalizedAttestationId,
            SignedBy: certificate.SignedBy,
            IssuedAt: certificate.IssuedAt,
            KeyId: certificate.KeyId,
            Algorithm: certificate.Algorithm,
            PayloadSha256: certificate.PayloadSha256,
            FingerprintSha256: certificate.FingerprintSha256,
            SignatureBase64: certificate.SignatureBase64,
            Revoked: false,
            RevokedAt: null,
            RevokedBy: null);

        if (!DatabaseLockCertificateCrypto.VerifyCertificate(record, _publicKeyPem, out failureReason))
        {
            return false;
        }

        string expectedStudyNormalized = NormalizePrefix(expectedStudy, "study:");
        bool studyMatches =
            string.Equals(expectedStudy, certificate.StudyCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(expectedStudy, certificate.StudyRef, StringComparison.OrdinalIgnoreCase)
            || string.Equals(expectedStudyNormalized, normalizedStudyId, StringComparison.OrdinalIgnoreCase);
        if (!studyMatches)
        {
            failureReason = $"Database lock certificate study '{certificate.StudyCode}' does not match requested study '{expectedStudy}'.";
            return false;
        }

        string expectedLockNormalized = NormalizePrefix(expectedLockId, "assertion:");
        if (!string.Equals(expectedLockNormalized, normalizedLockId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(expectedLockId, certificate.LockId, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = $"Database lock certificate lock '{certificate.LockId}' does not match requested lock '{expectedLockId}'.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(callerTenantCode) && !string.IsNullOrWhiteSpace(normalizedTenant))
        {
            string callerNormalized = NormalizePrefix(callerTenantCode, "tenant:");
            bool tenantMatches =
                string.Equals(callerNormalized, normalizedTenant, StringComparison.OrdinalIgnoreCase)
                || StandaloneTenantModelExtensions.ParseOrEmpty(callerNormalized) == StandaloneTenantModelExtensions.ParseOrEmpty(normalizedTenant);
            if (!tenantMatches)
            {
                failureReason = "Database lock certificate tenant does not match caller tenant.";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    internal static string NormalizePrefix(string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }
}

public static class KernelLockCertificateVerifierServiceCollectionExtensions
{
    public static IServiceCollection AddKernelLockCertificateVerification(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<KernelLockCertificateVerifierOptions>()
            .Configure<IServiceProvider>((options, sp) =>
            {
                var cfg = sp.GetService<IConfiguration>();
                options.PublicKeyPem ??= cfg?[KernelLockCertificateVerifierOptions.SectionKey]
                    ?? cfg?[KernelLockCertificateVerifierOptions.ConfigurationKey];
            })
            .Validate(
                options => KernelLockCertificateVerifier.IsAcceptableConfiguredPem(options.PublicKeyPem),
                $"{KernelLockCertificateVerifierOptions.ConfigurationKey} must be a valid SubjectPublicKeyInfo PEM public key (never a private key).")
            .ValidateOnStart();

        services.AddSingleton<IKernelLockCertificateVerifier, KernelLockCertificateVerifier>();
        return services;
    }
}
