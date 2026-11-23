using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// Every kernel command with the capability it needs and the kind of object it is authorized against, read once from
/// <see cref="KernelCapabilityAttribute"/> (ADR-2483), alongside non-command read capabilities such as
/// <see cref="SimulationCapabilities.Run"/>. The pipeline uses it to turn a command into an
/// <see cref="AuthorizationRequest"/>, and the probe uses it to list the commands offered on an object and resolve
/// dry-run capabilities.
/// </summary>
public sealed class KernelCommandCatalog
{
    private readonly FrozenDictionary<Type, Entry> _byType;
    private readonly FrozenDictionary<string, Capability> _capabilitiesByToken;

    public KernelCommandCatalog(IEnumerable<Assembly> commandAssemblies)
    {
        ArgumentNullException.ThrowIfNull(commandAssemblies);
        var assemblies = commandAssemblies.Distinct().ToList();
        var commandTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICommand).IsAssignableFrom(t));
        var entries = new Dictionary<Type, Entry>();
        foreach (var type in commandTypes)
        {
            if (type.GetCustomAttribute<KernelCapabilityAttribute>() is { } declared)
            {
                entries[type] = Entry.From(type, declared);
            }
        }

        _byType = entries.ToFrozenDictionary();
        Entries = [.. _byType.Values.OrderBy(e => e.Command, StringComparer.Ordinal)];

        var capabilities = new Dictionary<string, Capability>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            if (capabilities.TryGetValue(entry.Capability.Token, out var existing) && existing != entry.Capability)
            {
                throw new InvalidOperationException(
                    $"Capability '{entry.Capability.Token}' is declared with conflicting verb classes ({existing.VerbClass} vs {entry.Capability.VerbClass}).");
            }

            capabilities[entry.Capability.Token] = entry.Capability;
        }

        if (assemblies.Contains(typeof(SimulationCapabilities).Assembly))
        {
            capabilities[SimulationCapabilities.Run.Token] = SimulationCapabilities.Run;
        }

        _capabilitiesByToken = capabilities.ToFrozenDictionary(StringComparer.Ordinal);
        Capabilities = [.. _capabilitiesByToken.Values.OrderBy(c => c.Token, StringComparer.Ordinal)];
    }

    /// <summary>The catalogue of the kernel's own commands (<c>Cadence.Kernel.Abstractions</c>).</summary>
    public static KernelCommandCatalog Kernel { get; } = new([typeof(ICommand).Assembly]);

    /// <summary>Every declared command, ordered by name.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Every declared kernel command or read capability, ordered by token.</summary>
    public IReadOnlyList<Capability> Capabilities { get; }

    /// <summary>The declaration for <paramref name="commandType"/>, or null when it declares no capability.</summary>
    public Entry? Find(Type commandType) => _byType.GetValueOrDefault(commandType);

    /// <summary>The capability declared for <paramref name="token"/>, or null when unknown.</summary>
    public Capability? FindCapability(string token) =>
        string.IsNullOrEmpty(token) ? null : _capabilitiesByToken.GetValueOrDefault(token);

    /// <summary>The commands offered on objects of <paramref name="kind"/>, ordered by name.</summary>
    public IReadOnlyList<Entry> ForKind(string kind) => [.. Entries.Where(e => e.InCatalogue && e.ResourceKind == kind)];

    /// <summary>A command's declared capability and resource.</summary>
    public sealed class Entry
    {
        private readonly PropertyInfo _resource;

        private Entry(Type type, Capability capability, PropertyInfo resource, string resourceKind, bool inCatalogue)
        {
            CommandType = type;
            Capability = capability;
            _resource = resource;
            ResourceKind = resourceKind;
            InCatalogue = inCatalogue;
        }

        public Type CommandType { get; }

        public string Command => CommandType.Name;

        public Capability Capability { get; }

        /// <summary>The NodeRef kind of the object the command is authorized against.</summary>
        public string ResourceKind { get; }

        public bool InCatalogue { get; }

        /// <summary>The object <paramref name="command"/> is authorized against, or null when the command leaves it empty.</summary>
        public NodeRef? ResourceOf(ICommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.GetType() != CommandType)
            {
                throw new ArgumentException($"Expected a {Command}, got a {command.GetType().Name}.", nameof(command));
            }

            return _resource.GetValue(command) is IEntityId { IsEmpty: false } id ? NodeRef.For(id) : null;
        }

        internal static Entry From(Type type, KernelCapabilityAttribute declared)
        {
            var capability = new Capability(declared.Token, declared.VerbClass);
            var property = type.GetProperty(declared.Resource, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{type.Name} declares resource '{declared.Resource}', which is not a public property.");
            if (!typeof(IEntityId).IsAssignableFrom(property.PropertyType) || !NodeKinds.IdentifierTypes.Contains(property.PropertyType))
            {
                throw new InvalidOperationException(
                    $"{type.Name}.{property.Name} is a {property.PropertyType.Name}; a command's resource must be a kernel identifier with a NodeRef kind.");
            }

            return new Entry(type, capability, property, NodeKinds.Of(property.PropertyType), declared.InCatalogue);
        }
    }
}
