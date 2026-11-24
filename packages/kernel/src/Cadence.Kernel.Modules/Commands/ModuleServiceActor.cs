using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Scoped service-account actor override for module-hosted background jobs (ADR-2600, K-TX-5). When a module background
/// job opens a DI scope for a scheduled pass, it calls <see cref="ActAsServiceAccount"/> so commands sent through
/// <see cref="IKernelCommandSender"/> execute under that dedicated <c>svc:&lt;name&gt;</c> service account without coupling
/// the module to host-internal types (K-XS-2).
/// </summary>
public sealed class ModuleServiceActor
{
    /// <summary>The service account acting in the current DI scope, or <see langword="null"/> outside a module service scope.</summary>
    public Actor? Actor { get; private set; }

    /// <summary>Binds this scope to act as <c>svc:&lt;serviceAccountName&gt;</c> and returns the resolved <see cref="Commands.Actor"/>.</summary>
    public Actor ActAsServiceAccount(string serviceAccountName)
    {
        var serviceActor = Commands.Actor.ServiceAccount(serviceAccountName);
        Actor = serviceActor;
        return serviceActor;
    }
}
