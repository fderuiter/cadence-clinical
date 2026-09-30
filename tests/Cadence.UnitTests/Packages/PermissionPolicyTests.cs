using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// <c>RequirePermission("resource:action")</c> admits a signed caller whose roles hold the permission in the
/// permission matrix and answers 403 to any other caller, instead of failing because no policy of that name exists
/// (#4961, ADR-2510).
/// </summary>
public sealed class PermissionPolicyTests : IAsyncLifetime
{
    private const string Probe = "/probe";

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer().UseTestGatewaySecret();
        builder.Services.AddCadenceSecurity();

        _app = builder.Build();
        _app.UseCadenceSecurity();
        _app.MapGet(Probe, () => TypedResults.Ok()).RequirePermission("study_design:create");
        _app.MapGet("/signed-in", () => TypedResults.Ok());
        await _app.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("Sponsor Designer")]
    [InlineData("sysadmin")]
    public async Task ACallerHoldingThePermission_IsAdmitted(string role)
    {
        using var client = Client(role);

        var response = await client.GetAsync(Probe, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("CRA")]
    [InlineData("Site Investigator")]
    [InlineData("Subject")]
    public async Task ACallerWithoutThePermission_Gets403(string role)
    {
        using var client = Client(role);

        var response = await client.GetAsync(Probe, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnUnsignedCaller_Gets401()
    {
        using var client = _app!.GetTestClient();

        var response = await client.GetAsync(Probe, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RoutesWithoutAPermission_KeepTheAuthenticatedCallerFallback()
    {
        using var client = Client("Subject");

        var response = await client.GetAsync("/signed-in", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Permission:study_design")]
    [InlineData("Permission::create")]
    [InlineData("Permission:study_design:")]
    public async Task AMalformedPermissionPolicyName_IsRefused(string policyName)
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var act = () => provider.GetPolicyAsync(policyName);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OtherPolicyNames_AreLeftToTheRegisteredOptions()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("Named", policy => policy.RequireAuthenticatedUser());
        var provider = new PermissionPolicyProvider(Options.Create(options));

        (await provider.GetPolicyAsync("Named")).Should().NotBeNull();
        (await provider.GetPolicyAsync("Unknown")).Should().BeNull();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string role)
    {
        var handler = new TestSecurityContext.GatewaySigningHandler(TestSecurityContext.Identity($"user-{Guid.NewGuid():N}", role))
        {
            InnerHandler = _app!.GetTestServer().CreateHandler(),
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }
}
