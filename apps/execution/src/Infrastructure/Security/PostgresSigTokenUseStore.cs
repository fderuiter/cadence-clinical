using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// The durable <see cref="ISigTokenUseStore"/>: each use is an insert into <c>consumed_signature_tokens</c>, whose
/// primary key on the token id lets exactly one insert succeed however many instances race, and which survives a
/// restart (ADR-2515). The insert commits on its own connection before the signed write starts, so a write that
/// later fails leaves the token used: the signer re-authenticates, and a token is never reusable.
/// @req:SRS-SYS-005
/// </summary>
public sealed class PostgresSigTokenUseStore(IServiceScopeFactory scopes, IClock clock) : ISigTokenUseStore
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
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var now = clock.UtcNow;

        // Rows past their window can never match an acceptable token again.
        await db.ConsumedSignatureTokens.Where(t => t.RetainUntil < now).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        db.ConsumedSignatureTokens.Add(new ConsumedSignatureToken { TokenId = tokenId, ConsumedAt = now, RetainUntil = retainUntil });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }
}
