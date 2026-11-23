using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.JavaScript;
using Aspire.Hosting.Testing;

using Cadence.Topology;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.AppHost;

/// <summary>
/// Builds the AppHost's resource graph (no containers or processes start) and checks it can actually run the
/// platform: every service is a project, listens on the port docker compose publishes it on, gets the secrets and
/// database it refuses to start without, and the gateway is told where each service is. The core profile and the
/// existing-PostgreSQL and existing-OIDC switches (#4991) are checked the same way.
/// </summary>
public sealed class AppHostModelTests(AppHostModelTests.ModelFixture fixture) : IClassFixture<AppHostModelTests.ModelFixture>
{
    // From the topology manifest; ServiceTopologyTests pins the manifest to docker compose and the docs.
    private static readonly Dictionary<string, int> ServicePorts = ServiceTopology.Services
        .ToDictionary(s => s.Name, s => s.Port, StringComparer.Ordinal)
        .Append(new KeyValuePair<string, int>("gateway", ServiceTopology.GatewayPort))
        .ToDictionary(StringComparer.Ordinal);

    private static readonly string[] WithoutDatabase =
        ["gateway", .. ServiceTopology.Services.Where(s => !s.Database).Select(s => s.Name)];

    [Fact]
    public async Task EveryService_IsAProjectOnItsComposePort()
    {
        var projects = await ProjectsAsync();

        projects.Keys.Should().BeEquivalentTo(ServicePorts.Keys);
        foreach (var (name, project) in projects)
        {
            var http = project.Annotations.OfType<EndpointAnnotation>().Should().ContainSingle(e => e.Name == "http", name).Which;
            http.Port.Should().Be(ServicePorts[name], name);
        }
    }

    [Fact]
    public async Task EveryService_GetsTheSettingsItRefusesToStartWithout()
    {
        var projects = await ProjectsAsync();

        foreach (var (name, project) in projects)
        {
            var variables = await EnvironmentNamesAsync(project);
            variables.Should().Contain("GATEWAY_SECRET", name);
            if (!WithoutDatabase.Contains(name))
            {
                variables.Should().Contain(["AUDIT_LOG_SECRET_KEY", "ConnectionStrings__cadence", "Database__MigrateOnStartup"], name);
            }
        }

        (await EnvironmentNamesAsync(projects["execution"])).Should().Contain("DemographicsEncryption__KeyBase64");
        (await EnvironmentNamesAsync(projects["econsent"])).Should().Contain("FIELD_ENCRYPTION_KEY");
        (await EnvironmentNamesAsync(projects["payments"])).Should().Contain("FIELD_ENCRYPTION_KEY");
        (await EnvironmentNamesAsync(projects["interop"])).Should().Contain(["FIELD_LOOKUP_HASH_KEY", "CADENCE_SCE_SIGNING_KEY"]);
        (await EnvironmentNamesAsync(projects["imaging"]))
            .Should().Contain(["CADENCE_IMAGING_DEID_SIGNING_KEY", "CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY"]);
    }

    [Fact]
    public async Task Gateway_IsToldWhereEveryServiceIs()
    {
        var projects = await ProjectsAsync();

        var variables = await EnvironmentNamesAsync(projects["gateway"]);

        variables.Should().Contain("OIDC_AUTHORITY").And.Contain("OIDC_AUDIENCE");
        variables.Should().Contain(ServiceTopology.Services.Select(s => s.UrlVariable));
    }

    [Fact]
    public async Task FullProfile_RunsTheWebAppAndEveryContainer()
    {
        var resources = await ResourcesAsync();

        resources.Should().ContainKey("web").WhoseValue.Should().BeOfType<ViteAppResource>();
        resources.Keys.Should().Contain(["postgres", "cadence", "keycloak", "minio"]);
    }

    /// <summary>
    /// The subject portal runs beside the web app on its compose port (#5335) and, like the web app, calls its own origin
    /// while its dev server's /api proxy forwards to the gateway, which sends no CORS headers (#5451).
    /// </summary>
    [Fact]
    public async Task FullProfile_RunsTheSubjectPortalOnItsComposePort()
    {
        var resources = await ResourcesAsync();

        var portal = resources.Should().ContainKey("subject-portal").WhoseValue.Should().BeOfType<ViteAppResource>().Subject;
        portal.Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "http").Port.Should().Be(5174);
        var environment = await EnvironmentAsync(portal);
        environment["VITE_API_BASE_URL"].Should().BeOfType<EndpointReference>().Which.Resource.Name.Should().Be("subject-portal");
        environment["CADENCE_GATEWAY_URL"].Should().BeOfType<EndpointReference>().Which.Resource.Name.Should().Be("gateway");
        environment.Keys.Should().Contain(["VITE_KEYCLOAK_URL", "VITE_KEYCLOAK_REALM", "VITE_KEYCLOAK_CLIENT_ID"]);
    }

    [Fact]
    public async Task CoreProfile_RunsOnlyWhatTheCanvasCalls()
    {
        var resources = await ResourcesAsync("--AppHost:Profile=core");
        var projects = resources.Values.OfType<ProjectResource>().ToDictionary(p => p.Name, StringComparer.Ordinal);

        projects.Keys.Should().BeEquivalentTo(["gateway", .. ServiceTopology.Services.Where(s => s.Core).Select(s => s.Name)]);
        resources.Should().ContainKey("web");
        resources.Should().NotContainKey("subject-portal");
        resources.Should().NotContainKey("minio");
        foreach (var (name, project) in projects)
        {
            project.Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "http").Port.Should().Be(ServicePorts[name], name);
        }

        // The gateway refuses to start outside Development without every service URL; a service left out points
        // at its usual host port, where nothing listens.
        var gateway = await EnvironmentAsync(projects["gateway"]);
        gateway["EXECUTION_SERVICE_URL"].Should().BeOfType<EndpointReference>();
        gateway["ETMF_SERVICE_URL"].Should().BeOfType<EndpointReference>();
        gateway["CTMS_SERVICE_URL"].Should().BeOfType<EndpointReference>();
        gateway["KNOWLEDGE_SERVICE_URL"].Should().BeOfType<EndpointReference>();
        gateway["KERNEL_SERVICE_URL"].Should().BeOfType<EndpointReference>();
        foreach (var service in ServiceTopology.Services.Where(s => !s.Core))
        {
            gateway[service.UrlVariable].Should().Be($"http://localhost:{service.Port}", service.Name);
        }
        (await EnvironmentAsync(projects["execution"])).Keys
            .Should().Contain(["GATEWAY_SECRET", "AUDIT_LOG_SECRET_KEY", "ConnectionStrings__cadence", "DemographicsEncryption__KeyBase64"]);
        (await EnvironmentAsync(projects["etmf"]))["EXECUTION_SERVICE_URL"]
            .Should().BeOfType<EndpointReference>().Which.Resource.Name.Should().Be("execution");
    }

    [Fact]
    public async Task ExistingPostgres_ReplacesThePostgresContainer()
    {
        var resources = await ResourcesAsync("--AppHost:Profile=core", "--ConnectionStrings:cadence=Host=localhost;Database=cadence");

        resources.Values.OfType<PostgresServerResource>().Should().BeEmpty();
        resources.Should().ContainKey("cadence").WhoseValue.Should().BeAssignableTo<IResourceWithConnectionString>().And.NotBeOfType<PostgresDatabaseResource>();
        (await EnvironmentAsync(resources["kernel"])).Keys.Should().Contain("ConnectionStrings__cadence");
    }

    /// <summary>
    /// The kernel host provisions the tenant the local realm's users sign in with (ADR-2514, ADR-2542), so the Canvas's
    /// kernel reads are served instead of refused as "not an active kernel tenant" (#5430), and seeds the demo study the
    /// realm's users are granted (ADR-2543, #5452).
    /// </summary>
    [Fact]
    public async Task Kernel_ProvisionsTheLocalRealmsTenant()
    {
        var resources = await ResourcesAsync("--AppHost:Profile=core");

        var kernel = await EnvironmentAsync(resources["kernel"]);
        kernel["ASPNETCORE_ENVIRONMENT"].Should().Be("Development", "the bootstrap refuses to run in any other environment");
        kernel["Kernel__DevelopmentTenant__Code"].Should().Be("cadence-local");
        kernel["Kernel__DevelopmentTenant__DemoData"].Should().Be("true", "the realm's users read the demo study (ADR-2543)");
    }

    [Fact]
    public async Task ExistingOidcAuthority_ReplacesTheKeycloakContainer()
    {
        var resources = await ResourcesAsync("--AppHost:Profile=core", "--AppHost:OidcAuthority=https://id.example.test/realms/trial");

        resources.Should().NotContainKey("keycloak");
        var gateway = await EnvironmentAsync(resources["gateway"]);
        gateway["OIDC_AUTHORITY"].Should().Be("https://id.example.test/realms/trial");
        gateway["OIDC_REQUIRE_HTTPS_METADATA"].Should().Be("true");
        gateway["OIDC_AUDIENCE"].Should().Be("cadence-api");
        var web = await EnvironmentAsync(resources["web"]);
        web["VITE_KEYCLOAK_URL"].Should().Be("https://id.example.test");
        web["VITE_KEYCLOAK_REALM"].Should().Be("trial");
        web["VITE_KEYCLOAK_CLIENT_ID"].Should().Be("cadence-web");
        web.Keys.Should().Contain("VITE_API_BASE_URL");
    }

    /// <summary>
    /// The web app calls its own origin and Vite's /api proxy forwards to the gateway: the gateway sends no CORS
    /// headers, so a browser page on the web port calling the gateway's port directly is blocked (#5429).
    /// </summary>
    [Fact]
    public async Task WebApp_CallsItsOwnOrigin_AndProxiesApiCallsToTheGateway()
    {
        var resources = await ResourcesAsync("--AppHost:Profile=core");

        var web = await EnvironmentAsync(resources["web"]);
        web["VITE_API_BASE_URL"].Should().BeOfType<EndpointReference>().Which.Resource.Name.Should().Be("web");
        web["CADENCE_GATEWAY_URL"].Should().BeOfType<EndpointReference>().Which.Resource.Name.Should().Be("gateway");
    }

    [Theory]
    [InlineData("--AppHost:Profile=everything")]
    [InlineData("--AppHost:OidcAuthority=http://localhost:8080")]
    [InlineData("--AppHost:OidcAuthority=not a url/realms/cadence")]
    public async Task InvalidSettings_StopTheAppHostWithAMessage(string setting)
    {
        var build = () => BuildUncachedResourcesAsync(setting);

        await build.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<Dictionary<string, ProjectResource>> ProjectsAsync() =>
        (await ResourcesAsync()).Values.OfType<ProjectResource>().ToDictionary(p => p.Name, StringComparer.Ordinal);

    private Task<IReadOnlyDictionary<string, IResource>> ResourcesAsync(params string[] args) =>
        fixture.GetResourcesAsync(args);

    private static async Task<Dictionary<string, IResource>> BuildUncachedResourcesAsync(params string[] args)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Cadence_AppHost>(args, TestContext.Current.CancellationToken);
        await using var app = await builder.BuildAsync(TestContext.Current.CancellationToken);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        return model.Resources.ToDictionary(r => r.Name, StringComparer.Ordinal);
    }

    public sealed class ModelFixture : IAsyncDisposable
    {
        private readonly Dictionary<string, (DistributedApplication App, IReadOnlyDictionary<string, IResource> Resources)> _profiles =
            new(StringComparer.Ordinal);

        public async Task<IReadOnlyDictionary<string, IResource>> GetResourcesAsync(string[] args)
        {
            string key = string.Join('\u001f', args);
            if (!_profiles.TryGetValue(key, out var cached))
            {
                var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Cadence_AppHost>(
                    args,
                    TestContext.Current.CancellationToken);
                var app = await builder.BuildAsync(TestContext.Current.CancellationToken);
                var model = app.Services.GetRequiredService<DistributedApplicationModel>();
                var resources = model.Resources.ToDictionary(r => r.Name, StringComparer.Ordinal);
                cached = (app, resources);
                _profiles[key] = cached;
            }

            return cached.Resources;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var (app, _) in _profiles.Values)
            {
                await app.DisposeAsync();
            }

            _profiles.Clear();
        }
    }

    private static async Task<IReadOnlyCollection<string>> EnvironmentNamesAsync(IResource resource) =>
        (await EnvironmentAsync(resource)).Keys.ToList();

    /// <summary>The resource's environment in publish mode; plain values are strings, the rest are references.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(IResource resource)
    {
        var context = new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return new Dictionary<string, object>(context.EnvironmentVariables, StringComparer.Ordinal);
    }
}
