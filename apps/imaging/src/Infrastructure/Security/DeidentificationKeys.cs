using System;

using Cadence.Deid;
using Cadence.Security.FieldProtection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Imaging.Infrastructure.Security;

/// <summary>
/// The keys imaging de-identifies with (#5399): one signs certificates and batch manifests, the other keys the
/// anonymized UIDs and original-UID hashes. Both are base64 of 32 random bytes from configuration, with no default.
/// </summary>
public sealed class ImagingDeidentificationOptions
{
    /// <summary>The configuration key the certificate and manifest signing key is read from.</summary>
    public const string SigningKeyConfigurationKey = "CADENCE_IMAGING_DEID_SIGNING_KEY";

    /// <summary>The configuration key the UID pseudonymization key is read from.</summary>
    public const string PseudonymizationKeyConfigurationKey = "CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY";

    /// <summary>Base64 of the 32-byte HMAC-SHA256 key that signs certificates and manifests.</summary>
    public string? SigningKeyBase64 { get; set; }

    /// <summary>Base64 of the 32-byte HMAC-SHA256 key that derives anonymized UIDs and original-UID hashes.</summary>
    public string? PseudonymizationKeyBase64 { get; set; }

    /// <summary>True when both keys are valid and are not the same key.</summary>
    public static bool IsValid(ImagingDeidentificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return FieldProtectionKey.IsValid(options.SigningKeyBase64)
            && FieldProtectionKey.IsValid(options.PseudonymizationKeyBase64)
            && !string.Equals(options.SigningKeyBase64!.Trim(), options.PseudonymizationKeyBase64!.Trim(), StringComparison.Ordinal);
    }
}

/// <summary>Registers imaging's <see cref="DicomDeidentifier"/> with fail-closed key validation.</summary>
public static class ImagingDeidentificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="DicomDeidentifier"/> built from <see cref="ImagingDeidentificationOptions"/>. A missing,
    /// malformed or shared key stops the host starting (ADR-2256).
    /// </summary>
    public static IServiceCollection AddImagingDeidentification(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ImagingDeidentificationOptions>()
            .Configure<IServiceProvider>((options, sp) =>
            {
                var configuration = sp.GetService<IConfiguration>();
                options.SigningKeyBase64 ??= configuration?[ImagingDeidentificationOptions.SigningKeyConfigurationKey];
                options.PseudonymizationKeyBase64 ??= configuration?[ImagingDeidentificationOptions.PseudonymizationKeyConfigurationKey];
            })
            .Validate(
                ImagingDeidentificationOptions.IsValid,
                $"{ImagingDeidentificationOptions.SigningKeyConfigurationKey} and {ImagingDeidentificationOptions.PseudonymizationKeyConfigurationKey} must each be set to a different base64-encoded {FieldProtectionKey.KeySizeBytes}-byte key.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ImagingDeidentificationOptions>>().Value;
            return new DicomDeidentifier(
                FieldProtectionKey.Decode(options.SigningKeyBase64, ImagingDeidentificationOptions.SigningKeyConfigurationKey),
                FieldProtectionKey.Decode(options.PseudonymizationKeyBase64, ImagingDeidentificationOptions.PseudonymizationKeyConfigurationKey));
        });
        return services;
    }
}
