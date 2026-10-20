using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;

namespace Cadence.Apps.Interop.Infrastructure.Security;

/// <summary>
/// Resolves the private key that signs a registered EHR endpoint's SMART Backend Services client assertions. Keys are
/// held server-side and named by the endpoint's <c>SigningKeyRef</c>; a request never carries one (#5436).
/// </summary>
public interface ISmartSigningKeys
{
    /// <summary>
    /// Returns the RSA private key named <paramref name="keyRef"/>, which the caller disposes, or false with the reason
    /// when the reference is missing, not configured, or not a usable RSA private key of at least 2048 bits.
    /// </summary>
    bool TryGetPrivateKey(string? keyRef, [NotNullWhen(true)] out RSA? key, out string error);
}

/// <summary>
/// Reads each key as PEM from configuration <c>SmartSigningKeys:&lt;ref&gt;</c> (environment variable
/// <c>SmartSigningKeys__&lt;ref&gt;</c>, normally fed from a secret store). There is no default and no generated
/// fallback: a missing or invalid key signs nothing (AGENTS.md §5, ADR-2256).
/// </summary>
public sealed partial class ConfigurationSmartSigningKeys(IConfiguration configuration) : ISmartSigningKeys
{
    /// <summary>The configuration section the keys are read from.</summary>
    public const string SectionName = "SmartSigningKeys";

    /// <summary>The smallest RSA key accepted for RS384/RS256 client assertions.</summary>
    public const int MinimumKeySizeBits = 2048;

    /// <summary>Whether <paramref name="keyRef"/> can name a key: letters, digits and underscores, at most 64.</summary>
    public static bool IsValidReference(string? keyRef) => keyRef is not null && KeyRefPattern().IsMatch(keyRef);

    /// <inheritdoc />
    public bool TryGetPrivateKey(string? keyRef, [NotNullWhen(true)] out RSA? key, out string error)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(keyRef))
        {
            error = "The endpoint has no signing key reference.";
            return false;
        }

        if (!IsValidReference(keyRef) || configuration[$"{SectionName}:{keyRef}"] is not { Length: > 0 } pem)
        {
            error = $"Signing key '{keyRef}' is not configured.";
            return false;
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            // A public key imports too; only a private key can sign.
            _ = rsa.ExportParameters(includePrivateParameters: true);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            error = $"Signing key '{keyRef}' is not a valid RSA private key in PEM form.";
            return false;
        }

        if (rsa.KeySize < MinimumKeySizeBits)
        {
            rsa.Dispose();
            error = $"Signing key '{keyRef}' is shorter than {MinimumKeySizeBits} bits.";
            return false;
        }

        key = rsa;
        error = string.Empty;
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex KeyRefPattern();
}
