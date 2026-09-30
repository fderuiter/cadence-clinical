using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// Registers field-level protection services. Keys are read from configuration and validated when the host starts:
/// a missing or malformed key stops the service from starting (fail closed). There are no default keys.
/// @req:PRD-SYS-053
/// </summary>
public static class FieldProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFieldEncryptor"/> (AES-256-GCM) keyed by <c>FIELD_ENCRYPTION_KEY</c>.
    /// </summary>
    public static IServiceCollection AddCadenceFieldEncryption(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<FieldEncryptionOptions>()
            .Configure<IServiceProvider>((options, sp) =>
                options.KeyBase64 ??= sp.GetService<IConfiguration>()?[FieldEncryptionOptions.ConfigurationKey])
            .Validate(
                options => FieldProtectionKey.IsValid(options.KeyBase64),
                $"{FieldEncryptionOptions.ConfigurationKey} must be set to a base64-encoded {FieldProtectionKey.KeySizeBytes}-byte key.")
            .ValidateOnStart();

        services.TryAddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ILookupHasher"/> (HMAC-SHA256) keyed by <c>FIELD_LOOKUP_HASH_KEY</c>.
    /// </summary>
    public static IServiceCollection AddCadenceLookupHashing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<LookupHashOptions>()
            .Configure<IServiceProvider>((options, sp) =>
                options.KeyBase64 ??= sp.GetService<IConfiguration>()?[LookupHashOptions.ConfigurationKey])
            .Validate(
                options => FieldProtectionKey.IsValid(options.KeyBase64),
                $"{LookupHashOptions.ConfigurationKey} must be set to a base64-encoded {FieldProtectionKey.KeySizeBytes}-byte key.")
            .ValidateOnStart();

        services.TryAddSingleton<ILookupHasher, HmacLookupHasher>();
        return services;
    }
}
