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
/// The kernel tenant a local stack's sign-ins name (ADR-2542): with <c>Kernel:DevelopmentTenant:Code</c> set, the host
/// provisions that tenant on start, if no active tenant has the code yet, through the kernel command pipeline as the
/// <see cref="Operator"/> service account, so the row, its event and its audit entry are written like any other
/// provisioning. Only in the Development environment: anywhere else the host refuses to start with the setting.
/// </summary>
internal sealed partial class DevelopmentTenantBootstrap(
    string code,
    string name,
    IServiceScopeFactory scopes,
    ILogger<DevelopmentTenantBootstrap> logger) : IHostedService
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Kernel:DevelopmentTenant";

    /// <summary>The platform operator that provisions the development tenant; the host names no other.</summary>
    public static readonly Actor Operator = Actor.ServiceAccount("kernel-development-bootstrap");

    private const string Reason = "Provision the local development tenant that the local identity provider's users sign in to (ADR-2542)";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (await services.GetRequiredService<ITenantDirectory>().FindActiveByCodeAsync(code, cancellationToken).ConfigureAwait(false) is not null)
        {
            LogAlreadyProvisioned(logger, code);
            return;
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Registers the bootstrap when <c>Kernel:DevelopmentTenant:Code</c> is set, and returns the platform operators the
    /// authority evaluator needs for it (none otherwise). Throws outside the Development environment.
    /// </summary>
    public static string[] Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(SectionName);
        if (section["Code"] is not { Length: > 0 } code)
        {
            return [];
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{SectionName}:Code is set in the {environment.EnvironmentName} environment. The kernel host provisions a development tenant only in Development (ADR-2542).");
        }

        string name = section["Name"] is { Length: > 0 } configured ? configured : $"Local development ({code})";
        services.AddHostedService(sp => new DevelopmentTenantBootstrap(
            code,
            name,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<DevelopmentTenantBootstrap>>()));
        return [Operator.Id];
    }

    [LoggerMessage(EventId = 2542, Level = LogLevel.Information, Message = "The development tenant {Code} is already provisioned")]
    private static partial void LogAlreadyProvisioned(ILogger logger, string code);

    [LoggerMessage(EventId = 2543, Level = LogLevel.Information, Message = "Provisioned the development tenant {Code} ({TenantId})")]
    private static partial void LogProvisioned(ILogger logger, string code, TenantId tenantId);
}

/// <summary>
/// The service account a host-internal unit of work acts as, set only by host code in its own DI scope (the
/// development tenant bootstrap). Request scopes never set it, so a request's actor still comes only from the verified
/// caller identity (<see cref="ClaimsCurrentActor"/>).
/// </summary>
internal sealed class ServiceActorScope
{
    public Actor? Actor { get; set; }
}
