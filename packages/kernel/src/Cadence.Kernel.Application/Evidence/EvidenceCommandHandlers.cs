using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;

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

/// <summary>Handles <see cref="ExportStudySnapshot"/>.</summary>
internal sealed class ExportStudySnapshotHandler : ICommandHandler<ExportStudySnapshot, ArtifactState>
{
    public async Task<CommandResult<ArtifactState>> HandleAsync(ExportStudySnapshot command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Artifact>(command.ArtifactId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return EvidenceErrors.StudyUnknown;
        }

        var registered = Artifact.Register(
            command.ArtifactId,
            command.TenantId,
            ArtifactKind.Dataset,
            command.StudyId,
            command.Title,
            command.ArtifactVersionId,
            command.Content,
            context.RecordedAt);
        if (registered.Value is not { } artifact)
        {
            return registered.Error!;
        }

        var version = artifact.LatestVersion;
        context.Work.Add(artifact);
        context.Work.Raise(new ArtifactRegistered(
            artifact.TenantId, artifact.Id, version.Id, artifact.Kind, artifact.StudyId, version.ContentHash, version.MediaType, version.SizeBytes));
        context.Work.Raise(
            new StudySnapshotExported(
                artifact.TenantId,
                command.StudyId,
                artifact.Id,
                version.Id,
                command.Format,
                command.LockFilter,
                command.ValidAt,
                command.KnownAt,
                command.IncludesUnblindedData,
                version.ContentHash,
                version.SizeBytes),
            command.ValidAt);
        return ArtifactStates.Of(artifact);
    }
}

/// <summary>Handles <see cref="ExportEnrollmentCasebook"/>.</summary>
internal sealed class ExportEnrollmentCasebookHandler : ICommandHandler<ExportEnrollmentCasebook, ArtifactState>
{
    public async Task<CommandResult<ArtifactState>> HandleAsync(ExportEnrollmentCasebook command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Artifact>(command.ArtifactId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return EvidenceErrors.EnrollmentUnknown;
        }

        var registered = Artifact.Register(
            command.ArtifactId,
            command.TenantId,
            ArtifactKind.Dataset,
            enrollment.StudyId,
            command.Title,
            command.ArtifactVersionId,
            command.Content,
            context.RecordedAt);
        if (registered.Value is not { } artifact)
        {
            return registered.Error!;
        }

        var version = artifact.LatestVersion;
        context.Work.Add(artifact);
        context.Work.Raise(new ArtifactRegistered(
            artifact.TenantId, artifact.Id, version.Id, artifact.Kind, artifact.StudyId, version.ContentHash, version.MediaType, version.SizeBytes));
        context.Work.Raise(
            new EnrollmentCasebookExported(
                artifact.TenantId,
                command.EnrollmentId,
                enrollment.StudyId,
                artifact.Id,
                version.Id,
                command.Format,
                command.ValidAt,
                command.KnownAt,
                command.IncludesUnblindedData,
                version.ContentHash,
                version.SizeBytes),
            command.ValidAt);
        return ArtifactStates.Of(artifact);
    }
}

/// <summary>Handles <see cref="SaveSimulationReport"/> (ADR-2480, K-SIM-5).</summary>
internal sealed class SaveSimulationReportHandler : ICommandHandler<SaveSimulationReport, ArtifactState>
{
    public async Task<CommandResult<ArtifactState>> HandleAsync(SaveSimulationReport command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Artifact>(command.ArtifactId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return EvidenceErrors.StudyUnknown;
        }

        if (command.Result is not { } result)
        {
            return EvidenceErrors.SimulationReportInvalid.WithMessage("A simulation result is required.");
        }

        if (result.Scope != NodeRef.For(command.StudyId))
        {
            return EvidenceErrors.SimulationReportInvalid.WithMessage(
                $"The simulation result is scoped to {result.Scope}, not study {command.StudyId}.");
        }

        if (result.Masked.Count > 0)
        {
            return EvidenceErrors.SimulationReportInvalid.WithMessage(
                "A masked simulation result cannot be saved as an attestable governance artifact (K-SIM-5).");
        }

        if (SimulationArtifactBinding.ToArtifactContent(result) is not { } content)
        {
            return EvidenceErrors.SimulationReportInvalid;
        }

        var registered = Artifact.Register(
            command.ArtifactId,
            command.TenantId,
            ArtifactKind.SimulationReport,
            command.StudyId,
            command.Title,
            command.ArtifactVersionId,
            content,
            context.RecordedAt);
        if (registered.Value is not { } artifact)
        {
            return registered.Error!;
        }

        var version = artifact.LatestVersion;
        context.Work.Add(artifact);
        if (result.Candidate.Version is { Version: not null } candidateRef)
        {
            context.Work.LinkInput(candidateRef, LineageEdgeKind.DerivedFrom);
        }

        context.Work.Raise(new ArtifactRegistered(
            artifact.TenantId, artifact.Id, version.Id, artifact.Kind, artifact.StudyId, version.ContentHash, version.MediaType, version.SizeBytes));
        return ArtifactStates.Of(artifact);
    }
}

/// <summary>Immutable signable target details for an <see cref="ArtifactVersion"/>.</summary>
/// <param name="ArtifactId">Owning artifact identifier.</param>
/// <param name="ArtifactVersionId">Artifact version identifier.</param>
/// <param name="StudyId">Owning study identifier when study-scoped, otherwise null.</param>
/// <param name="Kind">Artifact kind.</param>
/// <param name="ContentHash">Canonical SHA-256 content hash of the artifact version.</param>
public sealed record ArtifactVersionSignableInfo(
    ArtifactId ArtifactId,
    ArtifactVersionId ArtifactVersionId,
    StudyId? StudyId,
    ArtifactKind Kind,
    string ContentHash);

/// <summary>Read-only lookup of an <see cref="ArtifactVersion"/>'s signable content hash for 21 CFR Part 11 attestation.</summary>
public static class ArtifactVersionTargetLookup
{
    /// <summary>Finds the signable metadata of <paramref name="artifactVersionId"/> in <paramref name="tenantId"/>, or null when absent.</summary>
    public static async Task<ArtifactVersionSignableInfo?> FindAsync(
        TenantId tenantId,
        IKernelUnitOfWorkFactory unitOfWorkFactory,
        Actor actor,
        DateTimeOffset recordedAt,
        ArtifactVersionId artifactVersionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWorkFactory);
        ArgumentNullException.ThrowIfNull(actor);

        var probe = new Attest(
            tenantId,
            AttestationId.New(),
            AttestationMeaning.Reviewed,
            new AttestationTarget(NodeKinds.ArtifactVersion, artifactVersionId.Value, string.Empty),
            recordedAt,
            "precheck",
            "precheck",
            "Artifact version lookup");
        var execution = new CommandExecution(
            probe,
            probe.GetType().Name,
            actor,
            recordedAt,
            Guid.CreateVersion7(),
            new CommandMetadata(Guid.CreateVersion7()),
            "precheck");
        await using var tx = unitOfWorkFactory.Begin(execution);
        var artifacts = await tx.Work.ReadAllAsync<Artifact>(
            a => a.Versions.Any(v => v.Id == artifactVersionId),
            cancellationToken).ConfigureAwait(false);
        if (artifacts.Count == 0 || artifacts[0].Version(artifactVersionId) is not { } version)
        {
            return null;
        }

        var artifact = artifacts[0];
        return new ArtifactVersionSignableInfo(
            artifact.Id,
            version.Id,
            artifact.StudyId,
            artifact.Kind,
            version.ContentHash);
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
        services.AddTransient<ICommandHandler<ExportStudySnapshot, ArtifactState>, ExportStudySnapshotHandler>();
        services.AddTransient<ICommandHandler<ExportEnrollmentCasebook, ArtifactState>, ExportEnrollmentCasebookHandler>();
        services.AddTransient<ICommandHandler<SaveSimulationReport, ArtifactState>, SaveSimulationReportHandler>();
        return services;
    }
}
