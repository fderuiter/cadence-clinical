using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Resolves a gateway-signed tenant code (<c>X-Tenant-Id</c>) to its immutable <see cref="Guid"/> primary key
/// without referencing <c>Cadence.Kernel.Persistence</c> (K-XS-2, ADR-2250, #4780).
/// </summary>
public interface ITenantCodeResolver
{
    ValueTask<Guid?> ResolveTenantIdAsync(string tenantCode, CancellationToken cancellationToken = default);

    Guid? ResolveTenantId(string tenantCode);

    Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves active tenant codes against PostgreSQL via <c>kernel.tenant_id_by_code(text)</c> and enumerates
/// tenant IDs for background workers via <c>kernel.tenant_ids()</c>.
/// </summary>
public sealed class PostgresTenantCodeResolver : ITenantCodeResolver, IDisposable, IAsyncDisposable
{
    private const string ResolveByCodeSql = "SELECT kernel.tenant_id_by_code(@code)";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresTenantCodeResolver(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var normalized = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = "public",
        }.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(normalized);
    }

    public async ValueTask<Guid?> ResolveTenantIdAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return null;
        }

        string trimmed = tenantCode.Trim();
        await using var command = _dataSource.CreateCommand(ResolveByCodeSql);
        command.Parameters.AddWithValue("code", trimmed);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is Guid id ? id : null;
    }

    public Guid? ResolveTenantId(string tenantCode)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return null;
        }

        string trimmed = tenantCode.Trim();
        using var command = _dataSource.CreateCommand(ResolveByCodeSql);
        command.Parameters.AddWithValue("code", trimmed);
        object? scalar = command.ExecuteScalar();
        return scalar is Guid id ? id : null;
    }

    public async Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var checkCmd = _dataSource.CreateCommand("SELECT to_regprocedure('kernel.tenant_ids()') IS NOT NULL");
        object? exists = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (exists is not true)
        {
            return [];
        }

        var ids = new List<Guid>();
        await using var cmd = _dataSource.CreateCommand("SELECT kernel.tenant_ids()");
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        return ids;
    }

    public void Dispose() => _dataSource.Dispose();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

/// <summary>
/// Deterministic RFC 4122 tenant-code resolver for in-memory and SQLite test hosts where PostgreSQL's
/// <c>kernel.tenant_id_by_code(text)</c> directory function is absent.
/// </summary>
public sealed class DeterministicTenantCodeResolver : ITenantCodeResolver
{
    private readonly ConcurrentDictionary<string, Guid> _activeTenants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inactiveTenants = new(StringComparer.Ordinal);

    public static Guid ComputeDeterministicGuid(string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        string trimmed = tenantCode.Trim();
        if (Guid.TryParse(trimmed, out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes("cadence-tenant:" + trimmed), hash);
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        var id = new Guid(guidBytes);
        return id == Guid.Empty ? new Guid(0, 0, 0x5000, 0x80, 0, 0, 0, 0, 0, 0, 1) : id;
    }

    public void DeactivateTenant(string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        _inactiveTenants[tenantCode.Trim()] = 1;
        _activeTenants.TryRemove(tenantCode.Trim(), out _);
    }

    public void ActivateTenant(string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        string trimmed = tenantCode.Trim();
        _inactiveTenants.TryRemove(trimmed, out _);
        _activeTenants[trimmed] = ComputeDeterministicGuid(trimmed);
    }

    public ValueTask<Guid?> ResolveTenantIdAsync(string tenantCode, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResolveTenantId(tenantCode));

    public Guid? ResolveTenantId(string tenantCode)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return null;
        }

        string trimmed = tenantCode.Trim();
        if (_inactiveTenants.ContainsKey(trimmed))
        {
            return null;
        }

        return _activeTenants.GetOrAdd(trimmed, ComputeDeterministicGuid);
    }

    public Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. _activeTenants.Values]);
}
