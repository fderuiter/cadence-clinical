using System;

using Cadence.Security.FieldProtection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Security.Authentication;

/// <summary>
/// Configuration for HMAC-SHA256 signing and verification of short-lived biometric liveness tokens.
/// Set <see cref="ConfigurationKey"/> to 32 random bytes encoded as base64; hosts refuse to start without it.
/// </summary>
public sealed class BiometricLivenessOptions
{
    /// <summary>Configuration key (<c>CADENCE_BIOMETRIC_LIVENESS_KEY</c>).</summary>
    public const string ConfigurationKey = "CADENCE_BIOMETRIC_LIVENESS_KEY";

    /// <summary>Base64-encoded 256-bit HMAC key.</summary>
    public string? KeyBase64 { get; set; }

    /// <summary>Decodes and validates the configured 32-byte HMAC key.</summary>
    public byte[] DecodeKey() => FieldProtectionKey.Decode(KeyBase64, ConfigurationKey);
}

/// <summary>
/// Registers startup-validated <see cref="BiometricLivenessOptions"/> so hosts that mint or verify
/// biometric liveness tokens fail closed when <c>CADENCE_BIOMETRIC_LIVENESS_KEY</c> is missing or malformed.
/// </summary>
public static class BiometricLivenessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BiometricLivenessOptions"/> keyed by <c>CADENCE_BIOMETRIC_LIVENESS_KEY</c> with startup validation.
    /// </summary>
    public static IServiceCollection AddCadenceBiometricLiveness(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BiometricLivenessOptions>()
            .Configure<IServiceProvider>((options, sp) =>
                options.KeyBase64 ??= sp.GetService<IConfiguration>()?[BiometricLivenessOptions.ConfigurationKey])
            .Validate(
                options => FieldProtectionKey.IsValid(options.KeyBase64),
                $"{BiometricLivenessOptions.ConfigurationKey} must be set to a base64-encoded {FieldProtectionKey.KeySizeBytes}-byte key.")
            .ValidateOnStart();

        return services;
    }
}
