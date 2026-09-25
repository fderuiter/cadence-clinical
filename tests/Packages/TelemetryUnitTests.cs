using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Telemetry;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadenceTelemetry_RegistersOpenTelemetryServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCadenceTelemetry();

        var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetService<TracerProvider>();
        var meterProvider = provider.GetService<MeterProvider>();

        tracerProvider.Should().NotBeNull();
        meterProvider.Should().NotBeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadenceWebDefaults_RegistersLoggingTelemetryAndOpenApi()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCadenceWebDefaults();

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<JsonConsoleFormatterOptions>>().Value;
        options.IncludeScopes.Should().BeTrue();

        var tracerProvider = app.Services.GetService<TracerProvider>();
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
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

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
        act5.Should().Throw<ArgumentNullException>();
        act6.Should().Throw<ArgumentNullException>();
    }
}
