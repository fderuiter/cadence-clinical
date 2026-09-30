using System;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Quoting for the identifiers kernel DDL helpers interpolate into SQL (schemas, tables, columns, triggers, roles).
/// Only lower-case snake_case names are accepted, so nothing a caller passes can change the statement.
/// </summary>
internal static partial class SqlIdentifier
{
    /// <summary>PostgreSQL's identifier length limit.</summary>
    public const int MaxLength = 63;

    public static string Quote(string identifier) =>
        SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a lower-case snake_case identifier.", nameof(identifier));

    /// <summary>Cuts a generated name to <see cref="MaxLength"/>, as PostgreSQL would.</summary>
    public static string Truncate(string name) => name.Length <= MaxLength ? name : name[..MaxLength];

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SafeIdentifier();
}
