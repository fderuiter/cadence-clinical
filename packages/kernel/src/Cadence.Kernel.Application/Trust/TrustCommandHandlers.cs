using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Events;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Trust;

/// <summary>
/// Handles <see cref="Attest"/>: checks the signer, the freshness of their authentication and that the target's
/// current content has the attested hash, then records the attestation.
/// </summary>
internal sealed class AttestHandler : ICommandHandler<Attest, AttestationId>
{
    public async Task<CommandResult<AttestationId>> HandleAsync(Attest command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Attestation>(command.AttestationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (command.Target is null)
        {
            return TrustErrors.TargetNotSignable;
        }

        if (await CheckTargetAsync(command.Target, context, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var signed = Attestation.Sign(
            command.AttestationId,
            command.TenantId,
            context.Actor,
            command.Meaning,
            command.Target,
            command.AuthenticatedAt,
            command.Method,
            context.RecordedAt,
            command.ReasonForChange);
        if (signed.Value is not { } attestation)
        {
            return signed.Error!;
        }

        context.Work.Add(attestation);
        context.Work.Raise(new AttestationRecorded(attestation.TenantId, attestation.Id, attestation.ActorId, attestation.Meaning, attestation.Target));
        return CommandResult<AttestationId>.Success(attestation.Id);
    }

    /// <summary>
    /// The kinds of record that can be signed, and how to read the content hash each one has now. A protocol version is
    /// signable once its content is frozen for review; a standard version's or library item's content never changes
    /// after it is recorded; an enrollment is signed in its current state, which every change of status changes.
    /// </summary>
    private static async Task<KernelError?> CheckTargetAsync(AttestationTarget target, ICommandContext context, CancellationToken cancellationToken)
    {
        AttestationTarget? current;
        switch (target.Kind)
        {
            case AttestationTarget.ProtocolVersionKind:
                var version = await context.Work.ReadAsync<ProtocolVersion>(ProtocolVersionId.FromGuid(target.Id), cancellationToken).ConfigureAwait(false);
                if (version is null)
                {
                    return TrustErrors.TargetUnknown;
                }

                current = version.SignableTarget;
                break;
            case NodeKinds.StandardVersion:
                var standard = await context.Work.ReadAsync<StandardVersion>(StandardVersionId.FromGuid(target.Id), cancellationToken).ConfigureAwait(false);
                if (standard is null)
                {
                    return TrustErrors.TargetUnknown;
                }

                current = target with { ContentHash = standard.ContentHash };
                break;
            case NodeKinds.LibraryItem:
                var item = await context.Work.ReadAsync<LibraryItem>(LibraryItemId.FromGuid(target.Id), cancellationToken).ConfigureAwait(false);
                if (item is null)
                {
                    return TrustErrors.TargetUnknown;
                }

                current = target with { ContentHash = item.ContentHash };
                break;
            case NodeKinds.Enrollment:
                var enrollment = await context.Work.ReadAsync<Enrollment>(EnrollmentId.FromGuid(target.Id), cancellationToken).ConfigureAwait(false);
                if (enrollment is null)
                {
                    return TrustErrors.TargetUnknown;
                }

                current = enrollment.SignableTarget;
                break;
            default:
                return TrustErrors.TargetKindUnsupported.WithMessage($"Attestations cannot target '{target.Kind}' records.");
        }

        if (current is null)
        {
            return TrustErrors.TargetNotSignable;
        }

        return string.Equals(current.ContentHash, target.ContentHash, StringComparison.Ordinal) ? null : TrustErrors.TargetContentChanged;
    }
}

/// <summary>Registers the trust command handlers (spec §9.5).</summary>
public static class TrustServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelTrustHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<Attest, AttestationId>, AttestHandler>();
        return services;
    }
}
