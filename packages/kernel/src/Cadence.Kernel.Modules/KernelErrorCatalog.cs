using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Modules;

/// <summary>
/// The closed set of registered errors (K-ERR-1): every public static <see cref="KernelError"/> field in the given
/// assemblies, which are the kernel's and the registered modules' (<see cref="CadenceModuleRegistration"/>). A code is
/// <c>K.&lt;Area&gt;.&lt;Name&gt;</c> in the kernel and starts <c>M.&lt;Module&gt;.</c> in a module (ADR-2600 §4.5);
/// it is unique and never reused with another status.
/// </summary>
public sealed partial class KernelErrorCatalog
{
    private readonly Dictionary<string, KernelError> _byCode;

    private KernelErrorCatalog(IReadOnlyList<KernelError> errors, IReadOnlyList<string> violations)
    {
        Errors = errors;
        Violations = violations;
        _byCode = errors.GroupBy(e => e.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>Every registered error, ordered by code.</summary>
    public IReadOnlyList<KernelError> Errors { get; }

    /// <summary>Malformed or clashing registrations; empty when the catalog is sound.</summary>
    public IReadOnlyList<string> Violations { get; }

    /// <summary>Collects the registered errors of <paramref name="assemblies"/>.</summary>
    public static KernelErrorCatalog From(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return FromTypes(assemblies.Distinct().SelectMany(a => a.GetTypes()));
    }

    /// <summary>Collects the registered errors declared on <paramref name="types"/>.</summary>
    internal static KernelErrorCatalog FromTypes(IEnumerable<Type> types)
    {
        var fields = types
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (Type: t, Field: f)))
            .Where(x => x.Field.FieldType == typeof(KernelError))
            .Select(x => (Name: $"{x.Type.FullName}.{x.Field.Name}", Error: (KernelError)x.Field.GetValue(null)!))
            .ToList();

        var violations = new List<string>();
        foreach (var (name, error) in fields)
        {
            if (!CodeFormat().IsMatch(error.Code))
            {
                violations.Add($"{name}: '{error.Code}' is not <Owner>.<Area>.<Name> or <Owner>.<Module>.<Area>.<Name>.");
            }

            if (error.HttpStatus is < 400 or > 599)
            {
                violations.Add($"{name}: {error.Code} maps to {error.HttpStatus}, not a 4xx or 5xx status.");
            }
        }

        foreach (var clash in fields.GroupBy(f => f.Error.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            violations.Add($"{clash.Key} is registered more than once: {string.Join(", ", clash.Select(c => c.Name))}.");
        }

        var errors = fields.Select(f => f.Error).DistinctBy(e => e.Code).OrderBy(e => e.Code, StringComparer.Ordinal).ToArray();
        return new KernelErrorCatalog(errors, violations);
    }

    /// <summary>Whether <paramref name="error"/> is registered with this code and status.</summary>
    public bool Contains(KernelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return _byCode.TryGetValue(error.Code, out var registered) && registered.HttpStatus == error.HttpStatus;
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*(\\.[A-Z][A-Za-z0-9]*){2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeFormat();
}
