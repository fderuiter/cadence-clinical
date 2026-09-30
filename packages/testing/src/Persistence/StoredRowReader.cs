using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Testing.Persistence;

/// <summary>
/// Reads rows exactly as the database stores them, bypassing EF Core value converters, so tests can assert what is
/// on disk (for example, that an encrypted or hashed column never holds plaintext).
/// </summary>
public static class StoredRowReader
{
    /// <summary>
    /// Returns every row of <paramref name="table"/> as a column-name to invariant-string map (<c>null</c> for SQL NULL).
    /// The reader is closed before returning, so the same connection can be used by a DbContext afterwards.
    /// </summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="table">A table name from the test's own model (not user input).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadAllAsync(
        DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var rows = new List<IReadOnlyDictionary<string, string?>>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken)
                    ? null
                    : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            }

            rows.Add(row);
        }

        return rows;
    }
}
