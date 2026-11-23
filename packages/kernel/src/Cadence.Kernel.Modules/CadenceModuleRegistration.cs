using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Modules;

/// <summary>
/// One module in the composition host (ADR-2601): its name, the assembly that declares its commands, whose
/// <c>[KernelCapability]</c> declarations the kernel authorizes module commands against, and the named OpenAPI document
/// served at <c>/openapi/{EffectiveOpenApiDocument}.json</c>.
/// </summary>
/// <param name="Name">Lower snake case, unique per host; the same name the module's command store uses.</param>
/// <param name="CommandAssembly">The assembly declaring the module's commands.</param>
/// <param name="OpenApiDocument">Lower snake case OpenAPI contract name; defaults to <paramref name="Name"/> when omitted.</param>
public sealed record CadenceModuleRegistration(string Name, Assembly CommandAssembly, string? OpenApiDocument = null)
{
    /// <summary>Gets the resolved OpenAPI document name (<see cref="OpenApiDocument"/> when set, otherwise <see cref="Name"/>).</summary>
    public string EffectiveOpenApiDocument => OpenApiDocument ?? Name;
}

/// <summary>Registers modules with the composition host (ADR-2601).</summary>
public static partial class CadenceModuleServiceCollectionExtensions
{
    /// <summary>
    /// Declares a module to the host. A module's <c>AddCadence&lt;Name&gt;Module</c> calls this once, so the kernel's
    /// authorization knows the capabilities its commands declare and ASP.NET Core registers an isolated
    /// <c>/openapi/{document}.json</c> contract for routes grouped under that document name; a module command without a
    /// declared capability is denied (fail closed).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The module's name, lower snake case.</param>
    /// <param name="commandAssembly">The assembly declaring the module's commands.</param>
    /// <param name="openApiDocument">
    /// Optional lower snake case OpenAPI contract name when the module's HTTP contract name differs from <paramref name="name"/>
    /// (for example <c>execution_forms</c> while standalone <c>execution</c> still exists); defaults to <paramref name="name"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">The name, OpenAPI document, or command assembly is already registered.</exception>
    public static IServiceCollection AddCadenceModule(
        this IServiceCollection services,
        string name,
        Assembly commandAssembly,
        string? openApiDocument = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commandAssembly);
        if (name is null || !ModuleName().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a module name: lower snake case, starting with a letter.", nameof(name));
        }

        string documentName = openApiDocument ?? name;
        if (!ModuleName().IsMatch(documentName) || documentName is "kernel" or "v1" or "cdisc")
        {
            throw new ArgumentException(
                $"'{documentName}' is not a valid module OpenAPI document name: lower snake case, distinct from reserved documents.",
                nameof(openApiDocument));
        }

        if (commandAssembly == typeof(Cadence.Kernel.Commands.ICommand).Assembly)
        {
            throw new ArgumentException("The kernel's own commands are always registered; a module names its own assembly.", nameof(commandAssembly));
        }

        if (services.Any(d => d.ImplementationInstance is CadenceModuleRegistration m &&
                              (m.Name == name || m.CommandAssembly == commandAssembly || m.EffectiveOpenApiDocument == documentName)))
        {
            throw new InvalidOperationException($"Module '{name}', OpenAPI document '{documentName}', or its command assembly is already registered.");
        }

        services.AddSingleton(new CadenceModuleRegistration(name, commandAssembly, documentName));
        services.AddOpenApi(documentName, options =>
        {
            options.ShouldInclude = description => string.Equals(description.GroupName, documentName, StringComparison.Ordinal);
        });
        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleName();
}
