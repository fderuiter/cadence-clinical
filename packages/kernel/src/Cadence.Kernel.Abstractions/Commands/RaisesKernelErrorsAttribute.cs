using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Declares registered errors a command's handler can return, named by the registry class and its fields, for example
/// <c>[RaisesKernelErrors(typeof(ObservationErrors), nameof(ObservationErrors.Locked))]</c> (ADR-2600 §4.5). Repeat it
/// for errors from several registries. The errors the pipeline itself returns (<see cref="KernelErrors.CommandPipeline"/>)
/// are implied. Endpoints declare the same set in OpenAPI, and the test sender refuses an undeclared error.
/// </summary>
/// <param name="registry">The static class holding the errors as <c>public static readonly KernelError</c> fields.</param>
/// <param name="fields">The names of those fields.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RaisesKernelErrorsAttribute(Type registry, params string[] fields) : Attribute
{
    /// <summary>The static class holding the errors.</summary>
    public Type Registry { get; } = registry;

    /// <summary>The names of the error fields on <see cref="Registry"/>.</summary>
    public IReadOnlyList<string> Fields { get; } = fields;

    /// <summary>
    /// The errors <paramref name="commandType"/> declares, or null when it carries no <see cref="RaisesKernelErrorsAttribute"/>.
    /// Throws when a declaration names a field that is not a public static <see cref="KernelError"/>.
    /// </summary>
    public static IReadOnlyList<KernelError>? Of(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        var declarations = commandType.GetCustomAttributes<RaisesKernelErrorsAttribute>().ToList();
        if (declarations.Count == 0)
        {
            return null;
        }

        return [.. declarations.SelectMany(d => d.Fields.Select(f => Resolve(commandType, d.Registry, f)))];
    }

    private static KernelError Resolve(Type commandType, Type registry, string field) =>
        registry.GetField(field, BindingFlags.Public | BindingFlags.Static) is { } info && info.FieldType == typeof(KernelError)
            ? (KernelError)info.GetValue(null)!
            : throw new InvalidOperationException(
                $"{commandType.Name} declares {registry.Name}.{field}, which is not a public static KernelError field.");
}
