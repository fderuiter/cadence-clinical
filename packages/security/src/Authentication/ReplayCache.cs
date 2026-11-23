using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;

namespace Cadence.Security.Authentication;

/// <summary>
/// Records the first use of each e-signature step-up token so that a token is accepted at most once (21 CFR
/// 11.200(a)(1)). The record must outlive the token's whole acceptance window: until
/// <c>exp + </c><see cref="SigTokenVerifier.ClockSkew"/>, after which the verifier refuses the token anyway.
/// <para>
/// Single use only holds as far as the store is shared. <see cref="ReplayCache"/> is process memory, so it forgets
/// every use on restart and a second instance never sees the first one's uses. A host that runs more than one
/// instance, or can restart within a token's window, registers a durable store instead (ADR-2515).
/// </para>
/// </summary>
public interface ISigTokenUseStore
{
    /// <summary>
    /// Records that <paramref name="tokenId"/> (the token's <c>jti</c>) was used, keeping the record at least until
    /// <paramref name="retainUntil"/>. Returns true for the first use and false for every later one, including
    /// concurrent attempts. Implementations must make check-and-record one atomic step.
    /// </summary>
    Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically records first use for every <c>(tokenId, retainUntil)</c> entry in <paramref name="tokens"/>.
    /// Returns true only when every token is distinct and unused; if any token was already consumed, none of the
    /// entries are recorded so a failed secondary check never burns a fresh primary token.
    /// </summary>
    async Task<bool> TryRecordFirstUseBatchAsync(
        IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> tokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Select(t => t.TokenId).Distinct(StringComparer.Ordinal).Count() != tokens.Count)
        {
            return false;
        }

        foreach (var (tokenId, retainUntil) in tokens)
        {
            if (!await TryRecordFirstUseAsync(tokenId, retainUntil, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// An <see cref="ISigTokenUseStore"/> whose records are shared by every instance of the host and survive a restart.
/// Outside <c>Development</c>, a host that maps an e-signature route must register one, or it refuses to start
/// (<see cref="SigTokenUseStoreStartupCheck"/>, #5434).
/// </summary>
public interface IDurableSigTokenUseStore : ISigTokenUseStore;

/// <summary>
/// The process-local <see cref="ISigTokenUseStore"/>: correct for one instance that does not restart within a
/// token's acceptance window, and the default until a host registers a durable store (ADR-2515).
/// @req:SRS-SYS-005
/// </summary>
public class ReplayCache(IClock? clock = null) : ISigTokenUseStore
{
    private readonly Dictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    /// <inheritdoc />
    public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        return Task.FromResult(TryRecordFirstUse(tokenId, retainUntil));
    }

    /// <inheritdoc />
    public Task<bool> TryRecordFirstUseBatchAsync(
        IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> tokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return Task.FromResult(false);
        }

        foreach (var (tokenId, _) in tokens)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        }

        var now = _clock.UtcNow;
        lock (_lock)
        {
            foreach (string expired in _used.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList())
            {
                _used.Remove(expired);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (tokenId, _) in tokens)
            {
                if (!seen.Add(tokenId) || _used.ContainsKey(tokenId))
                {
                    return Task.FromResult(false);
                }
            }

            foreach (var (tokenId, retainUntil) in tokens)
            {
                _used.Add(tokenId, retainUntil);
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>The synchronous form of <see cref="TryRecordFirstUseAsync"/>.</summary>
    public bool TryRecordFirstUse(string tokenId, DateTimeOffset retainUntil)
    {
        var now = _clock.UtcNow;
        lock (_lock)
        {
            // Forget only records whose retention has passed; the verifier refuses those tokens by then.
            foreach (string expired in _used.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList())
            {
                _used.Remove(expired);
            }

            return _used.TryAdd(tokenId, retainUntil);
        }
    }

    /// <summary>Forgets every recorded use.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _used.Clear();
        }
    }

    /// <summary>Forgets every recorded use.</summary>
    public void Clear() => Reset();
}
