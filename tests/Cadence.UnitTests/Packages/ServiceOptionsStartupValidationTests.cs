using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Workers;
using Cadence.Apps.Gateway;
using Cadence.Apps.Gateway.Signatures;
using Cadence.Database;
using Cadence.Kernel;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Tenancy;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Providers;
using Cadence.Modules.Intelligence.Sandbox;
using Cadence.Storage;
using Cadence.Telemetry;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.UnitTests.Gateway;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Verifies that backend service and shared-package options enforce <c>DataAnnotations</c>, custom validators, and
/// <c>ValidateOnStart()</c> during both <see cref="StartupValidationFactory{TEntryPoint}.ValidateStartup"/> and
/// <see cref="IHost.StartAsync"/>, block default <see cref="HttpClient"/> egress inside <see cref="AgentSandbox"/>,
/// and preserve the zero-CORS-header posture when <c>Cors:AllowedOrigins</c> is empty.
/// </summary>
public sealed class ServiceOptionsStartupValidationTests
{
    private static bool IsOptionsValidationFailure(Exception? ex) =>
        ex is OptionsValidationException
        || (ex is AggregateException agg && agg.Flatten().InnerExceptions.Any(e => e is OptionsValidationException))
        || (ex?.InnerException is not null && IsOptionsValidationFailure(ex.InnerException));

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task FileshareWorkerOptions_InvalidRetryCountAndNegativeIntervals_FailStartupWithOptionsValidationException()
    {
        using var valid = CreateFileshareValidationFactory(_ => { });
        valid.ValidateStartup().Should().BeNull();

        using var invalidFactory = CreateFileshareValidationFactory(webBuilder =>
        {
            webBuilder.UseSetting("FileshareWorkers:MaxRetryCount", "0");
            webBuilder.UseSetting("FileshareWorkers:TranscodePollInterval", "-00:00:10");
            webBuilder.UseSetting("FileshareWorkers:InitialBackoff", "-00:00:05");
        });

        Exception? validationEx = invalidFactory.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain(nameof(FileshareWorkerOptions.MaxRetryCount))
            .And.Contain(nameof(FileshareWorkerOptions.TranscodePollInterval))
            .And.Contain(nameof(FileshareWorkerOptions.InitialBackoff));

        using var host = BuildHostWithServices(
            new Dictionary<string, string?>
            {
                ["FileshareWorkers:BatchSize"] = "0",
                ["FileshareWorkers:MaxConcurrentJobs"] = "0",
                ["FileshareWorkers:MaxRetryCount"] = "0",
                ["FileshareWorkers:TranscodePollInterval"] = "-00:00:10",
                ["FileshareWorkers:ConversionPollInterval"] = "00:00:00",
                ["FileshareWorkers:InitialBackoff"] = "-00:00:05",
            },
            (services, configuration, _) =>
            {
                services.AddOptions<FileshareWorkerOptions>()
                    .Bind(configuration.GetSection(FileshareWorkerOptions.SectionName))
                    .ValidateDataAnnotations()
                    .Validate(o => o.TranscodePollInterval > TimeSpan.Zero, "FileshareWorkers:TranscodePollInterval must be greater than zero.")
                    .Validate(o => o.ConversionPollInterval > TimeSpan.Zero, "FileshareWorkers:ConversionPollInterval must be greater than zero.")
                    .Validate(o => o.InitialBackoff > TimeSpan.Zero, "FileshareWorkers:InitialBackoff must be greater than zero.")
                    .ValidateOnStart();
            });

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task InferenceOptions_InvalidModeAndNegativeProviderTimeout_FailStartupWithOptionsValidationException()
    {
        using var valid = CreateKernelValidationFactory(_ => { });
        valid.ValidateStartup().Should().BeNull();

        var invalidSettings = new Dictionary<string, string?>
        {
            ["Intelligence:Inference:Mode"] = "Local_Only",
            ["Intelligence:Inference:Providers:Bedrock:Region"] = "eu-central-1",
            ["Intelligence:Inference:Providers:Bedrock:Timeout"] = "-00:05:00",
        };

        using var invalidFactory = CreateKernelValidationFactory(webBuilder => ApplySettings(webBuilder, invalidSettings));
        Exception? validationEx = invalidFactory.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain("Intelligence:Inference:Mode")
            .And.Contain("Intelligence:Inference:Providers:Bedrock:Timeout");

        using var host = BuildHostWithServices(
            invalidSettings,
            (services, configuration, environment) => services.AddIntelligenceInference(configuration, environment));

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData("Intelligence:Inference:Providers:AnthropicAws:Region", "eu-west-1", "WorkspaceId")]
    [InlineData("Intelligence:Inference:Providers:Bedrock:Region", "", "Region")]
    [InlineData("Intelligence:Inference:Providers:Vertex:ProjectId", "proj-1", "Region")]
    [InlineData("Intelligence:Inference:Providers:Foundry:Resource", "", "Resource")]
    [InlineData("Intelligence:Inference:Providers:OpenAiCompatible:Endpoint", "https://models.example.com/v1", "inside the deployment")]
    public async Task InferenceOptions_InvalidNestedProviders_FailStartupWithOptionsValidationException(
        string key,
        string value,
        string expectedSubstring)
    {
        var settings = new Dictionary<string, string?> { [key] = value };

        using var invalidFactory = CreateKernelValidationFactory(webBuilder => ApplySettings(webBuilder, settings));
        Exception? validationEx = invalidFactory.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain(expectedSubstring);

        using var host = BuildHostWithServices(
            settings,
            (services, configuration, environment) => services.AddIntelligenceInference(configuration, environment));

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task SandboxEgressOptions_BlankEntriesFailStartup_AndDefaultHttpClientIsBlockedInsideAgentSandbox()
    {
        var blankSettings = new Dictionary<string, string?>
        {
            ["Intelligence:Sandbox:AllowedClients:0"] = "",
            ["Intelligence:Sandbox:AllowedClients:1"] = "   ",
        };

        using var invalidFactory = CreateKernelValidationFactory(webBuilder => ApplySettings(webBuilder, blankSettings));
        Exception? validationEx = invalidFactory.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain(nameof(SandboxEgressOptions.AllowedClients));

        using var host = BuildHostWithServices(
            blankSettings,
            (services, configuration, environment) => services.AddIntelligenceInference(configuration, environment));

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();

        // Even if SandboxEgressOptions is constructed in memory with Options.DefaultName ("") and whitespace names,
        // SandboxEgressConfiguration must still block default HttpClient and whitespace client egress in AgentSandbox.
        var recording = new StubMessageHandler();
        var sandboxServices = new ServiceCollection();
        sandboxServices.AddSingleton<IOptions<SandboxEgressOptions>>(
            Options.Create(new SandboxEgressOptions { AllowedClients = [Options.DefaultName, "   "] }));
        sandboxServices.AddHttpClient(Options.DefaultName).ConfigurePrimaryHttpMessageHandler(() => recording);
        sandboxServices.AddHttpClient("   ").ConfigurePrimaryHttpMessageHandler(() => recording);
        sandboxServices.AddSingleton<IConfigureOptions<Microsoft.Extensions.Http.HttpClientFactoryOptions>, SandboxEgressConfiguration>();

        await using var provider = sandboxServices.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var ct = TestContext.Current.CancellationToken;

        using var sandbox = AgentSandbox.Enter(TenantId.New(), Guid.NewGuid());
        Func<Task> defaultClientCall = () => clientFactory.CreateClient().GetAsync(new Uri("https://untrusted.example.org/"), ct);
        Func<Task> whitespaceClientCall = () => clientFactory.CreateClient("   ").GetAsync(new Uri("https://untrusted.example.org/"), ct);

        await defaultClientCall.Should().ThrowAsync<InferenceEgressRefusedException>();
        await whitespaceClientCall.Should().ThrowAsync<InferenceEgressRefusedException>();
        sandbox.EgressRefusals.Should().Be(2);
        recording.Calls.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task KeycloakAdminOptions_AllowsUnconfiguredSection_AndRejectsMalformedBaseUrlOrBlankCredentials()
    {
        using var unconfiguredKernel = CreateKernelValidationFactory(_ => { });
        unconfiguredKernel.ValidateStartup().Should().BeNull();

        using var validConfiguredKernel = CreateKernelValidationFactory(webBuilder =>
        {
            webBuilder.UseSetting("KeycloakAdmin:BaseUrl", "https://keycloak.cadence.example");
            webBuilder.UseSetting("KeycloakAdmin:Realm", "cadence");
            webBuilder.UseSetting("KeycloakAdmin:AdminRealm", "master");
            webBuilder.UseSetting("KeycloakAdmin:ClientId", "admin-cli");
            webBuilder.UseSetting("KeycloakAdmin:AdminUsername", "admin");
            webBuilder.UseSetting("KeycloakAdmin:AdminPassword", "test-admin-secret"); // pragma: allowlist secret
        });
        validConfiguredKernel.ValidateStartup().Should().BeNull();

        var invalidSettings = new Dictionary<string, string?>
        {
            ["KeycloakAdmin:BaseUrl"] = "not-a-valid-url",
            ["KeycloakAdmin:Realm"] = "",
            ["KeycloakAdmin:AdminPassword"] = "",
        };

        using var invalidFactory = CreateKernelValidationFactory(webBuilder => ApplySettings(webBuilder, invalidSettings));
        Exception? validationEx = invalidFactory.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain("Realm")
            .And.Contain("KeycloakAdmin:BaseUrl")
            .And.Contain("KeycloakAdmin:AdminPassword");

        using var host = BuildHostWithServices(
            invalidSettings,
            (services, configuration, _) => services.AddKeycloakOnboarding(configuration));

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task SignatureCredentialOptions_MalformedTokenUriAndBlankClientId_FailStartupWithOptionsValidationException()
    {
        using var validGateway = CreateGatewayValidationFactory(_ => { });
        validGateway.ValidateStartup().Should().BeNull();

        using var invalidGateway = CreateGatewayValidationFactory(webBuilder =>
        {
            webBuilder.UseSetting("OIDC_TOKEN_ENDPOINT", "not-a-valid-uri");
            webBuilder.UseSetting("OIDC_SIGNATURE_CLIENT_ID", "   ");
        });

        Exception? validationEx = invalidGateway.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain(nameof(SignatureCredentialOptions.TokenEndpoint))
            .And.Contain(nameof(SignatureCredentialOptions.ClientId));

        using var host = BuildHostWithServices(
            new Dictionary<string, string?>
            {
                ["OIDC_TOKEN_ENDPOINT"] = "not-a-valid-uri",
                ["OIDC_SIGNATURE_CLIENT_ID"] = "   ",
            },
            (services, configuration, _) =>
            {
                services.AddOptions<SignatureCredentialOptions>()
                    .Configure(options =>
                    {
                        options.TokenEndpoint = SignatureCredentialOptions.TryResolveTokenEndpoint(
                            configuration["OIDC_TOKEN_ENDPOINT"],
                            metadataAddress: null,
                            authority: "https://keycloak.test/realms/cadence",
                            out var endpoint)
                            ? endpoint
                            : null;
                        options.ClientId = configuration["OIDC_SIGNATURE_CLIENT_ID"] is { Length: > 0 } clientId
                            ? clientId
                            : SignatureCredentialOptions.DefaultClientId;
                    })
                    .ValidateDataAnnotations()
                    .Validate(
                        o => o.TokenEndpoint is { IsAbsoluteUri: true } uri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                        "OIDC_TOKEN_ENDPOINT must resolve to an absolute http or https URI.")
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ClientId),
                        "OIDC_SIGNATURE_CLIENT_ID must not be empty or whitespace.")
                    .ValidateOnStart();
            });

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData(GatewayClusterOptions.ProxyTimeoutSecondsKey, "0")]
    [InlineData(GatewayClusterOptions.ProxyTimeoutSecondsKey, "-5")]
    [InlineData(GatewayClusterOptions.HealthCheckIntervalSecondsKey, "0")]
    [InlineData(GatewayClusterOptions.HealthCheckTimeoutSecondsKey, "-1")]
    [InlineData(GatewayRateLimiting.PermitsPerMinuteKey, "0")]
    [InlineData(GatewayRateLimiting.PermitsPerMinuteKey, "-10")]
    public async Task GatewayClusterAndRateLimitOptions_NonPositiveSettings_FailStartupWithOptionsValidationException(
        string key,
        string invalidValue)
    {
        using var invalidGateway = CreateGatewayValidationFactory(webBuilder => webBuilder.UseSetting(key, invalidValue));
        Exception? validationEx = invalidGateway.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();

        using var host = BuildHostWithServices(
            new Dictionary<string, string?> { [key] = invalidValue },
            (services, configuration, _) =>
            {
                services.AddGatewayClusterOptions(configuration);
                services.AddGatewayRateLimiting(configuration);
            });

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task StorageOptions_FileSystemAndS3Bindings_EnforceStartupOptionsValidation()
    {
        using var validFileSystem = CreateEtmfValidationFactory(_ => { });
        validFileSystem.ValidateStartup().Should().BeNull();

        using var invalidFileSystem = CreateEtmfValidationFactory(webBuilder =>
            webBuilder.ConfigureTestServices(services =>
                services.Configure<FileSystemStorageOptions>(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RootPath"] = "" }).Build())));
        IsOptionsValidationFailure(invalidFileSystem.ValidateStartup()).Should().BeTrue();

        // Mutating configuration or PostConfigure after synchronous registration still fails ValidateOnStart on IHost.StartAsync().
        string validTempRoot = Path.Combine(Path.GetTempPath(), $"cadence-storage-valid-{Guid.NewGuid():N}");
        using var fsHost = BuildHostWithServices(
            new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "filesystem",
                ["Storage:RootPath"] = validTempRoot,
            },
            (services, configuration, environment) =>
            {
                services.AddCadenceStorage(configuration, environment);
                services.AddSingleton<IPostConfigureOptions<FileSystemStorageOptions>>(
                    new DelegatePostConfigure<FileSystemStorageOptions>());
                // Override bound section value via a later Configure registration to invalid relative path:
                services.Configure<FileSystemStorageOptions>(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RootPath"] = "relative/path" }).Build());
            });

        Exception? fsStartEx = await Record.ExceptionAsync(() => fsHost.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(fsStartEx).Should().BeTrue();

        using var s3Factory = new StartupValidationFactory<Cadence.Apps.Etmf.Program>(webBuilder => webBuilder
            .UseEnvironment(Environments.Production)
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseSetting("Storage:Provider", "s3")
            .UseSetting("Storage:BucketName", "cadence-valid-bucket")
            .ConfigureTestServices(services =>
                services.Configure<S3StorageOptions>(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BucketName"] = "",
                        ["AccessKeyId"] = "key-without-secret",
                    }).Build())));

        IsOptionsValidationFailure(s3Factory.ValidateStartup()).Should().BeTrue();

        using var s3Host = BuildHostWithServices(
            new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "s3",
                ["Storage:BucketName"] = "cadence-valid-bucket",
            },
            (services, configuration, environment) =>
            {
                services.AddCadenceStorage(configuration, environment);
                services.Configure<S3StorageOptions>(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BucketName"] = "",
                    }).Build());
            });

        Exception? s3StartEx = await Record.ExceptionAsync(() => s3Host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(s3StartEx).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task CadencePostgresOptions_BlankConnectionString_FailsStartupWithOptionsValidationException()
    {
        using var invalidFileshare = CreateFileshareValidationFactory(webBuilder =>
            webBuilder.ConfigureTestServices(services =>
                services.Configure<CadencePostgresOptions>(options => options.ConnectionString = "   ")));

        Exception? validationEx = invalidFileshare.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();

        using var host = BuildHostWithServices(
            new Dictionary<string, string?>(),
            (services, configuration, environment) => services.AddCadencePostgresOptions(configuration, environment));

        Exception? startEx = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData("*")]
    [InlineData("https://*.cadence.example")]
    [InlineData("http://localhost:3000")]
    [InlineData("http://portal.cadence.example")]
    [InlineData("")]
    [InlineData("not-an-origin")]
    public async Task CadenceCorsOptions_WildcardNonHttpsOrBlankOrigins_FailStartupWithOptionsValidationException(string invalidOrigin)
    {
        using var invalidGateway = CreateGatewayValidationFactory(webBuilder =>
            webBuilder.UseSetting("Cors:AllowedOrigins:0", invalidOrigin));

        Exception? validationEx = invalidGateway.ValidateStartup();
        IsOptionsValidationFailure(validationEx).Should().BeTrue();
        validationEx!.Message.Should().Contain("Cors:AllowedOrigins");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = invalidOrigin,
        });
        builder.AddCadenceWebDefaults();
        await using var app = builder.Build();

        Exception? startEx = await Record.ExceptionAsync(() => ((IHost)app).StartAsync(TestContext.Current.CancellationToken));
        IsOptionsValidationFailure(startEx).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task CadenceCorsOptions_EmitsNoCorsHeadersWhenEmpty_AndAllowsExplicitHttpsOriginsWhenConfigured()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. Empty Cors:AllowedOrigins preserves zero-CORS-header posture.
        await using (var sameOriginGateway = GatewayResilienceTests.CreateGateway("http://127.0.0.1:1"))
        {
            using var client = sameOriginGateway.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz/live");
            request.Headers.Add("Origin", "https://portal.cadence.example");

            using var response = await client.SendAsync(request, ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
            response.Headers.Contains("Access-Control-Allow-Methods").Should().BeFalse();
            response.Headers.Contains("Access-Control-Allow-Headers").Should().BeFalse();
        }

        // 2. Explicit HTTPS origin in Cors:AllowedOrigins emits Access-Control-Allow-Origin only for that origin.
        await using (var corsGateway = GatewayResilienceTests.CreateGateway(
            "http://127.0.0.1:1",
            ("Cors:AllowedOrigins:0", "https://portal.cadence.example")))
        {
            using var client = corsGateway.CreateClient();

            using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/healthz/live");
            allowedRequest.Headers.Add("Origin", "https://portal.cadence.example");
            using var allowedResponse = await client.SendAsync(allowedRequest, ct);
            allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            allowedResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedHeaders).Should().BeTrue();
            allowedHeaders.Should().ContainSingle().Which.Should().Be("https://portal.cadence.example");

            using var untrustedRequest = new HttpRequestMessage(HttpMethod.Get, "/healthz/live");
            untrustedRequest.Headers.Add("Origin", "https://untrusted.example");
            using var untrustedResponse = await client.SendAsync(untrustedRequest, ct);
            untrustedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            untrustedResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        }
    }

    private static StartupValidationFactory<Cadence.Apps.Fileshare.Program> CreateFileshareValidationFactory(
        Action<IWebHostBuilder> configure) =>
        new(builder =>
        {
            builder
                .UseTestGatewaySecret()
                .UseTestPostgresConnectionString();
            configure(builder);
        });

    private static StartupValidationFactory<Cadence.Kernel.Host.Program> CreateKernelValidationFactory(
        Action<IWebHostBuilder> configure) =>
        new(builder =>
        {
            builder
                .UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestBiometricLivenessKey();
            configure(builder);
        });

    private static StartupValidationFactory<Cadence.Apps.Gateway.Program> CreateGatewayValidationFactory(
        Action<IWebHostBuilder> configure) =>
        new(builder =>
        {
            builder
                .UseEnvironment(Environments.Development)
                .UseSetting("OIDC_AUTHORITY", "https://keycloak.test/realms/cadence")
                .UseSetting("OIDC_AUDIENCE", "cadence-api")
                .UseTestGatewaySecret();
            configure(builder);
        });

    private static StartupValidationFactory<Cadence.Apps.Etmf.Program> CreateEtmfValidationFactory(
        Action<IWebHostBuilder> configure) =>
        new(builder =>
        {
            builder
                .UseEnvironment(Environments.Production)
                .UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage();
            configure(builder);
        });

    private static void ApplySettings(IWebHostBuilder webBuilder, IReadOnlyDictionary<string, string?> settings)
    {
        foreach (var (key, value) in settings)
        {
            webBuilder.UseSetting(key, value);
        }
    }

    private static IHost BuildHostWithServices(
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection, IConfiguration, IHostEnvironment> register) =>
        new HostBuilder()
            .UseEnvironment(Environments.Production)
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) => register(services, context.Configuration, context.HostingEnvironment))
            .Build();

    private sealed class DelegatePostConfigure<TOptions> : IPostConfigureOptions<TOptions>
        where TOptions : class
    {
        public void PostConfigure(string? name, TOptions options)
        {
        }
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
