using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;

namespace Cadence.Modules.Intelligence.Infrastructure.Tasks;

/// <summary>
/// The task configurations agents registered with <c>AddAgentTask</c>, one version per key. Versioned files with
/// qualification records replace it with #5385 (ADR-2631); until then a registered configuration is not qualified.
/// </summary>
internal sealed class RegisteredTaskConfigurations : ITaskConfigurationCatalog
{
    private readonly FrozenDictionary<string, TaskConfiguration> _byKey;

    public RegisteredTaskConfigurations(IEnumerable<TaskConfiguration> configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var list = configurations.ToList();
        var problems = list.SelectMany(c => c.Problems().Select(p => $"{c.Key} v{c.Version}: {p}"))
            .Concat(list.GroupBy(c => c.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"{g.Key} is registered more than once."))
            .ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid agent task configurations: " + string.Join(" ", problems));
        }

        _byKey = list.ToFrozenDictionary(c => c.Key, StringComparer.Ordinal);
        All = [.. list.OrderBy(c => c.Key, StringComparer.Ordinal)];
    }

    public IReadOnlyList<TaskConfiguration> All { get; }

    public TaskConfiguration? Find(string taskKey) => taskKey is null ? null : _byKey.GetValueOrDefault(taskKey);
}
