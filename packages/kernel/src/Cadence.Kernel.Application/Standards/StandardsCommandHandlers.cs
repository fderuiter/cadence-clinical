using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Standards;

/// <summary>
/// Resolves Standards Registry terminology bindings to the tenant's Concept Registry code-system versions
/// (ADR-2460 §4.2): <c>(sdtmct, 2024-09-27)</c> is the version <c>2024-09-27</c> of
/// <see cref="KnownCodeSystems.SdtmCt"/>, and a URI-named code system is looked up by its URI.
/// </summary>
internal static class TerminologyResolution
{
    /// <summary>The references the tenant has imported, each with its code-system version. Others are absent.</summary>
    public static async Task<IReadOnlyDictionary<CodeSystemVersionRef, CodeSystemVersionId>> ResolveAsync(
        IConceptRegistry concepts, TenantId tenant, IEnumerable<CodeSystemVersionRef> references, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<CodeSystemVersionRef, CodeSystemVersionId>();
        foreach (var reference in references.Distinct())
        {
            if (reference.ToCodeSystemUri() is { } uri && !string.IsNullOrWhiteSpace(reference.Version)
                && await concepts.FindCodeSystemVersionAsync(tenant, uri, reference.Version, cancellationToken).ConfigureAwait(false) is { } version)
            {
                resolved[reference] = version.Id;
            }
        }

        return resolved;
    }
}

/// <summary>Handles <see cref="RegisterStandardVersion"/>.</summary>
internal sealed class RegisterStandardVersionHandler(IStandardsRegistry registry, IConceptRegistry concepts) : ICommandHandler<RegisterStandardVersion, StandardVersionId>
{
    public async Task<CommandResult<StandardVersionId>> HandleAsync(RegisterStandardVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Package);
        var terminology = await TerminologyResolution.ResolveAsync(
            concepts, command.TenantId, command.Package.ControlledTerminology ?? [], cancellationToken).ConfigureAwait(false);
        var (version, error) = StandardVersion.Register(
            command.StandardVersionId, command.TenantId, command.Package, command.ExpectedContentHash, terminology);
        if (error is not null)
        {
            return error;
        }

        var existing = await registry.FindStandardVersionAsync(command.TenantId, version!.StandardCode, version.Version, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return StandardsErrors.VersionAlreadyRegistered.WithMessage(
                $"{existing.StandardCode} {existing.Version} is already registered as {existing.Id} ({existing.ContentHash}).");
        }

        if (await context.Work.ExistsAsync<StandardVersion>(command.StandardVersionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(version);
        context.Work.Raise(new StandardVersionRegistered(
            command.TenantId,
            version.Id,
            version.StandardCode,
            version.Version,
            version.ContentHash,
            version.Datasets.Count,
            version.Datasets.Sum(d => d.Variables.Count)));
        return CommandResult<StandardVersionId>.Success(version.Id);
    }
}

/// <summary>Handles <see cref="ApproveStandardVersion"/> and <see cref="DeprecateStandardVersion"/>.</summary>
internal sealed class StandardVersionGovernanceHandler(IAttestationVerifier attestations) :
    ICommandHandler<ApproveStandardVersion, StandardVersionId>,
    ICommandHandler<DeprecateStandardVersion, StandardVersionId>
{
    public Task<CommandResult<StandardVersionId>> HandleAsync(ApproveStandardVersion command, ICommandContext context, CancellationToken cancellationToken) =>
        ChangeAsync(command.StandardVersionId, command.AttestationId, AttestationMeaning.Approved, context, cancellationToken, (v, a) =>
            v.Approve(a) ?? Raise(context, new StandardVersionApproved(command.TenantId, v.Id, a)));

    public Task<CommandResult<StandardVersionId>> HandleAsync(DeprecateStandardVersion command, ICommandContext context, CancellationToken cancellationToken) =>
        ChangeAsync(command.StandardVersionId, command.AttestationId, AttestationMeaning.Responsibility, context, cancellationToken, (v, a) =>
            v.Deprecate(a) ?? Raise(context, new StandardVersionDeprecated(command.TenantId, v.Id, a)));

    private async Task<CommandResult<StandardVersionId>> ChangeAsync(
        StandardVersionId id,
        AttestationId attestation,
        AttestationMeaning meaning,
        ICommandContext context,
        CancellationToken cancellationToken,
        Func<StandardVersion, AttestationId, KernelError?> change)
    {
        var version = await context.Work.FindAsync<StandardVersion>(id, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return StandardsErrors.StandardVersionUnknown;
        }

        var check = await attestations.VerifyAsync(
            new AttestationRequirement(context.TenantId, attestation, meaning, new AttestationTarget(NodeKinds.StandardVersion, id.Value, version.ContentHash), context.Actor),
            cancellationToken).ConfigureAwait(false);
        if (!check.Satisfied)
        {
            return StandardsErrors.AttestationRejected.WithMessage(check.Reason ?? StandardsErrors.AttestationRejected.Message);
        }

        return change(version, attestation) is { } error ? error : CommandResult<StandardVersionId>.Success(id);
    }

    private static KernelError? Raise(ICommandContext context, object domainEvent)
    {
        context.Work.Raise(domainEvent);
        return null;
    }
}

/// <summary>
/// Handles <see cref="DraftLibraryItem"/>. A mapping specification's references must also resolve, and become its
/// lineage inputs (<see cref="MappingSpecificationReferences"/>).
/// </summary>
internal sealed class DraftLibraryItemHandler(IStandardsRegistry registry, IConceptRegistry concepts, IEnumerable<ILibraryContentValidator> validators)
    : ICommandHandler<DraftLibraryItem, LibraryItemVersion>
{
    public async Task<CommandResult<LibraryItemVersion>> HandleAsync(DraftLibraryItem command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.StandardVersions);

        if (command.Content.ValueKind == JsonValueKind.Object)
        {
            var problems = validators.Where(v => v.Kind == command.Kind).SelectMany(v => v.Validate(command.Content)).ToList();
            if (problems.Count > 0)
            {
                return StandardsErrors.LibraryItemInvalid.WithMessage(string.Join(" ", problems.Take(20)));
            }
        }

        foreach (var standardVersion in command.StandardVersions.Distinct())
        {
            var found = await registry.FindStandardVersionAsync(command.TenantId, standardVersion, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return StandardsErrors.StandardVersionUnknown.WithMessage($"Standard version {standardVersion} does not exist.");
            }
        }

        IReadOnlyList<NodeRef> inputs = [];
        if (command.Kind == LibraryItemKind.MappingSpecification && command.Content.ValueKind == JsonValueKind.Object)
        {
            (inputs, var invalid) = await MappingSpecificationReferences.ResolveAsync(command, registry, concepts, context, cancellationToken).ConfigureAwait(false);
            if (invalid is not null)
            {
                return invalid;
            }
        }

        var versions = await registry.ListLibraryItemVersionsAsync(command.TenantId, command.Kind, command.Key ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var next = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1;

        var (item, error) = LibraryItem.Draft(
            command.LibraryItemId, command.TenantId, command.Kind, command.Key!, next, command.Title, command.Content, command.StandardVersions);
        if (error is not null)
        {
            return error;
        }

        if (await context.Work.ExistsAsync<LibraryItem>(command.LibraryItemId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(item!);
        foreach (var input in inputs)
        {
            context.Work.LinkInput(input, LineageEdgeKind.DefinedBy);
        }

        context.Work.Raise(new LibraryItemDrafted(command.TenantId, item!.Id, item.Kind, item.Key, item.ItemVersion, item.ContentHash));
        return CommandResult<LibraryItemVersion>.Success(item.ToVersion());
    }
}

/// <summary>Handles <see cref="ApproveLibraryItem"/> and <see cref="DeprecateLibraryItem"/>.</summary>
internal sealed class LibraryItemGovernanceHandler(IAttestationVerifier attestations) :
    ICommandHandler<ApproveLibraryItem, LibraryItemVersion>,
    ICommandHandler<DeprecateLibraryItem, LibraryItemVersion>
{
    public Task<CommandResult<LibraryItemVersion>> HandleAsync(ApproveLibraryItem command, ICommandContext context, CancellationToken cancellationToken) =>
        ChangeAsync(command.LibraryItemId, command.AttestationId, AttestationMeaning.Approved, context, cancellationToken, (i, a) =>
            i.Approve(a) ?? Raise(context, new LibraryItemApproved(command.TenantId, i.Id, i.Kind, i.Key, i.ItemVersion, a)));

    public Task<CommandResult<LibraryItemVersion>> HandleAsync(DeprecateLibraryItem command, ICommandContext context, CancellationToken cancellationToken) =>
        ChangeAsync(command.LibraryItemId, command.AttestationId, AttestationMeaning.Responsibility, context, cancellationToken, (i, a) =>
            i.Deprecate(a) ?? Raise(context, new LibraryItemDeprecated(command.TenantId, i.Id, i.Kind, i.Key, i.ItemVersion, a)));

    private async Task<CommandResult<LibraryItemVersion>> ChangeAsync(
        LibraryItemId id,
        AttestationId attestation,
        AttestationMeaning meaning,
        ICommandContext context,
        CancellationToken cancellationToken,
        Func<LibraryItem, AttestationId, KernelError?> change)
    {
        var item = await context.Work.FindAsync<LibraryItem>(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return StandardsErrors.LibraryItemUnknown;
        }

        var check = await attestations.VerifyAsync(
            new AttestationRequirement(context.TenantId, attestation, meaning, new AttestationTarget(NodeKinds.LibraryItem, id.Value, item.ContentHash), context.Actor),
            cancellationToken).ConfigureAwait(false);
        if (!check.Satisfied)
        {
            return StandardsErrors.AttestationRejected.WithMessage(check.Reason ?? StandardsErrors.AttestationRejected.Message);
        }

        return change(item, attestation) is { } error ? error : CommandResult<LibraryItemVersion>.Success(item.ToVersion());
    }

    private static KernelError? Raise(ICommandContext context, object domainEvent)
    {
        context.Work.Raise(domainEvent);
        return null;
    }
}

/// <summary>Handles <see cref="DeclareStudyStandards"/>.</summary>
internal sealed class DeclareStudyStandardsHandler(IStandardsRegistry registry, IConceptRegistry concepts) : ICommandHandler<DeclareStudyStandards, StudyStandardsProfileId>
{
    public async Task<CommandResult<StudyStandardsProfileId>> HandleAsync(DeclareStudyStandards command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.StandardVersions);
        ArgumentNullException.ThrowIfNull(command.ControlledTerminology);
        ArgumentNullException.ThrowIfNull(command.LibraryPins);

        if (!await context.Work.ExistsAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false))
        {
            return StandardsErrors.StudyUnknown;
        }

        var standards = new Dictionary<StandardVersionId, StandardVersionSummary>();
        foreach (var id in command.StandardVersions.Distinct())
        {
            if (await registry.FindStandardVersionAsync(command.TenantId, id, cancellationToken).ConfigureAwait(false) is { } found)
            {
                standards[id] = found;
            }
        }

        var pins = new Dictionary<LibraryItemId, LibraryItemView>();
        foreach (var id in command.LibraryPins.Distinct())
        {
            if (await registry.FindLibraryItemAsync(command.TenantId, id, cancellationToken).ConfigureAwait(false) is { } found)
            {
                pins[id] = found;
            }
        }

        var terminology = await TerminologyResolution.ResolveAsync(concepts, command.TenantId, command.ControlledTerminology, cancellationToken).ConfigureAwait(false);

        // Pinned mapping specifications must work together over the declared standards (ADR-2463). Unknown or
        // unapproved pins are reported by the declaration itself.
        if (pins.Count == command.LibraryPins.Distinct().Count()
            && pins.Values.All(p => p.Status == GovernanceStatus.Approved)
            && PinnedMappings.Resolve(pins.Values, command.StandardVersions).Problems is { Count: > 0 } mappingProblems)
        {
            return StandardsErrors.DeclarationInvalid.WithMessage(string.Join(" ", mappingProblems));
        }

        var profile = await context.Work.FindAsync<StudyStandardsProfile>(command.ProfileId, cancellationToken).ConfigureAwait(false);
        var isNew = profile is null;
        if (profile is null)
        {
            if (await registry.FindStudyStandardsAsync(command.TenantId, command.StudyId, cancellationToken).ConfigureAwait(false) is { } other)
            {
                return StandardsErrors.DeclarationInvalid.WithMessage($"Study {command.StudyId} already declared its standards as profile {other.Id}; revise that profile.");
            }

            profile = StudyStandardsProfile.Create(command.ProfileId, command.TenantId, command.StudyId);
        }
        else if (profile.StudyId != command.StudyId)
        {
            return StandardsErrors.DeclarationInvalid.WithMessage($"Profile {command.ProfileId} belongs to another study.");
        }

        var before = (profile.StandardVersionIds, profile.ControlledTerminology, profile.ControlledTerminologyVersions, profile.LibraryPins);
        if (profile.Declare(command.StandardVersions, command.ControlledTerminology, command.LibraryPins, standards, pins, terminology) is { } error)
        {
            return error;
        }

        if (isNew)
        {
            context.Work.Add(profile);
        }
        else if (before.StandardVersionIds.SequenceEqual(profile.StandardVersionIds)
            && before.ControlledTerminology.SequenceEqual(profile.ControlledTerminology, StringComparer.Ordinal)
            && before.ControlledTerminologyVersions.SequenceEqual(profile.ControlledTerminologyVersions)
            && before.LibraryPins.SequenceEqual(profile.LibraryPins))
        {
            // Nothing changed: no new revision, no event.
            return CommandResult<StudyStandardsProfileId>.Success(profile.Id);
        }

        context.Work.Raise(new StudyStandardsDeclared(
            command.TenantId,
            command.StudyId,
            profile.Id,
            isNew ? 1 : profile.VersionIndex + 1,
            profile.StandardVersionIds,
            profile.ControlledTerminologyRefs,
            profile.LibraryPins));
        return CommandResult<StudyStandardsProfileId>.Success(profile.Id);
    }
}

/// <summary>Registers the Standards Registry and Metadata Registry command handlers (spec §6.3).</summary>
public static class KernelStandardsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the standards command handlers. The host must also register kernel persistence's
    /// <see cref="IStandardsRegistry"/> (<c>AddKernelStandardsPersistence</c>); <c>AddKernelPersistence</c> provides the
    /// <see cref="IAttestationVerifier"/> that approval and deprecation check and the <see cref="IConceptRegistry"/> that
    /// terminology bindings resolve through. It also registers the mapping specification content validator and the
    /// protocol submission checks for K-STD-1 (ADR-2463) and K-STD-2 (ADR-2464), which read runs and findings through
    /// <see cref="IValidationFindings"/> (<c>AddKernelValidationPersistence</c>).
    /// </summary>
    public static IServiceCollection AddKernelStandards(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<ICommandHandler<RegisterStandardVersion, StandardVersionId>, RegisterStandardVersionHandler>();
        services.AddTransient<ICommandHandler<ApproveStandardVersion, StandardVersionId>, StandardVersionGovernanceHandler>();
        services.AddTransient<ICommandHandler<DeprecateStandardVersion, StandardVersionId>, StandardVersionGovernanceHandler>();
        services.AddTransient<ICommandHandler<DraftLibraryItem, LibraryItemVersion>, DraftLibraryItemHandler>();
        services.AddTransient<ICommandHandler<ApproveLibraryItem, LibraryItemVersion>, LibraryItemGovernanceHandler>();
        services.AddTransient<ICommandHandler<DeprecateLibraryItem, LibraryItemVersion>, LibraryItemGovernanceHandler>();
        services.AddTransient<ICommandHandler<DeclareStudyStandards, StudyStandardsProfileId>, DeclareStudyStandardsHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, MappingSpecificationContentValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IProtocolSubmissionCheck, MappingCoverageCheck>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IProtocolSubmissionCheck, DesignConformanceCheck>());
        return services;
    }
}
