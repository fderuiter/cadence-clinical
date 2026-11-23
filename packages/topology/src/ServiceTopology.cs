using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cadence.Topology;

/// <summary>
/// A module hosted inside a composition host service (such as <c>kernel</c>) whose source tree lives under <c>apps/</c>
/// while its HTTP endpoints and OpenAPI contract are served by the host (ADR-2601, ADR-2610).
/// </summary>
/// <param name="Name">Lower snake case module identifier matching <c>CadenceModuleRegistration.Name</c> (for example <c>intelligence</c> or <c>execution</c>).</param>
/// <param name="ContractName">Lower snake case OpenAPI contract name (for example <c>intelligence</c> or <c>execution_forms</c>).</param>
/// <param name="SourceDirectory">Repository-relative directory containing the module's <c>.csproj</c> (for example <c>apps/intelligence/src</c>).</param>
/// <param name="PathPrefix">Repository-relative path prefix whose edits map to <paramref name="ContractName"/> (for example <c>apps/intelligence</c>).</param>
/// <param name="AssemblyName">The assembly and <c>.csproj</c> base name of the module project (for example <c>Cadence.Modules.Intelligence</c>).</param>
public sealed record CadenceHostedModule(
    string Name,
    string ContractName,
    string SourceDirectory,
    string PathPrefix,
    string AssemblyName)
{
    /// <summary>The OpenAPI component schema prefix for <see cref="ContractName"/> (for example <c>ExecutionForms_</c>).</summary>
    public string SchemaPrefix => ServiceTopology.ToSchemaPrefix(ContractName);

    /// <summary>The repository-relative path to the module's <c>.csproj</c> file.</summary>
    public string CsprojRelativePath => $"{SourceDirectory}/{AssemblyName}.csproj";
}

/// <summary>
/// One service the gateway routes to: what it is called, where it listens, how the gateway finds it and which API
/// prefixes it serves, along with its OpenAPI schema prefix, entry-point <c>Program</c> type and hosted module paths.
/// </summary>
/// <param name="Name">The service's name: its AppHost resource, docker compose service and gateway cluster prefix.</param>
/// <param name="Directory">Its directory under <c>apps/</c>.</param>
/// <param name="Port">The host port docker compose publishes it on and the AppHost runs it on.</param>
/// <param name="UrlVariable">The variable that tells the gateway the service's base URL.</param>
/// <param name="RoutePrefixes">
/// The <c>/api/v1/&lt;prefix&gt;</c> paths the gateway forwards to it. A prefix of two segments, such as
/// <c>execution/forms</c>, takes precedence over another service's one-segment prefix it lies under: a module moved
/// into the kernel host keeps its routes (ADR-2601 §4.4).
/// </param>
/// <param name="Database">Whether it needs the PostgreSQL connection and the audit-log key.</param>
/// <param name="Core">Whether the AppHost's <c>core</c> profile runs it.</param>
/// <param name="SchemaPrefix">The OpenAPI component schema prefix (for example <c>Ctms_</c>).</param>
/// <param name="ProgramTypeName">The full CLR type name of the service's <c>Program</c> entry point.</param>
/// <param name="HostedModules">Optional modules hosted inside this service whose source trees live under <c>apps/</c>.</param>
public sealed record CadenceService(
    string Name,
    string Directory,
    int Port,
    string UrlVariable,
    IReadOnlyList<string> RoutePrefixes,
    bool Database,
    bool Core,
    string SchemaPrefix,
    string ProgramTypeName,
    IReadOnlyList<CadenceHostedModule>? HostedModules = null)
{
    /// <summary>The gateway's cluster for this service.</summary>
    public string ClusterId => $"{Name}-cluster";

    /// <summary>The address the gateway uses in Development when <see cref="UrlVariable"/> is not set.</summary>
    public Uri LocalUrl => new UriBuilder(Uri.UriSchemeHttp, "localhost", Port).Uri;

    /// <summary>The assembly and <c>.csproj</c> base name of the service's host project.</summary>
    public string AssemblyName => ProgramTypeName[..ProgramTypeName.LastIndexOf('.')];

    /// <summary>The repository-relative source directory containing the service's host project.</summary>
    public string SourceDirectory => $"apps/{Directory}/src";

    /// <summary>The repository-relative path to the service's <c>.csproj</c> file.</summary>
    public string CsprojRelativePath => $"{SourceDirectory}/{AssemblyName}.csproj";

    /// <summary>Modules hosted in this service (empty for standalone single-service hosts).</summary>
    public IReadOnlyList<CadenceHostedModule> Modules => HostedModules ?? [];
}

/// <summary>
/// The service topology manifest (#5315): the one list of services, ports, address variables and gateway routes. The
/// gateway builds its routes and clusters from it, the AppHost runs and wires the services from it, and the developer
/// CLI checks its ports from it. docker compose and the docs that describe the topology can't read C#, so
/// <c>ServiceTopologyTests</c> fails when they disagree with it.
/// </summary>
public static class ServiceTopology
{
    /// <summary>The gateway's port: the one address the web apps call.</summary>
    public const int GatewayPort = 8000;

    /// <summary>Every service behind the gateway, in port order.</summary>
    public static IReadOnlyList<CadenceService> Services { get; } =
    [
        new("designer", "designer", 8001, "DESIGNER_SERVICE_URL", ["studies", "protocols"], Database: false, Core: false, SchemaPrefix: "Designer_", ProgramTypeName: "Cadence.Apps.Designer.Program"),
        new("execution", "execution", 8002, "EXECUTION_SERVICE_URL", ["execution"], Database: true, Core: true, SchemaPrefix: "Execution_", ProgramTypeName: "Cadence.Apps.Execution.Program"),
        new("etmf", "etmf", 8003, "ETMF_SERVICE_URL", ["etmf"], Database: true, Core: true, SchemaPrefix: "Etmf_", ProgramTypeName: "Cadence.Apps.Etmf.Program"),
        new("interop", "interop", 8004, "INTEROP_SERVICE_URL", ["interop"], Database: true, Core: false, SchemaPrefix: "Interop_", ProgramTypeName: "Cadence.Apps.Interop.Program"),
        new("quality", "quality", 8005, "QUALITY_SERVICE_URL", ["quality"], Database: true, Core: false, SchemaPrefix: "Quality_", ProgramTypeName: "Cadence.Apps.Quality.Program"),
        new("notifications", "notifications", 8006, "NOTIFICATIONS_SERVICE_URL", ["notifications"], Database: true, Core: false, SchemaPrefix: "Notifications_", ProgramTypeName: "Cadence.Apps.Notifications.Program"),
        new("ctms", "ctms", 8007, "CTMS_SERVICE_URL", ["ctms"], Database: true, Core: true, SchemaPrefix: "Ctms_", ProgramTypeName: "Cadence.Apps.Ctms.Program"),
        new("safety", "safety", 8008, "SAFETY_SERVICE_URL", ["safety"], Database: true, Core: false, SchemaPrefix: "Safety_", ProgramTypeName: "Cadence.Apps.Safety.Program"),
        new("tickets", "tickets", 8009, "TICKETS_SERVICE_URL", ["tickets"], Database: true, Core: false, SchemaPrefix: "Tickets_", ProgramTypeName: "Cadence.Apps.Tickets.Program"),
        new("eisf", "eisf", 8010, "EISF_SERVICE_URL", ["eisf"], Database: true, Core: false, SchemaPrefix: "Eisf_", ProgramTypeName: "Cadence.Apps.Eisf.Program"),
        new("econsent", "econsent", 8011, "ECONSENT_SERVICE_URL", ["econsent"], Database: true, Core: false, SchemaPrefix: "Econsent_", ProgramTypeName: "Cadence.Apps.Econsent.Program"),
        new("org", "org", 8012, "ORG_SERVICE_URL", ["org"], Database: true, Core: false, SchemaPrefix: "Org_", ProgramTypeName: "Cadence.Apps.Org.Program"),
        new("fileshare", "fileshare", 8013, "FILESHARE_SERVICE_URL", ["fileshare"], Database: true, Core: false, SchemaPrefix: "Fileshare_", ProgramTypeName: "Cadence.Apps.Fileshare.Program"),
        new("imaging", "imaging", 8014, "IMAGING_SERVICE_URL", ["imaging"], Database: true, Core: false, SchemaPrefix: "Imaging_", ProgramTypeName: "Cadence.Apps.Imaging.Program"),
        new("knowledge", "knowledge", 8015, "KNOWLEDGE_SERVICE_URL", ["knowledge", "training"], Database: true, Core: true, SchemaPrefix: "Knowledge_", ProgramTypeName: "Cadence.Apps.Knowledge.Program"),
        new("payments", "payments", 8016, "PAYMENTS_SERVICE_URL", ["payments"], Database: true, Core: false, SchemaPrefix: "Payments_", ProgramTypeName: "Cadence.Apps.Payments.Program"),
        new(
            "kernel",
            "kernel-api",
            8018,
            "KERNEL_SERVICE_URL",
            ["kernel", "ai", "execution/forms"],
            Database: true,
            Core: true,
            SchemaPrefix: "Kernel_",
            ProgramTypeName: "Cadence.Kernel.Host.Program",
            HostedModules:
            [
                new("intelligence", "intelligence", "apps/intelligence/src", "apps/intelligence", "Cadence.Modules.Intelligence"),
                new("execution", "execution_forms", "apps/execution/module", "apps/execution/module", "Cadence.Modules.Execution"),
            ]),
    ];

    /// <summary>Every hosted module across <see cref="Services"/>.</summary>
    public static IReadOnlyList<CadenceHostedModule> Modules { get; } =
        Services.SelectMany(s => s.Modules).ToList();

    /// <summary>The service called <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">No service has that name.</exception>
    public static CadenceService Get(string name) =>
        Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentException($"No service is called '{name}'.", nameof(name));

    /// <summary>
    /// Converts a lower snake case OpenAPI contract name (for example <c>execution_forms</c>) into its PascalCase
    /// component schema prefix with a trailing underscore (<c>ExecutionForms_</c>).
    /// </summary>
    /// <param name="contractName">The contract name to convert.</param>
    /// <returns>The PascalCase schema prefix ending in <c>_</c>.</returns>
    public static string ToSchemaPrefix(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        var sb = new StringBuilder(contractName.Length + 1);
        foreach (string segment in contractName.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
            {
                sb.Append(segment.AsSpan(1));
            }
        }

        sb.Append('_');
        return sb.ToString();
    }
}
