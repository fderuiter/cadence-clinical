using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace Cadence.Kernel.Persistence.Background;

/// <summary>
/// Runs a background job's pass on one replica at a time (ADR-2601): the pass holds a PostgreSQL session advisory lock
/// named after the job on its own connection, and a replica that cannot take the lock skips the pass. A replica that
/// dies mid-pass loses its connection and so its lock. This keeps one outbox relay per deployment, so each tenant's
/// events arrive in order, and one sealing pass at a time.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelLeaderLock(KernelConnectionString connection)
{
    /// <summary>Runs <paramref name="pass"/> if this replica takes <paramref name="job"/>'s lock; false when another holds it.</summary>
    public async Task<bool> TryRunAsync(string job, Func<CancellationToken, Task> pass, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job);
        ArgumentNullException.ThrowIfNull(pass);
        var key = Key(job);
        await using var session = new NpgsqlConnection(connection.Value);
        await session.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await ScalarAsync<bool>(session, "SELECT pg_try_advisory_lock(@key)", key, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            await pass(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            // Closing the session also releases it; unlocking first returns the pooled connection clean.
            await ScalarAsync<bool>(session, "SELECT pg_advisory_unlock(@key)", key, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The job's lock key: the first eight bytes of SHA-256 over <c>cadence.kernel.leader:&lt;job&gt;</c>.</summary>
    internal static long Key(string job) =>
        BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"cadence.kernel.leader:{job}")));

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection session, string sql, long key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, session);
        command.Parameters.AddWithValue("key", key);
        return (T)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}

/// <summary>The kernel's application-role connection string, for work that needs a session of its own.</summary>
internal sealed record KernelConnectionString(string Value);
