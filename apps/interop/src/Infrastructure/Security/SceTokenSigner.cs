using System;

using Cadence.Security.Authentication;
using Cadence.Security.FieldProtection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Interop.Infrastructure.Security;

/// <summary>The SCE signing key, read from <c>CADENCE_SCE_SIGNING_KEY</c> (#5382).</summary>
public sealed class SceSigningOptions
{
    /// <summary>The configuration key the signing key is read from.</summary>
    public const string ConfigurationKey = "CADENCE_SCE_SIGNING_KEY";

    /// <summary>A base64-encoded 32-byte HMAC-SHA256 key. There is no default.</summary>
    public string? KeyBase64 { get; set; }
}

/// <summary>
/// Signs and verifies SCE database-lock tokens and run signature blocks with HMAC-SHA256 keyed by
/// <see cref="SceSigningOptions"/>, through <see cref="HmacSigner"/>. The key is validated at startup, so the host
/// does not start without one (ADR-2515).
/// </summary>
public sealed class SceTokenSigner(IOptions<SceSigningOptions> options)
{
    private readonly string _key = options.Value.KeyBase64
        ?? throw new InvalidOperationException($"{SceSigningOptions.ConfigurationKey} is not configured.");

    /// <summary>The lower-case hex HMAC-SHA256 of <paramref name="payload"/>.</summary>
    public string Sign(string payload) => HmacSigner.ComputeSignature(payload, _key);

    /// <summary>Whether <paramref name="signature"/> is this key's signature of <paramref name="payload"/> (fixed time).</summary>
    public bool Verify(string payload, string? signature) =>
        !string.IsNullOrEmpty(signature) && HmacSigner.VerifySignature(payload, _key, signature);
}

/// <summary>Registers <see cref="SceTokenSigner"/> with fail-closed key validation.</summary>
public static class SceSigningServiceCollectionExtensions
{
    /// <summary>Registers the signer; a missing or malformed <c>CADENCE_SCE_SIGNING_KEY</c> stops the host starting.</summary>
    public static IServiceCollection AddSceSigning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SceSigningOptions>()
            .Configure<IServiceProvider>((options, sp) =>
                options.KeyBase64 ??= sp.GetService<IConfiguration>()?[SceSigningOptions.ConfigurationKey])
            .Validate(
                options => FieldProtectionKey.IsValid(options.KeyBase64),
                $"{SceSigningOptions.ConfigurationKey} must be set to a base64-encoded {FieldProtectionKey.KeySizeBytes}-byte key.")
            .ValidateOnStart();

        services.AddSingleton<SceTokenSigner>();
        return services;
    }
}
