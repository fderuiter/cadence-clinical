using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace Cadence.Database.Signatures;

/// <summary>
/// The durable <see cref="ISigTokenUseStore"/>: each use is an insert into <typeparamref name="TContext"/>'s
/// <c>consumed_signature_tokens</c>, whose primary key on the token id lets exactly one insert succeed however many
/// instances race, and which survives a restart (ADR-2515). The insert commits immediately before the signed
/// write starts, so a write that later fails leaves the token used: the signer re-authenticates, and a token is never
/// reusable. When invoked inside an HTTP request whose scoped <typeparamref name="TContext"/> has no pending changes
/// or active transaction, it reuses that <typeparamref name="TContext"/> and its single connection lease, detaching
/// the inserted token entries afterwards. The context must map <see cref="ConsumedSignatureToken"/> with
/// <see cref="ConsumedSignatureTokenConfiguration"/>.
/// @req:SRS-SYS-005
/// </summary>
/// <typeparam name="TContext">The host's own context, whose schema holds the table.</typeparam>
public sealed class PostgresSigTokenUseStore<TContext>(
    IServiceScopeFactory scopes,
    IClock clock,
    IHttpContextAccessor? httpContextAccessor = null) : IDurableSigTokenUseStore
    where TContext : DbContext
{
    /// <inheritdoc />
    public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        return TryRecordFirstUseAsync([(tokenId, retainUntil)], cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> TryRecordFirstUseBatchAsync(
        IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> uses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uses);
        return TryRecordFirstUseAsync(uses, cancellationToken);
    }

    private async Task<bool> TryRecordFirstUseAsync(
        IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> uses,
        CancellationToken cancellationToken)
    {
        if (uses.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (tokenId, _) in uses)
        {
            if (string.IsNullOrWhiteSpace(tokenId)
                || tokenId.Length > ConsumedSignatureTokenConfiguration.MaxTokenIdLength
                || !seen.Add(tokenId))
            {
                return false;
            }
        }

        AsyncServiceScope? ownedScope = null;
        TContext? requestDb = TryResolveCleanRequestDbContext();
        bool detachAfterSave = requestDb is not null;
        if (requestDb is null)
        {
            ownedScope = scopes.CreateAsyncScope();
            requestDb = ownedScope.Value.ServiceProvider.GetRequiredService<TContext>();
        }

        try
        {
            var tokens = requestDb.Set<ConsumedSignatureToken>();
            var now = clock.UtcNow;

            bool isNpgsql = requestDb.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;
            if (isNpgsql)
            {
                // Rows past their window can never match an acceptable token again.
                await tokens.Where(t => t.RetainUntil < now).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var allTokens = await tokens.ToListAsync(cancellationToken).ConfigureAwait(false);
                var expired = allTokens.Where(t => t.RetainUntil < now).ToList();
                if (expired.Count > 0)
                {
                    tokens.RemoveRange(expired);
                }

                if (allTokens.Any(t => t.RetainUntil >= now && seen.Contains(t.TokenId)))
                {
                    if (detachAfterSave)
                    {
                        DetachEntities(requestDb, allTokens);
                    }

                    return false;
                }
            }

            var added = new List<ConsumedSignatureToken>(uses.Count);
            foreach (var (tokenId, retainUntil) in uses)
            {
                var entity = new ConsumedSignatureToken { TokenId = tokenId, ConsumedAt = now, RetainUntil = retainUntil };
                tokens.Add(entity);
                added.Add(entity);
            }

            try
            {
                await requestDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                || string.Equals(ex.InnerException?.GetType().Name, "SqliteException", StringComparison.Ordinal))
            {
                return false;
            }
            catch (ArgumentException) when (!requestDb.Database.IsRelational())
            {
                return false;
            }
            finally
            {
                if (detachAfterSave)
                {
                    DetachEntities(requestDb, added);
                }
            }
        }
        finally
        {
            if (ownedScope.HasValue)
            {
                await ownedScope.Value.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private TContext? TryResolveCleanRequestDbContext()
    {
        IServiceProvider? requestServices = httpContextAccessor?.HttpContext?.RequestServices;
        if (requestServices is null)
        {
            return null;
        }

        var candidate = requestServices.GetService<TContext>();
        if (candidate is null || candidate.ChangeTracker.HasChanges())
        {
            return null;
        }

        if (candidate.Database.IsRelational() && candidate.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return candidate;
    }

    private static void DetachEntities(TContext db, IEnumerable<ConsumedSignatureToken> entities)
    {
        foreach (var entity in entities)
        {
            db.Entry(entity).State = EntityState.Detached;
        }
    }
}

/// <summary>Registration of the durable e-signature single-use store.</summary>
public static class SigTokenUseStoreServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the process-local <see cref="ReplayCache"/> that <c>AddCadenceSecurity</c> registers with
    /// <see cref="PostgresSigTokenUseStore{TContext}"/>, so a signature token is single use across every instance and
    /// restart of this host (ADR-2515, #5434). Every host that maps a <c>RequireElectronicSignature</c> route calls it.
    /// </summary>
    public static IServiceCollection AddCadenceDurableSigTokenStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.RemoveAll<ISigTokenUseStore>();
        services.AddSingleton<ISigTokenUseStore, PostgresSigTokenUseStore<TContext>>();
        return services;
    }
}
