using System;
using System.Security.Cryptography;

using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Persistence.IdentityVault;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence;

/// <summary>Registers IdentityVault persistence.</summary>
public static class IdentityVaultPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vault store and the local key hierarchy (ADR-2458). Call after <c>AddKernelPersistence</c>, together
    /// with <c>AddKernelIdentityVault</c>. There is no default key: a host without one does not register the vault.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="masterKeyBase64">The 256-bit master key, base64-encoded, from the host's secret store.</param>
    /// <param name="region">The residency region this deployment serves, such as <c>EU</c>, or null for none.</param>
    public static IServiceCollection AddKernelIdentityVaultPersistence(this IServiceCollection services, string masterKeyBase64, string? region)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterKeyBase64);
        byte[] master;
        try
        {
            master = Convert.FromBase64String(masterKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("The IdentityVault master key is not base64.", nameof(masterKeyBase64), ex);
        }

        var provider = new LocalVaultKeyProvider(master, region);
        CryptographicOperations.ZeroMemory(master);
        services.TryAddSingleton<IVaultKeyProvider>(provider);
        services.TryAddSingleton<IIdentityVaultStore, KernelIdentityVaultStore>();
        return services;
    }
}
