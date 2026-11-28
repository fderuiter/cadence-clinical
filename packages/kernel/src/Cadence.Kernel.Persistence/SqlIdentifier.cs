using Cadence.Kernel.Sql;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Quoting for the identifiers kernel DDL helpers interpolate into SQL (schemas, tables, columns, triggers, roles).
/// Only lower-case snake_case names are accepted, so nothing a caller passes can change the statement.
/// </summary>
internal static class SqlIdentifier
{
    /// <summary>PostgreSQL's identifier length limit.</summary>
    public const int MaxLength = PostgresIdentifier.MaxLength;

    public static string Quote(string identifier) => PostgresIdentifier.Quote(identifier);

    /// <summary>Cuts a generated name to <see cref="MaxLength"/>, as PostgreSQL would.</summary>
    public static string Truncate(string name) => PostgresIdentifier.Truncate(name);
}
