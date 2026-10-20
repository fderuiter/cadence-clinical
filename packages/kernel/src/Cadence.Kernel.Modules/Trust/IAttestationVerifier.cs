using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Checks that a recorded attestation (spec §9.5) authorizes a governed action: it exists in the tenant, the acting
/// person signed it, it asserts the required meaning, and it is bound to the exact record and content hash, so a
/// signed record cannot silently change (21 CFR 11.70). Protocol approval and withdrawal, standards and library
/// governance, and later locks, reviews and consent call it before changing state. Kernel persistence implements it
/// over the attestation store (<c>AddKernelPersistence</c>).
/// </summary>
public interface IAttestationVerifier
{
    ValueTask<AttestationVerification> VerifyAsync(AttestationRequirement requirement, CancellationToken cancellationToken);
}

/// <summary>What an attestation must satisfy for a governed action.</summary>
/// <param name="Tenant">The tenant of the command; an attestation from another tenant is not found.</param>
/// <param name="AttestationId">The attestation the command names.</param>
/// <param name="Meaning">The meaning it must assert.</param>
/// <param name="Target">The record and content hash it must sign.</param>
/// <param name="Signer">The actor executing the command, who must be the person who signed.</param>
public sealed record AttestationRequirement(
    TenantId Tenant,
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    AttestationTarget Target,
    Actor Signer);

/// <summary>Whether a requirement is met and, when it is not, why (K-AUTH-6).</summary>
public sealed record AttestationVerification(bool Satisfied, string? Reason)
{
    public static AttestationVerification Met { get; } = new(true, null);

    public static AttestationVerification Unmet(string reason) => new(false, reason);
}

/// <summary>
/// Checks that a record of one kind may be signed with the requested target, for kinds outside the kernel core (spec
/// §9.5). Register one per kind with <c>TryAddEnumerable</c>; <see cref="Attest"/> consults it after the core kinds.
/// </summary>
public interface IAttestationTargetCheck
{
    /// <summary>The NodeRef kind this check signs (<see cref="AttestationTarget.Kind"/>).</summary>
    string Kind { get; }

    /// <summary>
    /// Null when <paramref name="target"/> may be signed now; otherwise <see cref="TrustErrors.TargetUnknown"/>,
    /// <see cref="TrustErrors.TargetNotSignable"/> or <see cref="TrustErrors.TargetContentChanged"/>.
    /// </summary>
    Task<KernelError?> CheckAsync(AttestationTarget target, ICommandContext context, CancellationToken cancellationToken);
}

/// <summary>Registers a module's attestation target checks (spec §9.5, ADR-2631).</summary>
public static class ModuleAttestationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TCheck"/>, so the kernel's <see cref="Attest"/> can sign records of its kind and
    /// a module command can require such a signature through <see cref="IAttestationVerifier"/>.
    /// </summary>
    /// <typeparam name="TCheck">The check for one module record kind.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddModuleAttestationTarget<TCheck>(this IServiceCollection services)
        where TCheck : class, IAttestationTargetCheck
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IAttestationTargetCheck, TCheck>());
        return services;
    }
}
