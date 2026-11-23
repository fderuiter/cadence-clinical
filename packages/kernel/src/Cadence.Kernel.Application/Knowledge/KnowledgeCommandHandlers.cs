using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Events;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Knowledge;

/// <summary>Handles <see cref="RegisterCodeSystem"/>.</summary>
internal sealed class RegisterCodeSystemHandler : ICommandHandler<RegisterCodeSystem, CodeSystemId>
{
    public async Task<CommandResult<CodeSystemId>> HandleAsync(RegisterCodeSystem command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        if (await context.Work.ExistsAsync<CodeSystem>(command.CodeSystemId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var registered = CodeSystem.Register(command.CodeSystemId, command.TenantId, command.Uri, command.Name, command.Publisher, command.License);
        if (!registered.IsSuccess)
        {
            return registered.Error!;
        }

        var codeSystem = registered.Value!;
        context.Work.Add(codeSystem);
        context.Work.Raise(new CodeSystemRegistered(command.TenantId, codeSystem.Id, codeSystem.Uri, codeSystem.Name, codeSystem.License));
        return CommandResult<CodeSystemId>.Success(codeSystem.Id);
    }
}

/// <summary>Handles <see cref="ImportCodeSystemVersion"/>. A second import of the same version is refused by its unique key.</summary>
internal sealed class ImportCodeSystemVersionHandler : ICommandHandler<ImportCodeSystemVersion, CodeSystemVersionId>
{
    public async Task<CommandResult<CodeSystemVersionId>> HandleAsync(ImportCodeSystemVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!await context.Work.ExistsAsync<CodeSystem>(command.CodeSystemId, cancellationToken).ConfigureAwait(false))
        {
            return KnowledgeErrors.CodeSystemUnknown;
        }

        if (await context.Work.ExistsAsync<CodeSystemVersion>(command.CodeSystemVersionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var imported = CodeSystemVersion.Import(
            command.CodeSystemVersionId, command.TenantId, command.CodeSystemId, command.Version, command.ReleasedOn, command.Package, command.Entries);
        if (!imported.IsSuccess)
        {
            return imported.Error!;
        }

        var version = imported.Value!;
        context.Work.Add(version);
        context.Work.Raise(new CodeSystemVersionImported(
            command.TenantId, version.Id, version.CodeSystemId, version.Version, version.ReleasedOn, version.PackageSha256, version.EntryCount));
        return CommandResult<CodeSystemVersionId>.Success(version.Id);
    }
}

/// <summary>Handles <see cref="DefineConcept"/>. A CDISC concept code already used in the tenant is refused by its unique key.</summary>
internal sealed class DefineConceptHandler : ICommandHandler<DefineConcept, ConceptRef>
{
    public async Task<CommandResult<ConceptRef>> HandleAsync(DefineConcept command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        if (await context.Work.ExistsAsync<Concept>(command.ConceptId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var defined = Concept.Define(
            command.ConceptId, command.TenantId, command.Origin, command.ExternalCode, command.Kind,
            command.PreferredLabel, command.Definition, context.RecordedAt);
        if (!defined.IsSuccess)
        {
            return defined.Error!;
        }

        var concept = defined.Value!;
        context.Work.Add(concept);
        context.Work.Raise(new ConceptDefined(command.TenantId, concept.Current, concept.Origin, concept.ExternalCode, concept.Kind, concept.PreferredLabel));
        return CommandResult<ConceptRef>.Success(concept.Current);
    }
}

/// <summary>
/// Handles <see cref="DefineConcepts"/>: validates and defines a homogeneous batch of concepts in one transaction,
/// emitting one <see cref="ConceptDefined"/> event (with per-concept subject) and one audit-chain link per concept.
/// </summary>
internal sealed class DefineConceptsHandler : ICommandHandler<DefineConcepts, IReadOnlyList<ConceptRef>>
{
    public async Task<CommandResult<IReadOnlyList<ConceptRef>>> HandleAsync(
        DefineConcepts command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.Concepts is not { Count: > 0 })
        {
            return KnowledgeErrors.ConceptBatchEmpty;
        }

        var staged = new List<Concept>(command.Concepts.Count);
        var seenIds = new HashSet<ConceptId>();
        var seenBcCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in command.Concepts)
        {
            var defined = Concept.Define(
                item.ConceptId,
                command.TenantId,
                item.Origin,
                item.ExternalCode,
                item.Kind,
                item.PreferredLabel,
                item.Definition,
                context.RecordedAt);
            if (!defined.IsSuccess)
            {
                return defined.Error!;
            }

            var concept = defined.Value!;
            if (!seenIds.Add(concept.Id))
            {
                return KernelErrors.AlreadyExists;
            }

            if (concept.Origin == ConceptOrigin.CdiscBiomedicalConcept
                && concept.ExternalCode is not null
                && !seenBcCodes.Add(concept.ExternalCode))
            {
                return KernelErrors.AlreadyExists;
            }

            staged.Add(concept);
        }

        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        var idList = seenIds.ToList();
        var existingById = await context.Work
            .ReadAllAsync<Concept>(c => idList.Contains(c.Id), cancellationToken)
            .ConfigureAwait(false);
        if (existingById.Count > 0)
        {
            return KernelErrors.AlreadyExists;
        }

        if (seenBcCodes.Count > 0)
        {
            var codeList = seenBcCodes.ToList();
            var existingByCode = await context.Work
                .ReadAllAsync<Concept>(
                    c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode != null && codeList.Contains(c.ExternalCode),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingByCode.Count > 0)
            {
                return KernelErrors.AlreadyExists;
            }
        }

        var refs = new ConceptRef[staged.Count];
        for (int i = 0; i < staged.Count; i++)
        {
            var concept = staged[i];
            context.Work.Add(concept);
            context.Work.Raise(
                new ConceptDefined(command.TenantId, concept.Current, concept.Origin, concept.ExternalCode, concept.Kind, concept.PreferredLabel),
                NodeRef.For(concept.Id, concept.Version));
            refs[i] = concept.Current;
        }

        return CommandResult<IReadOnlyList<ConceptRef>>.Success(refs);
    }
}

/// <summary>Handles <see cref="ReviseConcept"/>.</summary>
internal sealed class ReviseConceptHandler : ICommandHandler<ReviseConcept, ConceptRef>
{
    public async Task<CommandResult<ConceptRef>> HandleAsync(ReviseConcept command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Concept>(command.ConceptId, cancellationToken).ConfigureAwait(false) is not { } concept)
        {
            return KnowledgeErrors.ConceptUnknown;
        }

        if (concept.Revise(command.PreferredLabel, command.Definition, context.RecordedAt) is { } refused)
        {
            return refused;
        }

        context.Work.Raise(new ConceptRevised(command.TenantId, concept.Current, concept.PreferredLabel));
        return CommandResult<ConceptRef>.Success(concept.Current);
    }
}

/// <summary>Handles <see cref="DeprecateConcept"/>.</summary>
internal sealed class DeprecateConceptHandler : ICommandHandler<DeprecateConcept, ConceptRef>
{
    public async Task<CommandResult<ConceptRef>> HandleAsync(DeprecateConcept command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Concept>(command.ConceptId, cancellationToken).ConfigureAwait(false) is not { } concept)
        {
            return KnowledgeErrors.ConceptUnknown;
        }

        if (concept.Deprecate() is { } refused)
        {
            return refused;
        }

        context.Work.Raise(new ConceptDeprecated(command.TenantId, concept.Current));
        return CommandResult<ConceptRef>.Success(concept.Current);
    }
}

/// <summary>
/// Handles <see cref="MapConcept"/>. The concept version and the code are read through the registry: both are
/// immutable once written, so reading them outside the command's aggregate does not weaken K-TX-1. The mapping is
/// defined by the concept version and the code-system version (spec §9.6). A concept that is not deprecated has
/// revised exactly as often as it has changed, so its revision number is also its version index.
/// </summary>
internal sealed class MapConceptHandler(IConceptRegistry registry) : ICommandHandler<MapConcept, ConceptMappingId>
{
    public async Task<CommandResult<ConceptMappingId>> HandleAsync(MapConcept command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<ConceptMapping>(command.ConceptMappingId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var concept = await registry.ResolveAsync(command.TenantId, command.Concept, cancellationToken).ConfigureAwait(false);
        if (concept is null)
        {
            return KnowledgeErrors.ConceptUnknown;
        }

        if (concept.Status == ConceptStatus.Deprecated)
        {
            return KnowledgeErrors.ConceptDeprecated;
        }

        var entries = await registry.LookupAsync(command.TenantId, command.Target, cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return KnowledgeErrors.CodeUnknown;
        }

        var created = ConceptMapping.Create(
            command.ConceptMappingId, command.TenantId, command.Concept, command.Target, command.Relation, command.Evidence, command.Confirmed);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        var mapping = created.Value!;
        context.Work.Add(mapping);
        context.Work.LinkInput(NodeRef.For(command.Concept.ConceptId, command.Concept.Version), LineageEdgeKind.DefinedBy);
        context.Work.LinkInput(NodeRef.For(command.Target.CodeSystemVersionId, 1), LineageEdgeKind.DefinedBy);
        context.Work.Raise(new ConceptMapped(command.TenantId, mapping.Id, mapping.Concept, mapping.Target, mapping.Relation, mapping.Status));
        return CommandResult<ConceptMappingId>.Success(mapping.Id);
    }
}

/// <summary>Handles <see cref="ReviewConceptMapping"/>.</summary>
internal sealed class ReviewConceptMappingHandler : ICommandHandler<ReviewConceptMapping, ConceptMappingId>
{
    public async Task<CommandResult<ConceptMappingId>> HandleAsync(ReviewConceptMapping command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ConceptMapping>(command.ConceptMappingId, cancellationToken).ConfigureAwait(false) is not { } mapping)
        {
            return KnowledgeErrors.MappingUnknown;
        }

        if (mapping.Review(command.Status) is { } refused)
        {
            return refused;
        }

        context.Work.Raise(new ConceptMappingReviewed(command.TenantId, mapping.Id, mapping.Status));
        return CommandResult<ConceptMappingId>.Success(mapping.Id);
    }
}

/// <summary>Registers the Concept Registry's command handlers (spec §6.1).</summary>
public static class KnowledgeServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelKnowledgeHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<RegisterCodeSystem, CodeSystemId>, RegisterCodeSystemHandler>();
        services.AddTransient<ICommandHandler<ImportCodeSystemVersion, CodeSystemVersionId>, ImportCodeSystemVersionHandler>();
        services.AddTransient<ICommandHandler<DefineConcept, ConceptRef>, DefineConceptHandler>();
        services.AddTransient<ICommandHandler<DefineConcepts, IReadOnlyList<ConceptRef>>, DefineConceptsHandler>();
        services.AddTransient<ICommandHandler<ReviseConcept, ConceptRef>, ReviseConceptHandler>();
        services.AddTransient<ICommandHandler<DeprecateConcept, ConceptRef>, DeprecateConceptHandler>();
        services.AddTransient<ICommandHandler<MapConcept, ConceptMappingId>, MapConceptHandler>();
        services.AddTransient<ICommandHandler<ReviewConceptMapping, ConceptMappingId>, ReviewConceptMappingHandler>();
        return services;
    }
}
