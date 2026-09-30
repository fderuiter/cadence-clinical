using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.AppHost;

/// <summary>
/// Builds the AppHost's resource graph (no containers or processes start) and checks it can actually run the
/// platform: every service is a project, listens on the port docker compose publishes it on, gets the secrets and
/// database it refuses to start without, and the gateway is told where each service is.
/// </summary>
public sealed class AppHostModelTests
{
    private static readonly Dictionary<string, int> ServicePorts = new(StringComparer.Ordinal)
    {
        ["gateway"] = 8000,
        ["designer"] = 8001,
        ["execution"] = 8002,
        ["etmf"] = 8003,
        ["interop"] = 8004,
        ["quality"] = 8005,
        ["notifications"] = 8006,
        ["ctms"] = 8007,
        ["safety"] = 8008,
        ["tickets"] = 8009,
        ["eisf"] = 8010,
        ["econsent"] = 8011,
        ["org"] = 8012,
        ["fileshare"] = 8013,
        ["imaging"] = 8014,
        ["knowledge"] = 8015,
        ["payments"] = 8016,
        ["ai-gateway"] = 8017,
        ["kernel"] = 8018,
    };

    private static readonly string[] WithoutDatabase = ["gateway", "designer", "ai-gateway"];

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
        (await EnvironmentNamesAsync(projects["interop"])).Should().Contain("FIELD_LOOKUP_HASH_KEY");
    }

    [Fact]
    public async Task Gateway_IsToldWhereEveryServiceIs()
    {
        var projects = await ProjectsAsync();

        var variables = await EnvironmentNamesAsync(projects["gateway"]);

        variables.Should().Contain("OIDC_AUTHORITY");
        foreach (string service in ServicePorts.Keys.Where(s => s != "gateway"))
        {
            variables.Should().Contain($"{service.Replace('-', '_').ToUpperInvariant()}_SERVICE_URL");
        }
    }

    private static async Task<Dictionary<string, ProjectResource>> ProjectsAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Cadence_AppHost>(TestContext.Current.CancellationToken);
        await using var app = await builder.BuildAsync(TestContext.Current.CancellationToken);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        return model.Resources.OfType<ProjectResource>().ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyCollection<string>> EnvironmentNamesAsync(IResource resource)
    {
        var context = new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables.Keys.ToList();
    }
}
