using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// Every kernel command with the capability it needs and the kind of object it is authorized against, read once from
/// <see cref="KernelCapabilityAttribute"/> (ADR-2483). The pipeline uses it to turn a command into an
/// <see cref="AuthorizationRequest"/>, and the probe uses it to list the commands offered on an object.
/// </summary>
public sealed class KernelCommandCatalog
{
    private readonly FrozenDictionary<Type, Entry> _byType;

    public KernelCommandCatalog(IEnumerable<Assembly> commandAssemblies)
    {
        ArgumentNullException.ThrowIfNull(commandAssemblies);
        var commandTypes = commandAssemblies
            .Distinct()
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
    }

    /// <summary>The catalogue of the kernel's own commands (<c>Cadence.Kernel.Abstractions</c>).</summary>
    public static KernelCommandCatalog Kernel { get; } = new([typeof(ICommand).Assembly]);

    /// <summary>Every declared command, ordered by name.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>The declaration for <paramref name="commandType"/>, or null when it declares no capability.</summary>
    public Entry? Find(Type commandType) => _byType.GetValueOrDefault(commandType);

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

            if (command is Attest attest
                && NodeKinds.IsRegistered(attest.Target.Kind)
                && attest.Target.Id != Guid.Empty)
            {
                return new NodeRef(attest.Target.Kind, attest.Target.Id);
            }

            if (command is DraftLibraryItem draft
                && draft.GoverningProtocolVersionId is { IsEmpty: false } governingVersionId)
            {
                return NodeRef.For(governingVersionId);
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
