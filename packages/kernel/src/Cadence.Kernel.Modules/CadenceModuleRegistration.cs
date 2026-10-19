using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Modules;

/// <summary>
/// One module in the composition host (ADR-2601): its name and the assembly that declares its commands, whose
/// <c>[KernelCapability]</c> declarations the kernel authorizes module commands against.
/// </summary>
/// <param name="Name">Lower snake case, unique per host; the same name the module's command store uses.</param>
/// <param name="CommandAssembly">The assembly declaring the module's commands.</param>
public sealed record CadenceModuleRegistration(string Name, Assembly CommandAssembly);

/// <summary>Registers modules with the composition host (ADR-2601).</summary>
public static partial class CadenceModuleServiceCollectionExtensions
{
    /// <summary>
    /// Declares a module to the host. A module's <c>AddCadence&lt;Name&gt;Module</c> calls this once, so the kernel's
    /// authorization knows the capabilities its commands declare; a module command without one is denied (fail closed).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The module's name, lower snake case.</param>
    /// <param name="commandAssembly">The assembly declaring the module's commands.</param>
    /// <exception cref="InvalidOperationException">The name or the assembly is already registered.</exception>
    public static IServiceCollection AddCadenceModule(this IServiceCollection services, string name, Assembly commandAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commandAssembly);
        if (name is null || !ModuleName().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a module name: lower snake case, starting with a letter.", nameof(name));
        }

        if (commandAssembly == typeof(Cadence.Kernel.Commands.ICommand).Assembly)
        {
            throw new ArgumentException("The kernel's own commands are always registered; a module names its own assembly.", nameof(commandAssembly));
        }

        if (services.Any(d => d.ImplementationInstance is CadenceModuleRegistration m && (m.Name == name || m.CommandAssembly == commandAssembly)))
        {
            throw new InvalidOperationException($"Module '{name}' or its command assembly is already registered.");
        }

        services.AddSingleton(new CadenceModuleRegistration(name, commandAssembly));
        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleName();
}
