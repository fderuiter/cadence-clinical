using System;
using System.Text.Json;

namespace Cadence.Cli.Services;

/// <summary>
/// The pnpm version pinned by the root <c>package.json</c> <c>packageManager</c> field (#5331), and how
/// <c>cadence doctor</c> compares it with the pnpm on the PATH.
/// </summary>
public static class PnpmPin
{
    /// <summary>Reads the pinned version from <c>package.json</c> text, or <see langword="null"/> when no pnpm pin is declared.</summary>
    public static string? PinnedVersion(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson);
        if (!document.RootElement.TryGetProperty("packageManager", out var field) || field.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string value = field.GetString() ?? string.Empty;
        if (!value.StartsWith("pnpm@", StringComparison.Ordinal))
        {
            return null;
        }

        string version = value["pnpm@".Length..];
        int hash = version.IndexOf('+', StringComparison.Ordinal);
        return hash >= 0 ? version[..hash] : version;
    }

    /// <summary>
    /// Describes how the installed pnpm compares with the pin. <paramref name="installed"/> is the trimmed output of
    /// <c>pnpm --version</c>, or <see langword="null"/> when pnpm could not be started or crashed.
    /// </summary>
    public static PnpmPinStatus Compare(string? pinned, string? installed)
    {
        if (pinned is null)
        {
            return PnpmPinStatus.NotPinned;
        }

        if (string.IsNullOrWhiteSpace(installed))
        {
            return PnpmPinStatus.Unusable;
        }

        return string.Equals(pinned, installed.Trim(), StringComparison.Ordinal) ? PnpmPinStatus.Matches : PnpmPinStatus.Mismatch;
    }
}

/// <summary>The result of <see cref="PnpmPin.Compare"/>.</summary>
public enum PnpmPinStatus
{
    /// <summary>The installed pnpm is the pinned version.</summary>
    Matches,

    /// <summary>pnpm runs, but at a different version than the pin.</summary>
    Mismatch,

    /// <summary>pnpm is missing or crashes when asked for its version.</summary>
    Unusable,

    /// <summary><c>package.json</c> declares no pnpm <c>packageManager</c>.</summary>
    NotPinned,
}
