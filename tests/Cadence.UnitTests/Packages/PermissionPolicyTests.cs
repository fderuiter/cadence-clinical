using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing;
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
        _app.MapPost("/tickets-write-probe", () => TypedResults.Ok()).RequirePermission("tickets:create");
        _app.MapGet("/tickets-read-probe", () => TypedResults.Ok()).RequirePermission("tickets:read");
        _app.MapPost("/subjects-write-probe", () => TypedResults.Ok()).RequirePermission("subject_enrollment:create");
        _app.MapGet("/signed-in", () => TypedResults.Ok()).RequireSignedInCaller("test route");
        _app.MapGet("/undeclared", () => TypedResults.Ok());
        _app.MapGet("/studies/{studyId}/probe", () => TypedResults.Ok())
            .RequirePermission("study_design:read")
            .RequireStudyScope("studyId");
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

    /// <summary>A route that declares no authorization is denied to every signed caller (ADR-2516).</summary>
    [Theory]
    [InlineData("Subject")]
    [InlineData("sysadmin")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task RoutesThatDeclareNothing_AreDeniedByDefault(string role)
    {
        using var client = Client(role);

        var response = await client.GetAsync("/undeclared", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoutesThatDeclareNothing_ChallengeAnUnsignedCaller()
    {
        using var client = _app!.GetTestClient();

        var response = await client.GetAsync("/undeclared", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A path that matches no route still answers 404 to a signed caller and 401 to an unsigned one.</summary>
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task PathsThatMatchNoRoute_AnswerNotFoundOnlyAfterAuthentication()
    {
        using var signed = Client("sysadmin");
        using var unsigned = _app!.GetTestClient();

        var signedResponse = await signed.GetAsync("/no-such-route", Ct);
        var unsignedResponse = await unsigned.GetAsync("/no-such-route", Ct);

        signedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unsignedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireSignedInCaller_AdmitsAnySignedCaller()
    {
        using var client = Client("Subject");

        var response = await client.GetAsync("/signed-in", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Study scope reads the study from the route and admits only a caller assigned to it (ADR-2516).</summary>
    [Theory]
    [InlineData("STUDY-A", HttpStatusCode.OK)]
    [InlineData("*", HttpStatusCode.OK)]
    [InlineData("STUDY-B", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Forbidden)]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task StudyScope_AdmitsOnlyCallersAssignedToTheRouteStudy(string? assignment, HttpStatusCode expected)
    {
        var identity = TestSecurityContext.Identity($"user-{Guid.NewGuid():N}", "Sponsor Designer")
            with
        { StudyIds = assignment is null ? [] : [assignment] };
        using var client = _app!.GetTestClient();
        foreach (var header in TestSecurityContext.CreateHmacHeaders(identity))
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        var response = await client.GetAsync("/studies/STUDY-A/probe?study_id=STUDY-B", Ct);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task StudyScope_IgnoresAStudyIdInAnUnsignedHeader()
    {
        var identity = TestSecurityContext.Identity($"user-{Guid.NewGuid():N}", "Sponsor Designer") with { StudyIds = ["STUDY-B"] };
        using var client = _app!.GetTestClient();
        foreach (var header in TestSecurityContext.CreateHmacHeaders(identity))
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        client.DefaultRequestHeaders.Add("X-Study-Id", "STUDY-B");
        var response = await client.GetAsync("/studies/STUDY-A/probe", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AStudyAssignmentThatWasNotSigned_IsRejected()
    {
        var identity = TestSecurityContext.Identity($"user-{Guid.NewGuid():N}", "Sponsor Designer") with { StudyIds = ["STUDY-B"] };
        using var client = _app!.GetTestClient();
        foreach (var header in TestSecurityContext.CreateHmacHeaders(identity))
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Key == GatewayHeaders.StudyIds ? "STUDY-A" : header.Value);
        }

        var response = await client.GetAsync("/studies/STUDY-A/probe", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APermissionNoRoleHolds_IsRefused()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var act = () => provider.GetPolicyAsync(PermissionPolicyProvider.PolicyName("study_design:fly"));

        await act.Should().ThrowAsync<InvalidOperationException>();
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

    [Theory]
    [InlineData("trial-a1b2c3d4e5f6", HttpStatusCode.OK)]
    [InlineData("trial-001122334455", HttpStatusCode.OK)]
    [InlineData("trial-ABCDEF123456", HttpStatusCode.Forbidden)]
    [InlineData("trial-a1b2c3", HttpStatusCode.Forbidden)]
    [InlineData("tenant-enterprise-01", HttpStatusCode.Forbidden)]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task TrialCoordinatorWriteGrants_AreAdmittedOnlyInVerifiedTrialTenants_WhileReadGrantsRemainIntact(
        string tenantId,
        HttpStatusCode expectedWriteStatus)
    {
        var identity = TestSecurityContext.Identity($"tc-{Guid.NewGuid():N}", "trial_coordinator") with
        {
            TenantId = tenantId,
        };
        using var client = ClientForIdentity(identity);

        var writeResponse = await client.PostAsync("/tickets-write-probe", content: null, Ct);
        var readResponse = await client.GetAsync("/tickets-read-probe", Ct);

        writeResponse.StatusCode.Should().Be(expectedWriteStatus);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task MultiRoleEnterpriseCaller_RetainsSponsorGrants_WhileTrialCoordinatorOnlyWritesAreIgnored()
    {
        var identity = TestSecurityContext.Identity($"multi-{Guid.NewGuid():N}", "Sponsor Admin", "Trial Coordinator") with
        {
            TenantId = "tenant-enterprise-01",
        };
        using var client = ClientForIdentity(identity);

        (await client.PostAsync("/tickets-write-probe", content: null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/subjects-write-probe", content: null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string role) =>
        ClientForIdentity(TestSecurityContext.Identity($"user-{Guid.NewGuid():N}", role));

    private HttpClient ClientForIdentity(GatewayIdentity identity)
    {
        var handler = new TestSecurityContext.GatewaySigningHandler(identity)
        {
            InnerHandler = _app!.GetTestServer().CreateHandler(),
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }
}
