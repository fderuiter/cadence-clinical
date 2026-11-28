using System;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Sql;

/// <summary>
/// Pure BCL PostgreSQL identifier validation, quoting, and 63-byte truncation shared by kernel, module, and
/// standalone persistence helpers.
/// </summary>
public static partial class PostgresIdentifier
{
    /// <summary>PostgreSQL's identifier length limit.</summary>
    public const int MaxLength = 63;

    /// <summary>
    /// Validates and double-quotes <paramref name="identifier"/>. By default only lower-case <c>snake_case</c>
    /// identifiers are accepted; standalone schemas may set <paramref name="allowMixedCase"/> to <see langword="true"/>
    /// for EF Core metadata identifiers such as <c>__EFMigrationsHistory</c> or <c>VersionIndex</c>.
    /// </summary>
    public static string Quote(
        string identifier,
        bool allowMixedCase = false,
        string? invalidKindLabel = null,
        string? paramName = null)
    {
        Validate(identifier, allowMixedCase, invalidKindLabel, paramName);
        return $"\"{identifier}\"";
    }

    /// <summary>
    /// Validates <paramref name="identifier"/> against PostgreSQL identifier rules without quoting it.
    /// </summary>
    public static void Validate(
        string identifier,
        bool allowMixedCase = false,
        string? invalidKindLabel = null,
        string? paramName = null)
    {
        bool valid = identifier is not null
            && (allowMixedCase ? MixedCaseIdentifier().IsMatch(identifier) : LowerSnakeCaseIdentifier().IsMatch(identifier));
        if (!valid)
        {
            string kind = invalidKindLabel
                ?? (allowMixedCase ? "valid PostgreSQL identifier" : "lower-case snake_case identifier");
            throw new ArgumentException($"'{identifier}' is not a {kind}.", paramName ?? nameof(identifier));
        }
    }

    /// <summary>Truncates a generated constraint or trigger name to <see cref="MaxLength"/> characters.</summary>
    public static string Truncate(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return identifier.Length <= MaxLength ? identifier : identifier[..MaxLength];
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerSnakeCaseIdentifier();

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex MixedCaseIdentifier();
}
