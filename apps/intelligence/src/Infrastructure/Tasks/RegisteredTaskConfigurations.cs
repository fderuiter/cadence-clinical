using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;

namespace Cadence.Modules.Intelligence.Infrastructure.Tasks;

/// <summary>
/// The task configuration versions agents registered with <c>AddAgentTask</c> (ADR-2631): any number of versions per
/// key, each registered once. A version is unusable until a tenant qualifies it.
/// </summary>
internal sealed class RegisteredTaskConfigurations : ITaskConfigurationCatalog
{
    private readonly FrozenDictionary<string, TaskConfiguration[]> _byKey;

    public RegisteredTaskConfigurations(IEnumerable<TaskConfiguration> configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var list = configurations.ToList();
        var problems = list.SelectMany(c => c.Problems().Select(p => $"{c.Key} v{c.Version}: {p}"))
            .Concat(list.GroupBy(c => (c.Key, c.Version)).Where(g => g.Count() > 1).Select(g => $"{g.Key.Key} v{g.Key.Version} is registered more than once."))
            .ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid agent task configurations: " + string.Join(" ", problems));
        }

        _byKey = list.GroupBy(c => c.Key, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(c => c.Version).ToArray(), StringComparer.Ordinal);
        All = [.. list.OrderBy(c => c.Key, StringComparer.Ordinal).ThenBy(c => c.Version)];
    }

    public IReadOnlyList<TaskConfiguration> All { get; }

    public IReadOnlyList<TaskConfiguration> Versions(string taskKey) =>
        taskKey is not null && _byKey.TryGetValue(taskKey, out var versions) ? versions : [];

    public TaskConfiguration? Find(string taskKey, int version) => Versions(taskKey).FirstOrDefault(c => c.Version == version);
}
