using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Host;

/// <summary>
/// Tracks whether <see cref="DevelopmentTenantBootstrap"/> has finished provisioning the tenant and seeding demo data.
/// </summary>
internal sealed class DevelopmentTenantBootstrapReadiness
{
    private volatile bool _completed;

    /// <summary>True once startup tenant provisioning and configured demo seeding have completed.</summary>
    public bool IsCompleted => _completed;

    /// <summary>Marks startup tenant provisioning and demo seeding complete.</summary>
    public void MarkCompleted() => _completed = true;
}

/// <summary>
/// The kernel tenant a local or demo stack's sign-ins name (ADR-2542): with <c>Kernel:DevelopmentTenant:Code</c> set, the host
/// provisions that tenant on start, if no active tenant has the code yet, through the kernel command pipeline as the
/// <see cref="Operator"/> service account, so the row, its event and its audit entry are written like any other
/// provisioning. With <c>Kernel:DevelopmentTenant:DemoData</c> also true, it then writes the demo study and the local
/// realm's grants into the tenant (<see cref="DevelopmentDemoData"/>, ADR-2543). Only in the Development or Demo
/// environments: anywhere else the host refuses to start with the setting.
/// </summary>
internal sealed partial class DevelopmentTenantBootstrap(
    string code,
    string name,
    bool demoData,
    DevelopmentTenantBootstrapReadiness readiness,
    IServiceScopeFactory scopes,
    ILogger<DevelopmentTenantBootstrap> logger) : IHostedService
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Kernel:DevelopmentTenant";

    /// <summary>Environment name used by isolated demo and boot-to-seed smoke stacks.</summary>
    public const string DemoEnvironmentName = "Demo";

    /// <summary>The platform operator that provisions the development tenant; the host names no other.</summary>
    public static readonly Actor Operator = Actor.ServiceAccount("kernel-development-bootstrap");

    private const string Reason = "Provision the local development tenant that the local identity provider's users sign in to (ADR-2542)";

    /// <summary>True when <paramref name="environment"/> is <c>Development</c> or <c>Demo</c>.</summary>
    public static bool IsAllowedEnvironment(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsDevelopment() || environment.IsEnvironment(DemoEnvironmentName);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var tenant = await ProvisionAsync(cancellationToken).ConfigureAwait(false);
        if (demoData)
        {
            await DevelopmentDemoData.SeedAsync(tenant, scopes, cancellationToken).ConfigureAwait(false);
            LogDemoData(logger, DevelopmentDemoData.StudyCode, code);
        }

        readiness.MarkCompleted();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<TenantId> ProvisionAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (await services.GetRequiredService<ITenantDirectory>().FindActiveByCodeAsync(code, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            LogAlreadyProvisioned(logger, code);
            return existing;
        }

        services.GetRequiredService<ServiceActorScope>().Actor = Operator;
        var result = await services.GetRequiredService<IKernelCommandSender>().SendAsync(
            new ProvisionTenant(TenantId.New(), code, name, $"development-tenant:{code}", Reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Could not provision the development tenant '{code}': {result.Error?.Message}");
        }

        LogProvisioned(logger, code, result.Value);
        return result.Value;
    }

    /// <summary>
    /// Registers the bootstrap when <c>Kernel:DevelopmentTenant:Code</c> is set, and returns the platform operators the
    /// authority evaluator needs for it (none otherwise). Throws outside the Development and Demo environments.
    /// </summary>
    public static string[] Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(SectionName);
        if (section["Code"] is not { Length: > 0 } code)
        {
            return [];
        }

        if (!IsAllowedEnvironment(environment))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Code is set in the {environment.EnvironmentName} environment. The kernel host provisions a development tenant only in Development or {DemoEnvironmentName} (ADR-2542).");
        }

        string name = section["Name"] is { Length: > 0 } configured ? configured : $"Local development ({code})";
        bool demoData = section.GetValue<bool>("DemoData");
        var readiness = new DevelopmentTenantBootstrapReadiness();
        services.AddSingleton(readiness);
        services.AddHostedService(sp => new DevelopmentTenantBootstrap(
            code,
            name,
            demoData,
            readiness,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<DevelopmentTenantBootstrap>>()));
        return [Operator.Id];
    }

    [LoggerMessage(EventId = 2542, Level = LogLevel.Information, Message = "The development tenant {Code} is already provisioned")]
    private static partial void LogAlreadyProvisioned(ILogger logger, string code);

    [LoggerMessage(EventId = 2543, Level = LogLevel.Information, Message = "Provisioned the development tenant {Code} ({TenantId})")]
    private static partial void LogProvisioned(ILogger logger, string code, TenantId tenantId);

    [LoggerMessage(EventId = 2544, Level = LogLevel.Information, Message = "The demo study {StudyCode} is seeded in the development tenant {Code}")]
    private static partial void LogDemoData(ILogger logger, string studyCode, string code);
}
