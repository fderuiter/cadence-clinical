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
    /// Atomically records the first use of every token in <paramref name="uses"/>. Returns <see langword="true"/> only
    /// when every token id in the batch is distinct and none has been recorded before; if any token id is invalid,
    /// duplicated within the batch, or already recorded, returns <see langword="false"/> without recording any token in
    /// the batch (so a dual-signer failure never burns the innocent co-signer's step-up token).
    /// </summary>
    Task<bool> TryRecordFirstUseBatchAsync(
        IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> uses,
        CancellationToken cancellationToken);
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
    private readonly object _lock = new();
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    /// <inheritdoc />
    public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        return Task.FromResult(TryRecordFirstUse(tokenId, retainUntil));
    }

    /// <inheritdoc />
    public Task<bool> TryRecordFirstUseBatchAsync(
        IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> uses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uses);
        return Task.FromResult(TryRecordFirstUseBatch(uses));
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

    /// <summary>The synchronous form of <see cref="TryRecordFirstUseBatchAsync"/>.</summary>
    public bool TryRecordFirstUseBatch(IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);
        if (uses.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (tokenId, _) in uses)
        {
            if (string.IsNullOrWhiteSpace(tokenId) || !seen.Add(tokenId))
            {
                return false;
            }
        }

        var now = _clock.UtcNow;
        lock (_lock)
        {
            foreach (string expired in _used.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList())
            {
                _used.Remove(expired);
            }

            foreach (var (tokenId, _) in uses)
            {
                if (_used.ContainsKey(tokenId))
                {
                    return false;
                }
            }

            foreach (var (tokenId, retainUntil) in uses)
            {
                _used.Add(tokenId, retainUntil);
            }

            return true;
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
