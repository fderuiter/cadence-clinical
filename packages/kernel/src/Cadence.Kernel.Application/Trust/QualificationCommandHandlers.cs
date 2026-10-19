using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Trust;

/// <summary>
/// Handles <see cref="RecordQualification"/>: the evidence is a version of a training record or credential artifact,
/// and protocol training names a protocol version that exists.
/// </summary>
internal sealed class RecordQualificationHandler : ICommandHandler<RecordQualification, QualificationState>
{
    public async Task<CommandResult<QualificationState>> HandleAsync(RecordQualification command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Qualification>(command.QualificationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var recorded = Qualification.Record(
            command.QualificationId, command.TenantId, command.Holder, command.Kind, command.Code, command.ProtocolVersionId,
            command.Evidence, command.ValidFrom, command.ValidTo, context.Actor, context.RecordedAt);
        if (recorded.Value is not { } qualification)
        {
            return recorded.Error!;
        }

        var evidence = command.Evidence;
        var artifacts = await context.Work.ReadAllAsync<Artifact>(a => a.Versions.Any(v => v.Id == evidence), cancellationToken).ConfigureAwait(false);
        if (artifacts.SingleOrDefault() is not { Kind: ArtifactKind.TrainingRecord or ArtifactKind.Credential })
        {
            return QualificationErrors.EvidenceInvalid;
        }

        if (qualification.ProtocolVersionId is { } version
            && !await context.Work.ExistsAsync<ProtocolVersion>(version, cancellationToken).ConfigureAwait(false))
        {
            return QualificationErrors.ProtocolVersionUnknown;
        }

        context.Work.Add(qualification);
        var state = qualification.State();
        context.Work.Raise(new QualificationRecorded(
            qualification.TenantId, qualification.Id, qualification.Holder.ToString(), qualification.Kind, qualification.Code,
            qualification.ProtocolVersionId, qualification.EvidenceArtifactVersionId, state.ValidFrom!.Value, state.ValidTo));
        return CommandResult<QualificationState>.Success(state);
    }
}

/// <summary>Handles <see cref="RevokeQualification"/>.</summary>
internal sealed class RevokeQualificationHandler : ICommandHandler<RevokeQualification, QualificationState>
{
    public async Task<CommandResult<QualificationState>> HandleAsync(RevokeQualification command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Qualification>(command.QualificationId, cancellationToken).ConfigureAwait(false) is not { } qualification)
        {
            return KernelErrors.NotFound;
        }

        if (qualification.Revoke(command.EffectiveAt, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new QualificationRevoked(qualification.TenantId, qualification.Id, command.EffectiveAt));
        return CommandResult<QualificationState>.Success(qualification.State());
    }
}

/// <summary>
/// Handles <see cref="DefineQualificationRequirement"/>: the scope names an object of the tenant, and a named protocol
/// version exists.
/// </summary>
internal sealed class DefineQualificationRequirementHandler : ICommandHandler<DefineQualificationRequirement, QualificationRequirementState>
{
    public async Task<CommandResult<QualificationRequirementState>> HandleAsync(
        DefineQualificationRequirement command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<QualificationRequirement>(command.RequirementId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var defined = QualificationRequirement.Define(
            command.RequirementId, command.TenantId, command.Capability, command.QualificationCode, command.ProtocolVersionId,
            command.Scope, command.ValidFrom, command.ValidTo, context.Actor, context.RecordedAt);
        if (defined.Value is not { } requirement)
        {
            return defined.Error!;
        }

        if (await AuthorityChecks.PathOfAsync(requirement.Scope, context, cancellationToken).ConfigureAwait(false) is null)
        {
            return AuthorityErrors.ScopeUnknown;
        }

        if (requirement.ProtocolVersionId is { } version
            && !await context.Work.ExistsAsync<ProtocolVersion>(version, cancellationToken).ConfigureAwait(false))
        {
            return QualificationErrors.ProtocolVersionUnknown;
        }

        context.Work.Add(requirement);
        var state = requirement.State();
        context.Work.Raise(new QualificationRequirementDefined(
            requirement.TenantId, requirement.Id, requirement.Capability, requirement.QualificationCode, requirement.ProtocolVersionId,
            requirement.Scope, state.ValidFrom!.Value, state.ValidTo));
        return CommandResult<QualificationRequirementState>.Success(state);
    }
}

/// <summary>Handles <see cref="RetireQualificationRequirement"/>.</summary>
internal sealed class RetireQualificationRequirementHandler : ICommandHandler<RetireQualificationRequirement, QualificationRequirementState>
{
    public async Task<CommandResult<QualificationRequirementState>> HandleAsync(
        RetireQualificationRequirement command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<QualificationRequirement>(command.RequirementId, cancellationToken).ConfigureAwait(false) is not { } requirement)
        {
            return KernelErrors.NotFound;
        }

        if (requirement.Retire(command.EffectiveAt, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new QualificationRequirementRetired(requirement.TenantId, requirement.Id, command.EffectiveAt));
        return CommandResult<QualificationRequirementState>.Success(requirement.State());
    }
}

/// <summary>Registers the qualification command handlers (spec §9.3, K-AUTH-5).</summary>
public static class QualificationServiceCollectionExtensions
{
    /// <summary>Called by <see cref="AuthorityServiceCollectionExtensions.AddKernelAuthorityHandlers"/>.</summary>
    public static IServiceCollection AddKernelQualificationHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<RecordQualification, QualificationState>, RecordQualificationHandler>();
        services.AddTransient<ICommandHandler<RevokeQualification, QualificationState>, RevokeQualificationHandler>();
        services.AddTransient<ICommandHandler<DefineQualificationRequirement, QualificationRequirementState>, DefineQualificationRequirementHandler>();
        services.AddTransient<ICommandHandler<RetireQualificationRequirement, QualificationRequirementState>, RetireQualificationRequirementHandler>();
        return services;
    }
}
