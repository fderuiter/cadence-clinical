using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Sentinels;

public class SentinelEngine
{
    private readonly List<ISentinelCheck> _checks = [];

    public void Register(ISentinelCheck check)
    {
        _checks.Add(check);
    }

    public async Task<IReadOnlyList<SentinelResult>> ExecuteAllAsync(string? targetGate = null, CancellationToken ct = default)
    {
        var selected = _checks.Where(c => string.IsNullOrEmpty(targetGate) || c.Name.Equals(targetGate, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0)
        {
            return [];
        }

        var tasks = selected.Select(c => c.RunAsync(ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }
}
