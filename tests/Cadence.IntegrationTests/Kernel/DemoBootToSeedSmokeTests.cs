using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Gateway;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Persistence;
using Cadence.Testing.Security;
using Cadence.Topology;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

using Yarp.ReverseProxy.Forwarder;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Non-skipping PostgreSQL 18 fixture for <see cref="DemoBootToSeedSmokeTests"/>. Uses <c>CADENCE_TEST_POSTGRES</c> when
/// provided (for example inside full CI <c>dotnet-tests</c>) and otherwise starts a <see cref="PostgresTestImage.Image"/>
/// container via Testcontainers directly so <c>--filter Category=Smoke</c> never silently skips.
/// </summary>
public sealed class DemoBootToSeedSmokePostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _serverConnectionString;
    private string? _databaseName;

    /// <summary>
    /// Connection string for the isolated PostgreSQL 18 database used by the Demo host (<c>Database:MigrateOnStartup=true</c>
    /// requires the schema-owning role per ADR-2250, matching <c>docker/docker-compose.yml</c> and <c>AppHost</c>).
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        string? external = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _serverConnectionString = external;
        }
        else
        {
            string userSocket = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docker",
                "run",
                "docker.sock");
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")) && File.Exists(userSocket))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{userSocket}");
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED")))
            {
                Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
            }

            _container = new PostgreSqlBuilder(PostgresTestImage.Image)
                .WithUsername("postgres")
                .Build();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _container.StartAsync(cts.Token).ConfigureAwait(false);
            _serverConnectionString = _container.GetConnectionString();
        }

        await VerifyPostgresMajorAsync(_serverConnectionString).ConfigureAwait(false);

        _databaseName = $"cadence_demo_smoke_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _serverConnectionString,
            $"CREATE DATABASE \"{_databaseName}\"")
            .ConfigureAwait(false);

        ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        if (_serverConnectionString is not null && _databaseName is not null)
        {
            try
            {
                await ExecuteSqlAsync(_serverConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)")
                    .ConfigureAwait(false);
            }
            catch (NpgsqlException)
            {
                // Container teardown will reclaim ephemeral databases if the server is already shutting down.
            }
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    private static async Task VerifyPostgresMajorAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SHOW server_version_num", connection);
        string raw = (string)(await command.ExecuteScalarAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("SHOW server_version_num returned null."));
        int versionNumber = int.Parse(raw, CultureInfo.InvariantCulture);
        if (PostgresTestImage.Mismatch(versionNumber) is { } mismatch)
        {
            throw new InvalidOperationException(mismatch);
        }
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Fast Boot-to-Seed smoke test that gates OCI container image builds in CI. Boots <see cref="Cadence.Kernel.Host.Program"/>
/// and <see cref="Cadence.Apps.Gateway.Program"/> in the <c>Demo</c> environment against PostgreSQL 18, verifies startup
/// migrations, idempotent <c>CADENCE-101</c> demo seeding, and <c>/healthz/ready</c> + <c>/health/ready</c> readiness,
/// logs in as seeded demo persona <c>crc.site101</c> via OIDC through the Gateway, and executes a read + write + read-back
/// flow against <c>CADENCE-101</c>.
/// </summary>
[Trait("Category", "Smoke")]
[Trait("Category", "Integration")]
public sealed class DemoBootToSeedSmokeTests(DemoBootToSeedSmokePostgresFixture fixture)
    : IClassFixture<DemoBootToSeedSmokePostgresFixture>
{
    private const string OidcIssuer = "https://keycloak.demo.cadence.internal/realms/cadence";
    private const string OidcAudience = "cadence-api";
    private const string DownstreamKernelBaseUrl = "http://kernel.demo.cadence.internal";
    private const string DemoTenantCode = "cadence-local";

    // Seeded crc.site101 persona from packages/demo-fixtures/demo-seed.json and docker/cadence-realm.json.
    private const string CrcUsername = "crc.site101";
    private const string CrcSubjectId = "1a4345cc-364c-4b8b-881e-93bbea528b0a";
    private static readonly string[] CrcRealmRoles = ["Site Investigator"];

    private static readonly SymmetricSecurityKey OidcSigningKey =
        new(Encoding.UTF8.GetBytes("cadence-demo-smoke-oidc-signing-key-0123456789")) { KeyId = "demo-smoke-kid" };

    /// <summary>
    /// Boots the Demo stack from an empty PostgreSQL 18 database, verifies <c>/healthz/ready</c> and <c>/health/ready</c>
    /// report <c>200 Healthy</c> once migrations and <c>CADENCE-101</c> seeding finish, authenticates <c>crc.site101</c>
    /// through <c>Cadence.Apps.Gateway</c>, executes a read/write/read-back cycle on <c>CADENCE-101</c>, and confirms
    /// idempotent restart behavior.
    /// </summary>
    [Fact(DisplayName = "DemoBootToSeed_MigratesSeedsReportsReady_AndExecutesGatewayReadWriteFlow")]
    public async Task DemoBootToSeedMigratesSeedsReportsReadyAndExecutesGatewayReadWriteFlow()
    {
        var ct = TestContext.Current.CancellationToken;
        var collectedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        await using (var kernelHost = CreateDemoKernelHost())
        {
            using var anonymousKernelClient = kernelHost.CreateClient();

            // 1. Verify Demo configuration defaults were applied and readiness probes return 200 Healthy after migrations + seeding.
            var kernelConfig = kernelHost.Services.GetRequiredService<IConfiguration>();
            kernelConfig["Database:MigrateOnStartup"].Should().Be("True");
            kernelConfig["Kernel:DevelopmentTenant:Code"].Should().Be(DemoTenantCode);
            kernelConfig["Kernel:DevelopmentTenant:DemoData"].Should().Be("True");

            using (var readyResponse = await anonymousKernelClient.GetAsync(new Uri("/healthz/ready", UriKind.Relative), ct))
            {
                string body = await readyResponse.Content.ReadAsStringAsync(ct);
                readyResponse.StatusCode.Should().Be(HttpStatusCode.OK, body);
                body.Should().Be("Healthy");
            }

            using (var readyAliasResponse = await anonymousKernelClient.GetAsync(new Uri("/health/ready", UriKind.Relative), ct))
            {
                string body = await readyAliasResponse.Content.ReadAsStringAsync(ct);
                readyAliasResponse.StatusCode.Should().Be(HttpStatusCode.OK, body);
                body.Should().Be("Healthy");
            }

            // 2. Boot Gateway in Demo mode forwarding to Kernel TestServer and verify auth + readiness.
            await using var gatewayHost = CreateDemoGatewayHost(kernelHost.Server);
            using var anonymousGatewayClient = gatewayHost.CreateClient();

            using (var gatewayReady = await anonymousGatewayClient.GetAsync(new Uri("/healthz/ready", UriKind.Relative), ct))
            {
                gatewayReady.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using (var gatewayReadyAlias = await anonymousGatewayClient.GetAsync(new Uri("/health/ready", UriKind.Relative), ct))
            {
                gatewayReadyAlias.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using (var unauthenticated = await anonymousGatewayClient.GetAsync(new Uri("/api/v1/kernel/search?q=CADENCE-101", UriKind.Relative), ct))
            {
                unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }

            // 3. Log in as seeded demo persona crc.site101 using a signed OIDC JWT through Gateway.
            using var crcGatewayClient = gatewayHost.CreateClient();
            crcGatewayClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", MintDemoPersonaJwt(CrcSubjectId, CrcUsername, CrcRealmRoles, DemoTenantCode));

            // 4. Read seeded CADENCE-101 study, SITE-101, and SUBJ-101-001 through Gateway -> Kernel.
            var searchHits = await GetJsonAsync(crcGatewayClient, "/api/v1/kernel/search?q=CADENCE-101", ct);
            string studyRef = searchHits.EnumerateArray()
                .Should().ContainSingle(hit => hit.GetProperty("code").GetString() == "CADENCE-101")
                .Subject.GetProperty("ref").GetString()!;

            var studySites = (await GetJsonAsync(crcGatewayClient, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(studyRef)}/children", ct))
                .EnumerateArray()
                .Select(child => child.GetProperty("node"))
                .Where(node => node.GetProperty("kind").GetString() == "study_site")
                .ToList();
            studySites.Should().HaveCount(2);
            string site101Ref = studySites
                .Should().ContainSingle(node => node.GetProperty("mask").ValueKind == JsonValueKind.Null)
                .Subject.GetProperty("ref").GetString()!;

            var site101Children = (await GetJsonAsync(crcGatewayClient, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(site101Ref)}/children", ct))
                .EnumerateArray()
                .Select(child => child.GetProperty("node"))
                .ToList();
            string enrollmentRef = site101Children
                .Should().ContainSingle(node => node.GetProperty("kind").GetString() == "enrollment" && node.GetProperty("code").GetString() == "SUBJ-101-001")
                .Subject.GetProperty("ref").GetString()!;
            Guid enrollmentId = NodeRef.Parse(enrollmentRef).Id;

            // 5. Write 1: Open scheduled screening vitals eCRF form on SUBJ-101-001 through Gateway -> Execution module.
            Guid openedFormId;
            using (var openFormRequest = CreateMutationRequest(
                HttpMethod.Post,
                "/api/v1/execution/forms",
                "Boot-to-seed smoke test screening vitals",
                new
                {
                    enrollment_id = enrollmentId,
                    collection_spec_id = "cs.vitals",
                    planned_activity_id = "act.screening",
                    collected_at = collectedAt,
                }))
            using (var openFormResponse = await crcGatewayClient.SendAsync(openFormRequest, ct))
            {
                string openFormBody = await openFormResponse.Content.ReadAsStringAsync(ct);
                openFormResponse.StatusCode.Should().Be(HttpStatusCode.Created, openFormBody);
                using var doc = JsonDocument.Parse(openFormBody);
                openedFormId = doc.RootElement.GetProperty("form_id").GetGuid();
                doc.RootElement.GetProperty("collection_spec_id").GetString().Should().Be("cs.vitals");
            }

            // 6. Write 2: Screen a new participant at SITE-101 through Gateway -> Kernel command pipeline.
            using (var screenRequest = CreateMutationRequest(
                HttpMethod.Post,
                "/api/v1/kernel/enrollments/screen",
                "Boot-to-seed smoke test participant screening",
                new
                {
                    study_code = "CADENCE-101",
                    site_code = "SITE-101",
                    subject_code = "SUBJ-101-050",
                    screened_at = collectedAt,
                }))
            using (var screenResponse = await crcGatewayClient.SendAsync(screenRequest, ct))
            {
                string screenBody = await screenResponse.Content.ReadAsStringAsync(ct);
                screenResponse.StatusCode.Should().Be(HttpStatusCode.Created, screenBody);
            }

            // 7. Read back both writes through Gateway to verify persistence in PostgreSQL.
            var persistedForm = await GetJsonAsync(crcGatewayClient, $"/api/v1/execution/forms/{openedFormId:D}", ct);
            persistedForm.GetProperty("form_id").GetGuid().Should().Be(openedFormId);
            persistedForm.GetProperty("enrollment_id").GetGuid().Should().Be(enrollmentId);
            persistedForm.GetProperty("collection_spec_id").GetString().Should().Be("cs.vitals");

            var listedForms = await GetJsonAsync(crcGatewayClient, $"/api/v1/execution/forms?enrollment_id={enrollmentId:D}", ct);
            listedForms.EnumerateArray()
                .Should().ContainSingle(f => f.GetProperty("form_id").GetGuid() == openedFormId);

            var updatedSiteChildren = (await GetJsonAsync(crcGatewayClient, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(site101Ref)}/children", ct))
                .EnumerateArray()
                .Select(child => child.GetProperty("node"))
                .ToList();
            updatedSiteChildren.Should().ContainSingle(
                node => node.GetProperty("kind").GetString() == "enrollment"
                    && node.GetProperty("code").GetString() == "SUBJ-101-050");
        }

        // 8. Rebooting Demo host against the already-migrated/seeded database is idempotent and stays 200 Healthy.
        await using (var restartedKernelHost = CreateDemoKernelHost())
        {
            using var restartedClient = restartedKernelHost.CreateClient();
            using var ready = await restartedClient.GetAsync(new Uri("/healthz/ready", UriKind.Relative), ct);
            ready.StatusCode.Should().Be(HttpStatusCode.OK, await ready.Content.ReadAsStringAsync(ct));
        }

        await using var verifyConnection = new NpgsqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync(ct);
        await using var tenantCountCmd = new NpgsqlCommand(
            "SELECT count(*) FROM kernel.tenants WHERE code = @code AND is_active",
            verifyConnection);
        tenantCountCmd.Parameters.AddWithValue("code", DemoTenantCode);
        (await tenantCountCmd.ExecuteScalarAsync(ct)).Should().Be(1L);
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateDemoKernelHost() =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Demo")
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString));

    private static WebApplicationFactory<Cadence.Apps.Gateway.Program> CreateDemoGatewayHost(TestServer kernelServer) =>
        new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(builder =>
        {
            builder
                .UseEnvironment("Demo")
                .UseSetting("OIDC_AUTHORITY", OidcIssuer)
                .UseSetting("OIDC_AUDIENCE", OidcAudience)
                .UseTestGatewaySecret();

            foreach (var service in ServiceTopology.Services)
            {
                builder.UseSetting(service.UrlVariable, DownstreamKernelBaseUrl);
            }

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var oidcConfig = new OpenIdConnectConfiguration { Issuer = OidcIssuer };
                    oidcConfig.SigningKeys.Add(OidcSigningKey);
                    options.Configuration = oidcConfig;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidcConfig);
                });

                services.AddHttpClient(GatewayTenantEntitlementCache.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => kernelServer.CreateHandler());

                services.RemoveAll<IForwarderHttpClientFactory>();
                services.AddSingleton<IForwarderHttpClientFactory>(new TestServerForwarderHttpClientFactory(kernelServer));
            });
        });

    private static string MintDemoPersonaJwt(
        string subjectId,
        string username,
        IReadOnlyList<string> realmRoles,
        string tenantCode)
    {
        var claims = new Dictionary<string, object>
        {
            ["preferred_username"] = username,
            ["tenant_id"] = tenantCode,
            ["realm_access"] = new Dictionary<string, object>
            {
                ["roles"] = realmRoles,
            },
        };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = OidcIssuer,
            Audience = OidcAudience,
            Subject = new ClaimsIdentity([new Claim("sub", subjectId)]),
            Claims = claims,
            Expires = SystemClock.Instance.UtcNow.UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(OidcSigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private static HttpRequestMessage CreateMutationRequest(HttpMethod method, string path, string reason, object payload)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        return request;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed class TestServerForwarderHttpClientFactory(TestServer server) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(server.CreateHandler(), disposeHandler: true);
    }
}
