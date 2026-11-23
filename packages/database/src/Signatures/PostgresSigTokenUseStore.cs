using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace Cadence.Database.Signatures;

/// <summary>
/// The durable <see cref="ISigTokenUseStore"/>: each use is an insert into <typeparamref name="TContext"/>'s
/// <c>consumed_signature_tokens</c>, whose primary key on the token id lets exactly one insert succeed however many
/// instances race, and which survives a restart (ADR-2515). The insert commits on its own connection before the signed
/// write starts, so a write that later fails leaves the token used: the signer re-authenticates, and a token is never
/// reusable. The context must map <see cref="ConsumedSignatureToken"/> with
/// <see cref="ConsumedSignatureTokenConfiguration"/>.
/// @req:SRS-SYS-005
/// </summary>
/// <typeparam name="TContext">The host's own context, whose schema holds the table.</typeparam>
public sealed class PostgresSigTokenUseStore<TContext>(IServiceScopeFactory scopes, IClock clock) : IDurableSigTokenUseStore
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        if (tokenId.Length > ConsumedSignatureTokenConfiguration.MaxTokenIdLength)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var tokens = db.Set<ConsumedSignatureToken>();
        var now = clock.UtcNow;

        bool isNpgsql = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;
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

            if (allTokens.Any(t => t.RetainUntil >= now && string.Equals(t.TokenId, tokenId, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        tokens.Add(new ConsumedSignatureToken { TokenId = tokenId, ConsumedAt = now, RetainUntil = retainUntil });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            || string.Equals(ex.InnerException?.GetType().Name, "SqliteException", StringComparison.Ordinal))
        {
            return false;
        }
        catch (ArgumentException) when (!db.Database.IsRelational())
        {
            return false;
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
