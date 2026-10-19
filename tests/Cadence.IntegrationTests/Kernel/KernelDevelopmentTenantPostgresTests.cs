using System;
using System.Net;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The development tenant bootstrap (ADR-2542, #5430) against PostgreSQL: with <c>Kernel:DevelopmentTenant:Code</c> set,
/// the real kernel host, connected as the application role, provisions the tenant on start through the command
/// pipeline, so a caller signed in with that tenant code is served instead of refused. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelDevelopmentTenantPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Operator = "svc:kernel-development-bootstrap";

    /// <summary>
    /// The first start provisions the tenant with an audit entry naming the bootstrap's service account, and a caller
    /// with that tenant code is served (a kernel search, where a reader with no grant gets no hits); a second start finds the tenant and provisions nothing more.
    /// </summary>
    [Fact(DisplayName = "DevelopmentTenant_IsProvisionedOnStart_ThroughThePipeline_Once")]
    public async Task DevelopmentTenantIsProvisionedOnStartThroughThePipelineOnce()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string code = $"dev-{Guid.NewGuid():N}"[..20];

        using (var first = Host(code))
        {
            using var client = first.CreateSignedClient(new GatewayIdentity("crc@dev-tenant.test", [], [], TenantId: code));
            using var response = await client.GetAsync(new Uri("/api/v1/kernel/search?q=DEMO", UriKind.Relative), ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        }

        using (var second = Host(code))
        {
            using var client = second.CreateSignedClient(new GatewayIdentity("crc@dev-tenant.test", [], [], TenantId: code));
            using var response = await client.GetAsync(new Uri("/api/v1/kernel/search?q=DEMO", UriKind.Relative), ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM kernel.tenants WHERE code = @code AND is_active", code))
            .Should().Be(1L, "the second start finds the tenant and provisions no other");
        (await ScalarAsync(connection, "SELECT created_by FROM kernel.tenants WHERE code = @code", code)).Should().Be(Operator);
        (await ScalarAsync(
                connection,
                "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code AND a.actor_id = @actor",
                code))
            .Should().Be(1L, "provisioning wrote one audit entry, as the bootstrap's service account");
    }

    /// <summary>A caller signed in with a tenant code no one provisioned is still refused (ADR-2514).</summary>
    [Fact(DisplayName = "OtherTenantCodes_AreStillRefused")]
    public async Task OtherTenantCodesAreStillRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        using var host = Host($"dev-{Guid.NewGuid():N}"[..20]);
        using var client = host.CreateSignedClient(new GatewayIdentity("crc@dev-tenant.test", [], [], TenantId: "never-provisioned"));
        using var response = await client.GetAsync(new Uri("/api/v1/kernel/search?q=DEMO", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Actor.ServiceAccount("kernel-development-bootstrap").Id.Should().Be(Operator);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, string code)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("actor", Operator);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> Host(string code) =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .UseSetting("Kernel:DevelopmentTenant:Code", code));
}
