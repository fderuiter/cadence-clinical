using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Host configuration and persistence context passed to each <see cref="ICadenceHostModule"/> when composing the
/// kernel host (ADR-2601).
/// </summary>
/// <param name="Configuration">The host's configuration.</param>
/// <param name="Environment">The host's environment.</param>
/// <param name="ConnectionString">The PostgreSQL connection string used by the host's kernel persistence.</param>
public sealed record CadenceHostModuleContext(
    IConfiguration Configuration,
    IHostEnvironment Environment,
    string ConnectionString);

/// <summary>
/// Descriptor through which a composition-host module registers its services and maps its HTTP endpoints without
/// bespoke per-module calls in <c>apps/kernel-api/src/Program.cs</c> (ADR-2601).
/// </summary>
public interface ICadenceHostModule
{
    /// <summary>Gets the module's unique lower snake case identifier.</summary>
    string Name { get; }

    /// <summary>Registers the module's services, command handlers, migrations, and OpenAPI document with the host.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="context">The host's configuration, environment, and database connection string.</param>
    void RegisterServices(IServiceCollection services, CadenceHostModuleContext context);

    /// <summary>Maps the module's HTTP endpoints onto the host's endpoint route builder.</summary>
    /// <param name="endpoints">The host's endpoint route builder.</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

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
    /// Registers one or more <see cref="ICadenceHostModule"/> descriptors with the composition host so their services
    /// are wired in order and their endpoints can later be mapped together via <see cref="MapCadenceHostModules"/>.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="context">The host's configuration, environment, and database connection string.</param>
    /// <param name="modules">The host modules to register in order.</param>
    /// <returns>The <paramref name="services"/> collection for chaining.</returns>
    public static IServiceCollection AddCadenceHostModules(
        this IServiceCollection services,
        CadenceHostModuleContext context,
        params ICadenceHostModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (ICadenceHostModule module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            if (services.Any(d => d.ImplementationInstance is ICadenceHostModule existing &&
                                  string.Equals(existing.Name, module.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Host module '{module.Name}' is already registered.");
            }

            services.AddSingleton<ICadenceHostModule>(module);
            module.RegisterServices(services, context);
        }

        return services;
    }

    /// <summary>
    /// Maps HTTP endpoints for every <see cref="ICadenceHostModule"/> registered via <see cref="AddCadenceHostModules"/>
    /// in registration order.
    /// </summary>
    /// <param name="endpoints">The host's endpoint route builder.</param>
    /// <returns>The <paramref name="endpoints"/> builder for chaining.</returns>
    public static IEndpointRouteBuilder MapCadenceHostModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (ICadenceHostModule module in endpoints.ServiceProvider.GetServices<ICadenceHostModule>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
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
        if (services.Any(d => d.ServiceType == typeof(IHostEnvironment)))
        {
            services.AddRoutingCore();
            services.AddOpenApi(documentName, options =>
            {
                options.ShouldInclude = description => string.Equals(description.GroupName, documentName, StringComparison.Ordinal);
            });
        }

        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleName();
}
