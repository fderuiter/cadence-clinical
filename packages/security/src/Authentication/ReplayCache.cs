using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Time;

namespace Cadence.Security.Authentication;

/// <summary>
/// Thread-safe cache to prevent 21 CFR Part 11 signature token replay attacks.
/// @req:PRD-SYS-053
/// </summary>
public class ReplayCache(IClock? clock = null)
{
    private readonly ConcurrentDictionary<string, double> _consumed = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    /// <summary>
    /// Atomically verifies and consumes a token.
    /// Returns true if first time consumed, false if already consumed (replay attempt).
    /// </summary>
    public bool Consume(string token, string? jti, double exp)
    {
        string key = !string.IsNullOrWhiteSpace(jti) ? jti : token;
        double now = _clock.UtcNow.ToUnixTimeSeconds();

        lock (_lock)
        {
            // Prune expired entries
            var expiredKeys = _consumed.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList();
            foreach (var expKey in expiredKeys)
            {
                _consumed.TryRemove(expKey, out _);
            }

            if (_consumed.ContainsKey(key))
            {
                return false;
            }

            _consumed[key] = exp;
            return true;
        }
    }

    /// <summary>
    /// Resets the consumed token cache.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _consumed.Clear();
        }
    }

    public void Clear() => Reset();
}
