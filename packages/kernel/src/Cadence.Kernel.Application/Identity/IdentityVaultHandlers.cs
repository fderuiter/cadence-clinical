using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Identity;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Identity;

/// <summary>Checks shared by the IdentityVault handlers.</summary>
internal static class VaultChecks
{
    /// <summary>
    /// The first reason the command cannot touch the subject's identity: the tenant's residency is served by another
    /// vault (spec §13), or the subject is not visible in the tenant.
    /// </summary>
    public static async Task<KernelError?> SubjectAsync(
        ICommandContext context, IdentitySealer sealer, IdentityLayer layer, Guid subjectId, CancellationToken ct)
    {
        var tenant = await context.Work.ReadAsync<Tenant>(context.TenantId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return IdentityErrors.TenantUnknown;
        }

        if (!sealer.Serves(tenant.Residency))
        {
            return IdentityVaultErrors.ResidencyMismatch;
        }

        return await ExistsAsync(context, layer, subjectId, ct).ConfigureAwait(false) ? null : IdentityVaultErrors.SubjectUnknown;
    }

    /// <summary>True when the subject exists and, for a person, has a membership in the tenant (K-ID-4).</summary>
    public static Task<bool> ExistsAsync(ICommandContext context, IdentityLayer layer, Guid subjectId, CancellationToken ct) => layer switch
    {
        IdentityLayer.Person => context.Work.ExistsAsync<Person>(PersonId.FromGuid(subjectId), ct),
        IdentityLayer.SiteLocal => context.Work.ExistsAsync<Participant>(ParticipantId.FromGuid(subjectId), ct),
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown identity layer."),
    };
}

/// <summary>Records sealed details for a person or a participant's site-local identifier.</summary>
internal static class RecordIdentity
{
    public static async Task<KernelError?> HandleAsync(
        ICommandContext context,
        IdentitySealer sealer,
        IIdentityVaultStore store,
        IdentityLayer layer,
        Guid subjectId,
        IdentityDetails details,
        CancellationToken ct)
    {
        if (details is null)
        {
            return IdentityVaultErrors.DetailsRequired;
        }

        if (details.Validate(layer) is { } invalid)
        {
            return invalid;
        }

        if (await VaultChecks.SubjectAsync(context, sealer, layer, subjectId, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        if (await store.FindAsync(context.TenantId, layer, subjectId, ct).ConfigureAwait(false) is { IsErased: true })
        {
            return IdentityVaultErrors.Erased;
        }

        context.Work.StageVaultWrite(new PutIdentityRecord(layer, subjectId, sealer.Seal(context.TenantId, layer, subjectId, details)));
        context.Work.Raise(new IdentityRecorded(context.TenantId, layer, subjectId));
        return null;
    }
}

/// <summary>Handles <see cref="RecordPersonIdentity"/>.</summary>
internal sealed class RecordPersonIdentityHandler(IdentitySealer sealer, IIdentityVaultStore store)
    : ICommandHandler<RecordPersonIdentity, PersonId>
{
    public async Task<CommandResult<PersonId>> HandleAsync(RecordPersonIdentity command, ICommandContext context, CancellationToken cancellationToken) =>
        await RecordIdentity.HandleAsync(context, sealer, store, IdentityLayer.Person, command.PersonId.Value, command.Details, cancellationToken).ConfigureAwait(false) is { } error
            ? error
            : CommandResult<PersonId>.Success(command.PersonId);
}

/// <summary>Handles <see cref="RecordSiteLocalIdentity"/>.</summary>
internal sealed class RecordSiteLocalIdentityHandler(IdentitySealer sealer, IIdentityVaultStore store)
    : ICommandHandler<RecordSiteLocalIdentity, ParticipantId>
{
    public async Task<CommandResult<ParticipantId>> HandleAsync(RecordSiteLocalIdentity command, ICommandContext context, CancellationToken cancellationToken) =>
        await RecordIdentity.HandleAsync(context, sealer, store, IdentityLayer.SiteLocal, command.ParticipantId.Value, command.Details, cancellationToken).ConfigureAwait(false) is { } error
            ? error
            : CommandResult<ParticipantId>.Success(command.ParticipantId);
}

/// <summary>
/// Handles both reveal commands: it records the disclosure (K-IDN-1) when there is something to disclose, and
/// discloses nothing itself. <see cref="IdentityVaultService"/> decrypts after the commit.
/// </summary>
internal sealed class RevealIdentityHandler<TCommand>(IdentitySealer sealer, IIdentityVaultStore store)
    : ICommandHandler<TCommand, IdentityRevealReceipt>
    where TCommand : IIdentityRevealCommand
{
    public async Task<CommandResult<IdentityRevealReceipt>> HandleAsync(TCommand command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await VaultChecks.SubjectAsync(context, sealer, command.Layer, command.SubjectId, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        switch (await store.FindAsync(context.TenantId, command.Layer, command.SubjectId, cancellationToken).ConfigureAwait(false))
        {
            case null:
                return IdentityVaultErrors.NotRecorded;
            case { IsErased: true }:
                return IdentityVaultErrors.Erased;
        }

        context.Work.Raise(new IdentityRevealed(context.TenantId, command.Layer, command.SubjectId));
        return CommandResult<IdentityRevealReceipt>.Success(new IdentityRevealReceipt(command.Layer, command.SubjectId));
    }
}

/// <summary>Crypto-shreds a subject's record in a layer.</summary>
internal static class EraseIdentity
{
    public static async Task<KernelError?> HandleAsync(
        ICommandContext context, IIdentityVaultStore store, IdentityLayer layer, Guid subjectId, CancellationToken ct)
    {
        if (!await VaultChecks.ExistsAsync(context, layer, subjectId, ct).ConfigureAwait(false))
        {
            return IdentityVaultErrors.SubjectUnknown;
        }

        switch (await store.FindAsync(context.TenantId, layer, subjectId, ct).ConfigureAwait(false))
        {
            case null:
                return IdentityVaultErrors.NotRecorded;
            case { IsErased: true }:
                return IdentityVaultErrors.Erased;
        }

        context.Work.StageVaultWrite(new ShredIdentityRecord(layer, subjectId));
        context.Work.Raise(new IdentityErased(context.TenantId, layer, subjectId));
        return null;
    }
}

/// <summary>Handles <see cref="ErasePersonIdentity"/>. Erasure needs no residency match: it only destroys.</summary>
internal sealed class ErasePersonIdentityHandler(IIdentityVaultStore store) : ICommandHandler<ErasePersonIdentity, PersonId>
{
    public async Task<CommandResult<PersonId>> HandleAsync(ErasePersonIdentity command, ICommandContext context, CancellationToken cancellationToken) =>
        await EraseIdentity.HandleAsync(context, store, IdentityLayer.Person, command.PersonId.Value, cancellationToken).ConfigureAwait(false) is { } error
            ? error
            : CommandResult<PersonId>.Success(command.PersonId);
}

/// <summary>Handles <see cref="EraseSiteLocalIdentity"/>.</summary>
internal sealed class EraseSiteLocalIdentityHandler(IIdentityVaultStore store) : ICommandHandler<EraseSiteLocalIdentity, ParticipantId>
{
    public async Task<CommandResult<ParticipantId>> HandleAsync(EraseSiteLocalIdentity command, ICommandContext context, CancellationToken cancellationToken) =>
        await EraseIdentity.HandleAsync(context, store, IdentityLayer.SiteLocal, command.ParticipantId.Value, cancellationToken).ConfigureAwait(false) is { } error
            ? error
            : CommandResult<ParticipantId>.Success(command.ParticipantId);
}

/// <summary>Handles <see cref="LinkParticipantToPerson"/> (K-IDN-2).</summary>
internal sealed class LinkParticipantToPersonHandler(IIdentityVaultStore store) : ICommandHandler<LinkParticipantToPerson, ParticipantId>
{
    public async Task<CommandResult<ParticipantId>> HandleAsync(LinkParticipantToPerson command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!await VaultChecks.ExistsAsync(context, IdentityLayer.Person, command.PersonId.Value, cancellationToken).ConfigureAwait(false)
            || !await VaultChecks.ExistsAsync(context, IdentityLayer.SiteLocal, command.ParticipantId.Value, cancellationToken).ConfigureAwait(false))
        {
            return IdentityVaultErrors.SubjectUnknown;
        }

        if (await store.FindAsync(context.TenantId, IdentityLayer.Person, command.PersonId.Value, cancellationToken).ConfigureAwait(false) is { IsErased: true })
        {
            return IdentityVaultErrors.Erased;
        }

        switch (await store.LinkedPersonAsync(context.TenantId, command.ParticipantId, cancellationToken).ConfigureAwait(false))
        {
            case { } linked when linked == command.PersonId:
                return CommandResult<ParticipantId>.Success(command.ParticipantId);
            case not null:
                return IdentityVaultErrors.AlreadyLinked;
        }

        context.Work.StageVaultWrite(new PutParticipantLink(command.ParticipantId, command.PersonId));
        context.Work.Raise(new ParticipantLinked(context.TenantId, command.ParticipantId));
        return CommandResult<ParticipantId>.Success(command.ParticipantId);
    }
}

/// <summary>Handles <see cref="RevealPersonLinks"/>: records the disclosure; <see cref="IdentityVaultService"/> reads the links after the commit.</summary>
internal sealed class RevealPersonLinksHandler : ICommandHandler<RevealPersonLinks, PersonId>
{
    public async Task<CommandResult<PersonId>> HandleAsync(RevealPersonLinks command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!await VaultChecks.ExistsAsync(context, IdentityLayer.Person, command.PersonId.Value, cancellationToken).ConfigureAwait(false))
        {
            return IdentityVaultErrors.SubjectUnknown;
        }

        context.Work.Raise(new PersonLinksRevealed(context.TenantId, command.PersonId));
        return CommandResult<PersonId>.Success(command.PersonId);
    }
}

/// <summary>
/// <see cref="IIdentityVault"/>: sends the audited reveal command first and decrypts only after it committed, so every
/// disclosure has its audit entry (K-IDN-1) and a denial discloses nothing.
/// </summary>
internal sealed class IdentityVaultService(IKernelCommandSender sender, IIdentityVaultStore store, IdentitySealer sealer) : IIdentityVault
{
    public async Task<CommandResult<IdentityDetails>> RevealAsync(IIdentityRevealCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var receipt = await sender.SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!receipt.IsSuccess)
        {
            return receipt.Error!;
        }

        if (receipt.Replayed)
        {
            return IdentityVaultErrors.RevealReplayed;
        }

        return await store.FindAsync(command.TenantId, command.Layer, command.SubjectId, cancellationToken).ConfigureAwait(false) switch
        {
            null => IdentityVaultErrors.NotRecorded,
            { Sealed: null } => IdentityVaultErrors.Erased,
            { Sealed: { } sealedIdentity } => CommandResult<IdentityDetails>.Success(
                sealer.Unseal(command.TenantId, command.Layer, command.SubjectId, sealedIdentity)),
        };
    }

    public async Task<CommandResult<IReadOnlyList<ParticipantId>>> RevealLinksAsync(RevealPersonLinks command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var receipt = await sender.SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!receipt.IsSuccess)
        {
            return receipt.Error!;
        }

        return receipt.Replayed
            ? IdentityVaultErrors.RevealReplayed
            : CommandResult<IReadOnlyList<ParticipantId>>.Success(
                await store.LinkedParticipantsAsync(command.TenantId, command.PersonId, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Registers the IdentityVault's handlers and <see cref="IIdentityVault"/>.</summary>
public static class IdentityVaultServiceCollectionExtensions
{
    /// <summary>
    /// Registers the IdentityVault commands, <see cref="IdentitySealer"/> and <see cref="IIdentityVault"/> (ADR-2458).
    /// The host must also register an <see cref="IVaultKeyProvider"/> and an <see cref="IIdentityVaultStore"/>, which
    /// <c>AddKernelIdentityVaultPersistence</c> does. Without this call the identity commands have no handler, so a host
    /// that holds no key never touches the vault.
    /// </summary>
    public static IServiceCollection AddKernelIdentityVault(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IdentitySealer>();
        services.AddScoped<IIdentityVault, IdentityVaultService>();
        services.AddTransient<ICommandHandler<RecordPersonIdentity, PersonId>, RecordPersonIdentityHandler>();
        services.AddTransient<ICommandHandler<RecordSiteLocalIdentity, ParticipantId>, RecordSiteLocalIdentityHandler>();
        services.AddTransient<ICommandHandler<RevealPersonIdentity, IdentityRevealReceipt>, RevealIdentityHandler<RevealPersonIdentity>>();
        services.AddTransient<ICommandHandler<RevealSiteLocalIdentity, IdentityRevealReceipt>, RevealIdentityHandler<RevealSiteLocalIdentity>>();
        services.AddTransient<ICommandHandler<ErasePersonIdentity, PersonId>, ErasePersonIdentityHandler>();
        services.AddTransient<ICommandHandler<EraseSiteLocalIdentity, ParticipantId>, EraseSiteLocalIdentityHandler>();
        services.AddTransient<ICommandHandler<LinkParticipantToPerson, ParticipantId>, LinkParticipantToPersonHandler>();
        services.AddTransient<ICommandHandler<RevealPersonLinks, PersonId>, RevealPersonLinksHandler>();
        return services;
    }
}
