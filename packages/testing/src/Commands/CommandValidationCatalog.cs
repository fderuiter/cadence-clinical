using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Cadence.Kernel.Commands;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Testing.Commands;

/// <summary>
/// The catalog check behind ADR-2600 §4.4: every command a kernel or module assembly declares has a FluentValidation
/// validator registered in the services that run it, or carries <see cref="NoValidatorAttribute"/> with a reason, and
/// never both. The kernel's tests and each module's tests call it with their own registrations.
/// </summary>
public static class CommandValidationCatalog
{
    /// <summary>One line per command that breaks the rule, empty when every command complies.</summary>
    /// <param name="services">The registrations that run the commands (for example after <c>AddKernelCommandPipeline()</c>).</param>
    /// <param name="commandAssemblies">The assemblies that declare the commands.</param>
    public static IReadOnlyList<string> Violations(IServiceCollection services, params Assembly[] commandAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commandAssemblies);
        var validated = services
            .Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(d => d.ServiceType.GetGenericArguments()[0])
            .ToHashSet();

        return [.. Commands(commandAssemblies)
            .Select(command => (Command: command, Marked: command.GetCustomAttribute<NoValidatorAttribute>() is not null, Validated: validated.Contains(command)))
            .Where(c => c.Marked == c.Validated)
            .Select(c => c.Marked
                ? $"{c.Command.FullName} has a validator and [NoValidator]; remove the marker."
                : $"{c.Command.FullName} has neither a registered IValidator<{c.Command.Name}> nor [NoValidator(\"why\")].")
            .OrderBy(line => line, StringComparer.Ordinal)];
    }

    private static IEnumerable<Type> Commands(IEnumerable<Assembly> assemblies) =>
        assemblies.Distinct().SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICommand).IsAssignableFrom(t));
}
