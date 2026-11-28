using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Modules;
using Cadence.Telemetry;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class TelemetryUnitTests
{
    [Fact]
    public async Task MapCadenceHealthAndStatusEndpoints_ReturnsExpectedPayloads()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddCadenceWebDefaults();

        using var app = builder.Build();
        app.UseCadenceWebDefaults();
        app.MapCadenceHealthAndStatusEndpoints(
            "Cadence.Apps.Ctms",
            "ctms",
            "CTMS operations health probe.",
            "Returns CTMS operational status.");

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        var healthResponse = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        healthResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var healthJson = await healthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(healthJson))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
            doc.RootElement.GetProperty("service").GetString().Should().Be("Cadence.Apps.Ctms");
        }

        var statusResponse = await client.GetAsync("/api/v1/ctms/status", TestContext.Current.CancellationToken);
        statusResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var statusJson = await statusResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(statusJson))
        {
            doc.RootElement.GetProperty("mode").GetString().Should().Be("Active");
            doc.RootElement.GetProperty("service").GetString().Should().Be("Cadence.Apps.Ctms");
        }

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddCadenceLogging_ConfiguresJsonConsoleOptions()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCadenceLogging();

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<JsonConsoleFormatterOptions>>().Value;

        options.IncludeScopes.Should().BeTrue();
        options.TimestampFormat.Should().Be("yyyy-MM-ddTHH:mm:ss.fffZ");
        options.JsonWriterOptions.Indented.Should().BeFalse();
    }

    [Fact]
    public void AddCadenceTelemetry_RegistersOpenTelemetryServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCadenceTelemetry();

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetService<TracerProvider>();
        var meterProvider = provider.GetService<MeterProvider>();

        tracerProvider.Should().NotBeNull();
        meterProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddCadenceWebDefaults_RegistersLoggingTelemetryOpenApiAndProblemDetails()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCadenceWebDefaults();

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<JsonConsoleFormatterOptions>>().Value;
        options.IncludeScopes.Should().BeTrue();

        var tracerProvider = app.Services.GetService<TracerProvider>();
        tracerProvider.Should().NotBeNull();

        var problemDetailsService = app.Services.GetService<IProblemDetailsService>();
        problemDetailsService.Should().NotBeNull();
    }

    [Fact]
    public void AddCadenceWebDefaults_AllowsCustomProblemDetailsConfiguration()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCadenceWebDefaults(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["testCustom"] = "customValue";
            };
        });

        using var app = builder.Build();
        var problemDetailsService = app.Services.GetService<IProblemDetailsService>();
        problemDetailsService.Should().NotBeNull();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = app.Services;

        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails()
        };

        var problemDetailsOptions = app.Services.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;
        problemDetailsOptions.CustomizeProblemDetails?.Invoke(context);

        context.ProblemDetails.Extensions.Should().ContainKey("testCustom")
            .WhoseValue.Should().Be("customValue");
    }

    [Fact]
    public async Task UseCadenceWebDefaults_AddsExceptionHandler_AndReturnsProblemJsonForUnhandledException()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCadenceWebDefaults();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        using var app = builder.Build();
        app.UseCadenceWebDefaults();

        app.MapGet("/error", () =>
        {
            throw new InvalidOperationException("Test unhandled error response");
        });

        await app.StartAsync(TestContext.Current.CancellationToken);

        var address = app.Urls.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        var response = await client.GetAsync("/error", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(500);
        root.TryGetProperty("title", out _).Should().BeTrue();

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UseCadenceTraceparent_InjectsW3CTraceparentHeaderWhenActivityActive()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCadenceWebDefaults();

        using var app = builder.Build();
        app.UseCadenceTraceparent();
        app.MapGet("/test-trace", () => "ok");

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var activitySource = new ActivitySource("TestActivitySource");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "TestActivitySource",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("TestActivity");
        activity.Should().NotBeNull();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var appBuilder = new ApplicationBuilder(app.Services);
        appBuilder.UseCadenceTraceparent();
        var pipeline = appBuilder.Build();

        // Set Activity.Current explicitly
        Activity.Current = activity;

        await pipeline(context);

        context.Response.Headers.Should().ContainKey("traceparent");
        var headerValue = context.Response.Headers["traceparent"].ToString();
        headerValue.Should().StartWith("00-");
        headerValue.Should().Contain(activity!.TraceId.ToString());
        headerValue.Should().Contain(activity!.SpanId.ToString());

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void MapCadenceApiDocumentation_RoutesConfiguredInDevelopment()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.AddCadenceWebDefaults();

        using var app = builder.Build();
        app.UseCadenceWebDefaults();

        ((IEndpointRouteBuilder)app).DataSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ExtensionMethods_ThrowOnNullArguments()
    {
        WebApplicationBuilder nullBuilder = null!;
        IServiceCollection nullServices = null!;
        IApplicationBuilder nullAppBuilder = null!;
        WebApplication nullApp = null!;
        IEndpointRouteBuilder nullEndpoints = null!;
        IWebHostEnvironment nullEnv = null!;

        Action act1 = () => CadenceTelemetryExtensions.AddCadenceLogging(nullBuilder);
        Action act2 = () => CadenceTelemetryExtensions.AddCadenceTelemetry(nullServices);
        Action act3 = () => CadenceTelemetryExtensions.AddCadenceWebDefaults(nullBuilder);
        Action act4 = () => CadenceTelemetryExtensions.UseCadenceTraceparent(nullAppBuilder);
        Action act5 = () => CadenceTelemetryExtensions.MapCadenceApiDocumentation(nullEndpoints, nullEnv);
        Action act6 = () => CadenceTelemetryExtensions.UseCadenceWebDefaults(nullApp);
        Action act7 = () => CadenceTelemetryExtensions.MapCadenceHealthAndStatusEndpoints(nullApp, "service", "route", "health", "status");
        Action act8 = () => CadenceTelemetryExtensions.AddCadenceOpenApiDocument(nullServices, "kernel");
        Action act9 = () => new ServiceCollection().AddCadenceOpenApiDocument(" ");

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
        act5.Should().Throw<ArgumentNullException>();
        act6.Should().Throw<ArgumentNullException>();
        act7.Should().Throw<ArgumentNullException>();
        act8.Should().Throw<ArgumentNullException>();
        act9.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddCadenceOpenApiDocument_IsolatesGroupedAndUngroupedEndpointsByDocumentName()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddCadenceOpenApiDocument("kernel", includeUngrouped: true);
        builder.Services.AddCadenceOpenApiDocument("intelligence");

        using var app = builder.Build();
        app.MapGet("/healthz", () => Results.Ok());
        app.MapGroup("/api/v1/kernel").WithGroupName("kernel").MapGet("/status", () => Results.Ok());
        app.MapGroup("/api/v1/ai").WithGroupName("intelligence").MapGet("/status", () => Results.Ok());
        app.MapCadenceApiDocumentation(app.Environment);

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        using var kernelDoc = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/kernel.json", TestContext.Current.CancellationToken));
        var kernelPaths = kernelDoc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        kernelPaths.Should().Equal("/healthz", "/api/v1/kernel/status");

        using var intelligenceDoc = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/intelligence.json", TestContext.Current.CancellationToken));
        var intelligencePaths = intelligenceDoc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        intelligencePaths.Should().Equal("/api/v1/ai/status");

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UseCadenceWebDefaults_MapsLivenessAndReadinessProbes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddCadenceWebDefaults();
        builder.Services.AddHealthChecks()
            .AddCheck("database", () => HealthCheckResult.Unhealthy(), tags: [CadenceTelemetryExtensions.ReadinessTag]);

        using var app = builder.Build();
        app.UseCadenceWebDefaults();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        var live = await client.GetAsync("/healthz/live", TestContext.Current.CancellationToken);
        var ready = await client.GetAsync("/healthz/ready", TestContext.Current.CancellationToken);

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var probes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is "/healthz/live" or "/healthz/ready")
            .ToList();
        probes.Should().HaveCount(2);
        probes.Should().OnlyContain(e => e.Metadata.GetMetadata<IAllowAnonymous>() != null);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddCadenceWebDefaults_WithOtlpEndpoint_RegistersProvidersNamedAfterService()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[CadenceTelemetryExtensions.OtlpEndpointKey] = "http://localhost:4317";
        builder.Configuration["OTEL_SERVICE_NAME"] = "cadence-test";

        builder.AddCadenceWebDefaults();

        using var app = builder.Build();
        app.Services.GetService<TracerProvider>().Should().NotBeNull();
        app.Services.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public async Task OpenApiDocuments_FromTrustedProxy_RewriteServersForServiceKernelAndModuleContracts()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.WebHost.UseTestServer();
        builder.Configuration[CadenceHttpHardening.TrustedProxiesKey] = "10.0.0.0/8";
        builder.AddCadenceWebDefaults();
        builder.Services.AddCadenceOpenApiDocument("kernel", includeUngrouped: true);
        builder.Services.AddCadenceModule("sample_module", typeof(TelemetryUnitTests).Assembly);

        using var app = builder.Build();
        app.UseCadenceWebDefaults();
        app.MapGet("/api/v1/service/status", () => Results.Ok());
        app.MapGroup("/api/v1/kernel").WithGroupName("kernel").MapGet("/status", () => Results.Ok());
        app.MapGroup("/api/v1/sample").WithGroupName("sample_module").MapGet("/status", () => Results.Ok());

        await app.StartAsync(TestContext.Current.CancellationToken);
        var server = app.GetTestServer();

        foreach (string documentPath in new[] { "/openapi/v1.json", "/openapi/kernel.json", "/openapi/sample_module.json" })
        {
            var context = await server.SendAsync(http =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse("10.20.30.40");
                http.Request.Method = HttpMethods.Get;
                http.Request.Scheme = "http";
                http.Request.Host = new HostString("internal-host:8080");
                http.Request.Path = documentPath;
                http.Request.Headers["X-Forwarded-Proto"] = "https";
                http.Request.Headers["X-Forwarded-Host"] = "api.example.org";
                http.Request.Headers["X-Forwarded-Prefix"] = "/edge";
            }, TestContext.Current.CancellationToken);

            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            using var doc = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
            var urls = doc.RootElement.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("url").GetString()).ToList();
            urls.Should().Equal("https://api.example.org/edge");
        }

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OpenApiDocuments_FromUntrustedSender_IgnoreSpoofedForwardedHostAndPrefix()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.WebHost.UseTestServer();
        builder.Configuration[CadenceHttpHardening.TrustedProxiesKey] = "10.0.0.0/8";
        builder.AddCadenceWebDefaults();
        builder.Services.AddCadenceModule("sample_module", typeof(TelemetryUnitTests).Assembly);

        using var app = builder.Build();
        app.UseCadenceWebDefaults();
        app.MapGet("/api/v1/service/status", () => Results.Ok());
        app.MapGroup("/api/v1/sample").WithGroupName("sample_module").MapGet("/status", () => Results.Ok());

        await app.StartAsync(TestContext.Current.CancellationToken);
        var server = app.GetTestServer();

        foreach (string documentPath in new[] { "/openapi/v1.json", "/openapi/sample_module.json" })
        {
            var context = await server.SendAsync(http =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.77");
                http.Request.Method = HttpMethods.Get;
                http.Request.Scheme = "http";
                http.Request.Host = new HostString("service.internal:8080");
                http.Request.Path = documentPath;
                http.Request.Headers["X-Forwarded-Proto"] = "https";
                http.Request.Headers["X-Forwarded-Host"] = "spoofed.example.org";
                http.Request.Headers["X-Forwarded-Prefix"] = "/spoofed";
            }, TestContext.Current.CancellationToken);

            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            using var doc = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
            var urls = doc.RootElement.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("url").GetString()).ToList();
            urls.Should().Equal("http://service.internal:8080");
        }

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    [InlineData("https://api.example.org/", "https://api.example.org")]
    public async Task OpenApiDocuments_DuringOfflineBareLocalhostExport_EmitConfiguredOriginOrRelativeRoot(
        string? configuredOrigin,
        string expectedUrl)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.WebHost.UseTestServer();
        if (configuredOrigin is not null)
        {
            builder.Configuration[CadenceHttpHardening.OpenApiPublicOriginKey] = configuredOrigin;
        }

        builder.AddCadenceWebDefaults();
        builder.Services.AddCadenceModule("sample_module", typeof(TelemetryUnitTests).Assembly);

        using var app = builder.Build();
        app.UseCadenceWebDefaults();
        app.MapGet("/api/v1/service/status", () => Results.Ok());
        app.MapGroup("/api/v1/sample").WithGroupName("sample_module").MapGet("/status", () => Results.Ok());

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        foreach (string documentPath in new[] { "/openapi/v1.json", "/openapi/sample_module.json" })
        {
            using var doc = JsonDocument.Parse(
                await client.GetStringAsync(documentPath, TestContext.Current.CancellationToken));
            var urls = doc.RootElement.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("url").GetString()).ToList();
            urls.Should().Equal(expectedUrl);
        }

        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}
