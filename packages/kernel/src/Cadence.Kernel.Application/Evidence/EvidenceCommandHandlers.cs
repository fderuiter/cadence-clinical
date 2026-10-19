using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>Handles <see cref="RegisterArtifact"/>.</summary>
internal sealed class RegisterArtifactHandler : ICommandHandler<RegisterArtifact, ArtifactState>
{
    public async Task<CommandResult<ArtifactState>> HandleAsync(RegisterArtifact command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Artifact>(command.ArtifactId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (command.StudyId is { } studyId
            && await context.Work.ReadAsync<Study>(studyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return EvidenceErrors.StudyUnknown;
        }

        var registered = Artifact.Register(
            command.ArtifactId, command.TenantId, command.Kind, command.StudyId, command.Title, command.ArtifactVersionId, command.Content, context.RecordedAt);
        if (registered.Value is not { } artifact)
        {
            return registered.Error!;
        }

        var version = artifact.LatestVersion;
        context.Work.Add(artifact);
        context.Work.Raise(new ArtifactRegistered(
            artifact.TenantId, artifact.Id, version.Id, artifact.Kind, artifact.StudyId, version.ContentHash, version.MediaType, version.SizeBytes));
        return ArtifactStates.Of(artifact);
    }
}

/// <summary>Handles <see cref="AddArtifactVersion"/>.</summary>
internal sealed class AddArtifactVersionHandler : ICommandHandler<AddArtifactVersion, ArtifactState>
{
    public async Task<CommandResult<ArtifactState>> HandleAsync(AddArtifactVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Artifact>(command.ArtifactId, cancellationToken).ConfigureAwait(false) is not { } artifact)
        {
            return KernelErrors.NotFound;
        }

        if (artifact.Version(command.ArtifactVersionId) is not null)
        {
            return KernelErrors.AlreadyExists;
        }

        if (artifact.AddVersion(command.ArtifactVersionId, command.Content, context.RecordedAt) is { } error)
        {
            return error;
        }

        var version = artifact.LatestVersion;
        context.Work.Raise(new ArtifactVersioned(
            artifact.TenantId, artifact.Id, version.Id, version.Number, version.ContentHash, version.MediaType, version.SizeBytes));
        return ArtifactStates.Of(artifact);
    }
}

/// <summary>Shared steps of the evidence handlers.</summary>
internal static class ArtifactStates
{
    public static CommandResult<ArtifactState> Of(Artifact artifact)
    {
        var latest = artifact.LatestVersion;
        return CommandResult<ArtifactState>.Success(new ArtifactState(
            artifact.Id, artifact.Kind, artifact.StudyId, artifact.Title, latest.Id, latest.Number, latest.ContentHash));
    }
}

/// <summary>Registers the evidence command handlers (spec §8.3).</summary>
public static class EvidenceServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelEvidenceHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<RegisterArtifact, ArtifactState>, RegisterArtifactHandler>();
        services.AddTransient<ICommandHandler<AddArtifactVersion, ArtifactState>, AddArtifactVersionHandler>();
        return services;
    }
}
