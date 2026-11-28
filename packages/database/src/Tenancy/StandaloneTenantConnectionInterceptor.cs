using System;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Sql;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Npgsql;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Binds <c>cadence.tenant_id</c> via <c>pg_catalog.set_config('cadence.tenant_id', @tenant, false)</c> when a
/// standalone PostgreSQL <see cref="DbConnection"/> opens, acquires a scoped <see cref="DbContext"/> connection lease,
/// rebinds (or fails closed) if <see cref="ITenantScope"/> changes mid-scope, and clears <c>cadence.tenant_id</c>
/// before the connection returns to <see cref="NpgsqlDataSource"/> (K-ID-2, ADR-2250, ADR-2548).
/// </summary>
public sealed class StandaloneTenantConnectionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
{
    public static readonly StandaloneTenantConnectionInterceptor Instance = new();

    private const string SetTenantSql = PostgresTenantSession.SetTenantSql;
    private const string InvalidTenantBindingMessage =
        "K-ID-2: cannot bind standalone database connection to an empty, unknown, or inactive tenant.";

    private static readonly ConditionalWeakTable<DbConnection, BoundConnectionState> BoundStates = new();

    private sealed class BoundConnectionState
    {
        public string BoundTenant { get; set; } = string.Empty;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        (string tenant, bool isInvalid) = ResolveTenantSetting(eventData.Context);
        if (isInvalid)
        {
            ResetAndCloseOnInvalidBinding(connection);
            throw new InvalidOperationException(InvalidTenantBindingMessage);
        }

        ExecuteSetTenant(connection, transaction: null, tenant);
        BoundStates.GetOrCreateValue(connection).BoundTenant = tenant;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        (string tenant, bool isInvalid) = await ResolveTenantSettingAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        if (isInvalid)
        {
            await ResetAndCloseOnInvalidBindingAsync(connection, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(InvalidTenantBindingMessage);
        }

        await ExecuteSetTenantAsync(connection, transaction: null, tenant, cancellationToken).ConfigureAwait(false);
        BoundStates.GetOrCreateValue(connection).BoundTenant = tenant;
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        BoundStates.Remove(connection);
        if (connection.State == ConnectionState.Open)
        {
            try
            {
                ExecuteSetTenant(connection, transaction: null, string.Empty);
            }
            catch
            {
                EvictDirtyPoolConnection(connection);
                throw;
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        BoundStates.Remove(connection);
        if (connection.State == ConnectionState.Open)
        {
            try
            {
                await ExecuteSetTenantAsync(connection, transaction: null, string.Empty, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                EvictDirtyPoolConnection(connection);
                throw;
            }
        }

        return result;
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        BoundStates.Remove(connection);
        ReleaseBrokenLeaseIfClosed(connection, eventData.Context);
        base.ConnectionFailed(connection, eventData);
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        BoundStates.Remove(connection);
        ReleaseBrokenLeaseIfClosed(connection, eventData.Context);
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        EnsureBoundTenantMatchesScope(command, eventData.Context);
        return result;
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureBoundTenantMatchesScopeAsync(command, eventData.Context, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        EnsureBoundTenantMatchesScope(command, eventData.Context);
        return result;
    }

    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureBoundTenantMatchesScopeAsync(command, eventData.Context, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        EnsureBoundTenantMatchesScope(command, eventData.Context);
        return result;
    }

    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureBoundTenantMatchesScopeAsync(command, eventData.Context, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        if (command.Connection is { } conn)
        {
            ReleaseBrokenLeaseIfClosed(conn, eventData.Context);
        }
    }

    public Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (command.Connection is { } conn)
        {
            ReleaseBrokenLeaseIfClosed(conn, eventData.Context);
        }

        return Task.CompletedTask;
    }

    private static void EnsureBoundTenantMatchesScope(DbCommand command, DbContext? context)
    {
        if (context is null
            || command.Connection is not { State: ConnectionState.Open } connection
            || string.Equals(command.CommandText, SetTenantSql, StringComparison.Ordinal))
        {
            return;
        }

        (string desiredTenant, bool isInvalid) = ResolveTenantSetting(context);
        BoundConnectionState state = BoundStates.GetOrCreateValue(connection);
        if (isInvalid)
        {
            if (state.BoundTenant.Length > 0)
            {
                ExecuteSetTenant(connection, command.Transaction, string.Empty);
                state.BoundTenant = string.Empty;
            }

            throw new InvalidOperationException(InvalidTenantBindingMessage);
        }

        if (!string.Equals(state.BoundTenant, desiredTenant, StringComparison.Ordinal))
        {
            ExecuteSetTenant(connection, command.Transaction, desiredTenant);
            state.BoundTenant = desiredTenant;
        }

        AcquireScopeLease(context);
    }

    private static async ValueTask EnsureBoundTenantMatchesScopeAsync(
        DbCommand command,
        DbContext? context,
        CancellationToken cancellationToken)
    {
        if (context is null
            || command.Connection is not { State: ConnectionState.Open } connection
            || string.Equals(command.CommandText, SetTenantSql, StringComparison.Ordinal))
        {
            return;
        }

        (string desiredTenant, bool isInvalid) = await ResolveTenantSettingAsync(context, cancellationToken).ConfigureAwait(false);
        BoundConnectionState state = BoundStates.GetOrCreateValue(connection);
        if (isInvalid)
        {
            if (state.BoundTenant.Length > 0)
            {
                await ExecuteSetTenantAsync(connection, command.Transaction, string.Empty, cancellationToken).ConfigureAwait(false);
                state.BoundTenant = string.Empty;
            }

            throw new InvalidOperationException(InvalidTenantBindingMessage);
        }

        if (!string.Equals(state.BoundTenant, desiredTenant, StringComparison.Ordinal))
        {
            await ExecuteSetTenantAsync(connection, command.Transaction, desiredTenant, cancellationToken).ConfigureAwait(false);
            state.BoundTenant = desiredTenant;
        }

        AcquireScopeLease(context);
    }

    private static void AcquireScopeLease(DbContext context)
    {
        var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        ext?.ResolveLeaseManager()?.AcquireLease(context);
    }

    internal static void ForceResetAndClose(DbConnection connection)
    {
        ResetAndCloseOnInvalidBinding(connection);
    }

    internal static ValueTask ForceResetAndCloseAsync(DbConnection connection)
    {
        return ResetAndCloseOnInvalidBindingAsync(connection, CancellationToken.None);
    }

    private static (string Tenant, bool IsInvalid) ResolveTenantSetting(DbContext? context)
    {
        var ext = context?.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return (string.Empty, false);
        }

        if (ext.TenantAccessor is CurrentTenantAccessor currentAccessor)
        {
            var resolution = currentAccessor.ResolveBindingState();
            return resolution.Status switch
            {
                CurrentTenantAccessor.TenantBindingStatus.Bound => (resolution.TenantId.ToString("D"), false),
                CurrentTenantAccessor.TenantBindingStatus.Invalid => (string.Empty, true),
                _ => (string.Empty, false),
            };
        }

        Guid? tenantId = ext.TenantAccessor.GetCurrentTenantId();
        return tenantId is { } id && id != Guid.Empty
            ? (id.ToString("D"), false)
            : (string.Empty, false);
    }

    private static async ValueTask<(string Tenant, bool IsInvalid)> ResolveTenantSettingAsync(
        DbContext? context,
        CancellationToken cancellationToken)
    {
        var ext = context?.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return (string.Empty, false);
        }

        if (ext.TenantAccessor is CurrentTenantAccessor currentAccessor)
        {
            var resolution = await currentAccessor.ResolveBindingStateAsync(cancellationToken).ConfigureAwait(false);
            return resolution.Status switch
            {
                CurrentTenantAccessor.TenantBindingStatus.Bound => (resolution.TenantId.ToString("D"), false),
                CurrentTenantAccessor.TenantBindingStatus.Invalid => (string.Empty, true),
                _ => (string.Empty, false),
            };
        }

        Guid? tenantId = await ext.TenantAccessor.GetCurrentTenantIdAsync(cancellationToken).ConfigureAwait(false);
        return tenantId is { } id && id != Guid.Empty
            ? (id.ToString("D"), false)
            : (string.Empty, false);
    }

    private static void ExecuteSetTenant(DbConnection connection, DbTransaction? transaction, string tenant) =>
        PostgresTenantSession.ExecuteSetTenant(connection, tenant, transaction);

    private static ValueTask ExecuteSetTenantAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string tenant,
        CancellationToken cancellationToken) =>
        PostgresTenantSession.ExecuteSetTenantAsync(connection, tenant, transaction, cancellationToken);

    private static void ResetAndCloseOnInvalidBinding(DbConnection connection)
    {
        BoundStates.Remove(connection);
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                ExecuteSetTenant(connection, transaction: null, string.Empty);
                connection.Close();
            }
        }
        catch
        {
            EvictDirtyPoolConnection(connection);
        }
    }

    private static async ValueTask ResetAndCloseOnInvalidBindingAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        BoundStates.Remove(connection);
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                await ExecuteSetTenantAsync(connection, transaction: null, string.Empty, cancellationToken).ConfigureAwait(false);
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            EvictDirtyPoolConnection(connection);
        }
    }

    private static void ReleaseBrokenLeaseIfClosed(DbConnection connection, DbContext? context)
    {
        if (connection.State != ConnectionState.Open)
        {
            BoundStates.Remove(connection);
            var ext = context?.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
            ext?.ResolveLeaseManager()?.ReleaseBrokenLease(context);
        }
    }

    private static void EvictDirtyPoolConnection(DbConnection connection)
    {
        if (connection is NpgsqlConnection npgsqlConnection)
        {
            try
            {
                NpgsqlConnection.ClearPool(npgsqlConnection);
            }
            catch (Exception)
            {
                // Best-effort pool eviction when session reset fails; connection may be unpooled or already disposed.
            }
        }
    }
}
