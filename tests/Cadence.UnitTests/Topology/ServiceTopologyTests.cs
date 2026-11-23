using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Topology;

/// <summary>
/// The service topology manifest is the one list of services (#5315). The gateway and the AppHost read it; docker
/// compose and the docs that describe the topology can't, so these tests fail when one of them disagrees with it.
/// </summary>
public sealed class ServiceTopologyTests
{
    private static readonly string Root = Cadence.Topology.RepositoryRoot.Find(AppContext.BaseDirectory);

    /// <summary>
    /// Validates the manifest itself: unique names, ports, address variables and route prefixes, ports after the
    /// gateway's, address variables named after the service, and a directory under <c>apps/</c> with a README.
    /// </summary>
    [Fact(DisplayName = "ServiceTopology_Manifest_IsConsistent")]
    public void ServiceTopologyManifestIsConsistent()
    {
        var services = ServiceTopology.Services;

        services.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        services.Select(s => s.Port).Should().OnlyHaveUniqueItems().And.OnlyContain(p => p > ServiceTopology.GatewayPort);
        services.Select(s => s.UrlVariable).Should().OnlyHaveUniqueItems();
        services.SelectMany(s => s.RoutePrefixes).Should().OnlyHaveUniqueItems().And.OnlyContain(p => Regex.IsMatch(p, "^[a-z][a-z0-9-]*(/[a-z][a-z0-9-]*)?$"));
        foreach (var service in services)
        {
            service.UrlVariable.Should().Be($"{service.Name.Replace('-', '_').ToUpperInvariant()}_SERVICE_URL");
            service.RoutePrefixes.Should().NotBeEmpty(service.Name);
            service.SchemaPrefix.Should().NotBeNullOrWhiteSpace(service.Name);
            service.ProgramTypeName.Should().NotBeNullOrWhiteSpace(service.Name);
            service.AssemblyName.Should().NotBeNullOrWhiteSpace(service.Name);
            service.SourceDirectory.Should().StartWith("apps/", service.Name);
            File.Exists(Path.Combine(Root, "apps", service.Directory, "README.md")).Should().BeTrue($"apps/{service.Directory} is {service.Name}'s directory");
            File.Exists(Path.Combine(Root, service.CsprojRelativePath)).Should().BeTrue($"{service.CsprojRelativePath} exists");
        }

        ServiceTopology.Get("kernel").SchemaPrefix.Should().Be("Kernel_");
        ServiceTopology.Get("econsent").SchemaPrefix.Should().Be("Econsent_");
        ServiceTopology.Modules.Select(m => m.Name).Should().BeEquivalentTo("execution", "intelligence");
        ServiceTopology.Modules.Select(m => m.SchemaPrefix).Should().BeEquivalentTo("ExecutionForms_", "Intelligence_");
        foreach (var module in ServiceTopology.Modules)
        {
            File.Exists(Path.Combine(Root, module.CsprojRelativePath)).Should().BeTrue($"{module.CsprojRelativePath} exists");
        }

        ServiceTopology.Get("kernel").LocalUrl.Should().Be(new Uri("http://localhost:8018/"));
        var unknown = () => ServiceTopology.Get("nope");
        unknown.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates docker compose against the manifest: each service publishes its port, and the gateway is given each
    /// service's address under its variable.
    /// </summary>
    [Fact(DisplayName = "ServiceTopology_DockerCompose_Matches")]
    public void ServiceTopologyDockerComposeMatches()
    {
        var compose = File.ReadAllText(Path.Combine(Root, "docker", "docker-compose.yml"));

        Block(compose, "front-proxy").Should().Contain($"- \"{ServiceTopology.GatewayPort}:{ServiceTopology.GatewayPort}\"");
        var gateway = Block(compose, "gateway");
        foreach (var service in ServiceTopology.Services)
        {
            Block(compose, service.Name).Should().Contain($"- \"{service.Port}:8080\"", service.Name);
            gateway.Should().Contain($"{service.UrlVariable}: http://{service.Name}:8080", service.Name);
        }
    }

    /// <summary>
    /// Validates the docs that list the services against the manifest: the gateway's address variables
    /// (ENVIRONMENT_VARIABLES.md), the local ports (LOCAL_DEV_ENVIRONMENT.md and the AppHost README) and the running
    /// services table (ARCHITECTURE.md §3) and the gateway's route table (its README).
    /// </summary>
    [Fact(DisplayName = "ServiceTopology_Docs_Match")]
    public void ServiceTopologyDocsMatch()
    {
        var variables = Read("docs", "ENVIRONMENT_VARIABLES.md");
        var localDev = Read("docs", "LOCAL_DEV_ENVIRONMENT.md");
        var appHost = Read("apps", "apphost", "README.md");
        var architecture = Read("ARCHITECTURE.md");
        var gatewayRoutes = Read("apps", "gateway", "README.md");

        foreach (var service in ServiceTopology.Services)
        {
            var port = service.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            variables.Should().MatchRegex($@"\|\s*`{service.UrlVariable}`\s*\|\s*{port}\s*\|", service.Name);
            localDev.Should().MatchRegex($@"\|\s*(?:{Regex.Escape(service.Name)}|{Regex.Escape(service.Directory)})\b[^|]*\|\s*{port}\s*\|", service.Name);
            appHost.Should().MatchRegex($@"\|\s*{port}\s*\|\s*{Regex.Escape(service.Name)}\s*\|", service.Name);
            architecture.Should().MatchRegex(
                $@"\[{Regex.Escape(service.Directory)}\]\(apps/{Regex.Escape(service.Directory)}/README\.md\)\s*\|[^|]*\|\s*{port}\s*\|", service.Name);
            foreach (var prefix in service.RoutePrefixes)
            {
                gatewayRoutes.Should().MatchRegex(
                    $@"\|\s*`/api/v1/{prefix}/\*\*`\s*\|\s*{Regex.Escape(service.Name)}\b[^|]*\|\s*`{service.UrlVariable}`\s*\|", service.Name);
            }
        }
    }

    private static string Block(string compose, string service)
    {
        var start = Regex.Match(compose, $@"^  {Regex.Escape(service)}:\s*$", RegexOptions.Multiline);
        start.Success.Should().BeTrue($"docker compose defines {service}");
        var next = Regex.Match(compose[(start.Index + start.Length)..], @"^  [a-z][a-z0-9_-]*:\s*$", RegexOptions.Multiline);
        return next.Success ? compose.Substring(start.Index, start.Length + next.Index) : compose[start.Index..];
    }

    [Fact(DisplayName = "ServiceTopology_SlnxAndServiceReferencesProps_Match")]
    public void ServiceTopologySlnxAndServiceReferencesPropsMatch()
    {
        File.Exists(Path.Combine(Root, Cadence.Topology.RepositoryRoot.SolutionFileName)).Should().BeTrue("Cadence.slnx is the canonical solution file");
        File.Exists(Path.Combine(Root, Cadence.Topology.RepositoryRoot.LegacySolutionFileName)).Should().BeFalse("legacy Cadence.sln must not coexist with Cadence.slnx");

        string slnx = Read("Cadence.slnx");
        string props = Read("packages", "topology", "ServiceReferences.props").Replace('\\', '/');

        foreach (var service in ServiceTopology.Services)
        {
            slnx.Should().Contain($"Path=\"{service.CsprojRelativePath}\"", service.Name);
            props.Should().Contain(service.CsprojRelativePath, service.Name);
        }

        foreach (var module in ServiceTopology.Modules)
        {
            slnx.Should().Contain($"Path=\"{module.CsprojRelativePath}\"", module.Name);
            props.Should().Contain(module.CsprojRelativePath, module.Name);
        }
    }

    [Fact(DisplayName = "RepositoryRoot_ResolvesSlnxAndFallbackMarkers")]
    public void RepositoryRootResolvesSlnxAndFallbackMarkers()
    {
        string tempBase = Path.Combine(Path.GetTempPath(), $"cadence-reporoot-{Guid.NewGuid():N}");
        try
        {
            string slnxRoot = Path.Combine(tempBase, "slnx");
            string slnxNested = Path.Combine(slnxRoot, "a", "b");
            Directory.CreateDirectory(slnxNested);
            File.WriteAllText(Path.Combine(slnxRoot, "Cadence.slnx"), "<Solution />\n");
            Path.GetFullPath(Cadence.Topology.RepositoryRoot.Find(slnxNested)).Should().Be(Path.GetFullPath(slnxRoot));
            Cadence.Topology.RepositoryRoot.ResolveSolutionFileName(slnxRoot).Should().Be("Cadence.slnx");

            string slnRoot = Path.Combine(tempBase, "sln");
            string slnNested = Path.Combine(slnRoot, "x", "y");
            Directory.CreateDirectory(slnNested);
            File.WriteAllText(Path.Combine(slnRoot, "Cadence.sln"), string.Empty);
            Path.GetFullPath(Cadence.Topology.RepositoryRoot.Find(slnNested)).Should().Be(Path.GetFullPath(slnRoot));
            Cadence.Topology.RepositoryRoot.ResolveSolutionFileName(slnRoot).Should().Be("Cadence.sln");

            string globalJsonRoot = Path.Combine(tempBase, "global");
            string globalNested = Path.Combine(globalJsonRoot, "nested");
            Directory.CreateDirectory(globalNested);
            File.WriteAllText(Path.Combine(globalJsonRoot, "global.json"), "{}");
            Path.GetFullPath(Cadence.Topology.RepositoryRoot.Find(globalNested)).Should().Be(Path.GetFullPath(globalJsonRoot));

            string isolated = Path.Combine(tempBase, "isolated");
            Directory.CreateDirectory(isolated);
            Action missing = () => Cadence.Topology.RepositoryRoot.Find(isolated);
            missing.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));
}
