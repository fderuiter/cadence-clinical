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
    private static readonly string Root = RepositoryRoot();

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
            File.Exists(Path.Combine(Root, "apps", service.Directory, "README.md")).Should().BeTrue($"apps/{service.Directory} is {service.Name}'s directory");
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

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Cadence.sln not found above the test output directory.");
    }
}
