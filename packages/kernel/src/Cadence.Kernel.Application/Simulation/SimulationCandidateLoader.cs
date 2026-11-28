using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Trust;
using Cadence.Rules.Packs;

namespace Cadence.Kernel.Application.Simulation;

internal sealed record ProtocolCandidate(
    ProtocolVersionId? Id,
    StudyId StudyId,
    string Content,
    string ContentHash,
    long VersionIndex,
    ProtocolAmendment? Amendment);

internal static class SimulationCandidateLoader
{
    private const int MaxReportedProblems = 10;

    internal static async Task<CommandResult<(ProtocolCandidate Candidate, ProtocolVersionSnapshot? Base)>> LoadProtocolAsync(
        ISimulationInputs inputs,
        TenantId tenant,
        SimulationCandidate request,
        CancellationToken cancellationToken)
    {
        if (request.VersionId is { } versionId)
        {
            var stored = await inputs.FindProtocolVersionAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return SimulationErrors.VersionUnknown;
            }

            if (request.Version is { } expectedVersion && stored.VersionIndex != expectedVersion)
            {
                return SimulationErrors.VersionStale.WithMessage(
                    $"The protocol version is at version {stored.VersionIndex}, not {expectedVersion}; reload and simulate the current version.");
            }

            ProtocolVersionSnapshot? parent = null;
            if (stored.ParentVersionId is { } parentId)
            {
                parent = await inputs.FindProtocolVersionAsync(tenant, parentId, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    return SimulationErrors.VersionUnknown.WithMessage("The protocol version's parent does not exist.");
                }
            }

            var hash = stored.ContentHash ?? ContentHash.Compute(stored.Content);
            return CommandResult<(ProtocolCandidate, ProtocolVersionSnapshot?)>.Success(
                (new ProtocolCandidate(stored.Id, stored.StudyId, stored.Content, hash, stored.VersionIndex, stored.Amendment), parent));
        }

        var baseId = request.BaseVersionId!.Value;
        var baseVersion = await inputs.FindProtocolVersionAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
        if (baseVersion is null)
        {
            return SimulationErrors.VersionUnknown;
        }

        if (request.BaseVersion is { } expectedBaseVersion && baseVersion.VersionIndex != expectedBaseVersion)
        {
            return SimulationErrors.VersionStale.WithMessage(
                $"The base protocol version is at version {baseVersion.VersionIndex}, not {expectedBaseVersion}; reload and simulate against the current version.");
        }

        if (baseVersion.ContentHash is null)
        {
            return SimulationErrors.BaseNotFrozen;
        }

        var read = ProtocolDesignDocument.Read(request.Content!);
        if (!read.IsValid)
        {
            var more = read.Problems.Count > MaxReportedProblems ? $" (and {read.Problems.Count - MaxReportedProblems} more)" : string.Empty;
            return SimulationErrors.ContentInvalid.WithMessage(
                $"{SimulationErrors.ContentInvalid.Message} {string.Join(" ", read.Problems.Take(MaxReportedProblems))}{more}");
        }

        return CommandResult<(ProtocolCandidate, ProtocolVersionSnapshot?)>.Success(
            (new ProtocolCandidate(null, baseVersion.StudyId, request.Content!, ContentHash.Compute(request.Content!), 0, null), baseVersion));
    }

    internal static async Task<CommandResult<(
        MappingSpecification Candidate,
        NodeRef? CandidateRef,
        string CandidateHash,
        LibraryItemView? ExplicitBaseItem,
        MappingSpecification? ExplicitBaseSpec)>> LoadMappingCandidateAsync(
        ISimulationInputs inputs,
        TenantId tenant,
        SimulationCandidate request,
        CancellationToken cancellationToken)
    {
        LibraryItemView? explicitBaseItem = null;
        MappingSpecification? explicitBaseSpec = null;
        if (request.BaseLibraryItemId is { } baseId)
        {
            explicitBaseItem = await inputs.FindLibraryItemAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
            if (explicitBaseItem is null || explicitBaseItem.Kind != LibraryItemKind.MappingSpecification)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            if (request.LibraryItemId is null && explicitBaseItem.Status == GovernanceStatus.Draft)
            {
                return SimulationErrors.BaseNotFrozen;
            }

            var (parsedBase, baseProblems) = MappingSpecificationJson.Read(explicitBaseItem.Content);
            if (parsedBase is null)
            {
                return FormatInvalid(SimulationErrors.MappingSpecificationInvalid, baseProblems);
            }

            explicitBaseSpec = parsedBase;
        }

        if (request.LibraryItemId is { } itemId)
        {
            var item = await inputs.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
            if (item is null || item.Kind != LibraryItemKind.MappingSpecification)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            var (parsed, problems) = MappingSpecificationJson.Read(item.Content);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.MappingSpecificationInvalid, problems);
            }

            return CommandResult<(MappingSpecification, NodeRef?, string, LibraryItemView?, MappingSpecification?)>.Success(
                (parsed, NodeRefForItem(item), item.ContentHash, explicitBaseItem, explicitBaseSpec));
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.Content!);
        }
        catch (JsonException ex)
        {
            return SimulationErrors.MappingSpecificationInvalid.WithMessage($"{SimulationErrors.MappingSpecificationInvalid.Message} {ex.Message}");
        }

        using (doc)
        {
            var (parsed, problems) = MappingSpecificationJson.Read(doc.RootElement);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.MappingSpecificationInvalid, problems);
            }

            return CommandResult<(MappingSpecification, NodeRef?, string, LibraryItemView?, MappingSpecification?)>.Success(
                (parsed, null, ContentHash.Compute(request.Content!), explicitBaseItem, explicitBaseSpec));
        }
    }

    internal static async Task<CommandResult<(
        RulePack Candidate,
        LibraryItemView? CandidateItem,
        NodeRef? CandidateRef,
        string CandidateHash,
        LibraryItemView? ExplicitBaseItem,
        RulePack? ExplicitBasePack)>> LoadRulePackCandidateAsync(
        ISimulationInputs inputs,
        TenantId tenant,
        SimulationCandidate request,
        CancellationToken cancellationToken)
    {
        LibraryItemView? explicitBaseItem = null;
        RulePack? explicitBasePack = null;
        if (request.BaseLibraryItemId is { } baseId)
        {
            explicitBaseItem = await inputs.FindLibraryItemAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
            if (explicitBaseItem is null || explicitBaseItem.Kind != LibraryItemKind.RulePack)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            if (request.LibraryItemId is null && explicitBaseItem.Status == GovernanceStatus.Draft)
            {
                return SimulationErrors.BaseNotFrozen;
            }

            var (parsedBase, baseProblems) = RulePackJson.Read(explicitBaseItem.Content);
            if (parsedBase is null)
            {
                return FormatInvalid(SimulationErrors.RulePackInvalid, baseProblems);
            }

            explicitBasePack = parsedBase;
        }

        if (request.LibraryItemId is { } itemId)
        {
            var item = await inputs.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
            if (item is null || item.Kind != LibraryItemKind.RulePack)
            {
                return SimulationErrors.LibraryItemUnknown;
            }

            var (parsed, problems) = RulePackJson.Read(item.Content);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.RulePackInvalid, problems);
            }

            return CommandResult<(RulePack, LibraryItemView?, NodeRef?, string, LibraryItemView?, RulePack?)>.Success(
                (parsed, item, NodeRefForItem(item), item.ContentHash, explicitBaseItem, explicitBasePack));
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.Content!);
        }
        catch (JsonException ex)
        {
            return SimulationErrors.RulePackInvalid.WithMessage($"{SimulationErrors.RulePackInvalid.Message} {ex.Message}");
        }

        using (doc)
        {
            var (parsed, problems) = RulePackJson.Read(doc.RootElement);
            if (parsed is null)
            {
                return FormatInvalid(SimulationErrors.RulePackInvalid, problems);
            }

            return CommandResult<(RulePack, LibraryItemView?, NodeRef?, string, LibraryItemView?, RulePack?)>.Success(
                (parsed, null, null, ContentHash.Compute(request.Content!), explicitBaseItem, explicitBasePack));
        }
    }

    internal static (LibraryItemView? BaseItem, MappingSpecification? BaseSpec) ResolveMappingBase(
        MappingSpecification candidate,
        LibraryItemView? explicitBaseItem,
        MappingSpecification? explicitBaseSpec,
        IReadOnlyList<LibraryItemView> pinnedItems)
    {
        if (explicitBaseItem is not null && explicitBaseSpec is not null)
        {
            return (explicitBaseItem, explicitBaseSpec);
        }

        if (!string.IsNullOrWhiteSpace(candidate.Overrides))
        {
            var overridden = pinnedItems.FirstOrDefault(i =>
                i.Kind == LibraryItemKind.MappingSpecification
                && string.Equals(i.Key, candidate.Overrides, StringComparison.Ordinal));
            if (overridden is not null && MappingSpecificationJson.Read(overridden.Content).Specification is { } parsedOverridden)
            {
                return (overridden, parsedOverridden);
            }
        }

        var firstPinned = pinnedItems.FirstOrDefault(i => i.Kind == LibraryItemKind.MappingSpecification);
        if (firstPinned is not null && MappingSpecificationJson.Read(firstPinned.Content).Specification is { } parsedFirst)
        {
            return (firstPinned, parsedFirst);
        }

        return (null, null);
    }

    internal static (LibraryItemView? BaseItem, RulePack? BasePack) ResolveRulePackBase(
        RulePack candidate,
        LibraryItemView? candidateItem,
        LibraryItemView? explicitBaseItem,
        RulePack? explicitBasePack,
        IReadOnlyList<LibraryItemView> pinnedPacks)
    {
        if (explicitBaseItem is not null && explicitBasePack is not null)
        {
            return (explicitBaseItem, explicitBasePack);
        }

        LibraryItemView? matching = null;
        if (candidateItem is not null)
        {
            matching = pinnedPacks.FirstOrDefault(p =>
                p.Id != candidateItem.Id && string.Equals(p.Key, candidateItem.Key, StringComparison.Ordinal));
        }

        matching ??= pinnedPacks.FirstOrDefault(p => string.Equals(p.Title, candidate.Title, StringComparison.Ordinal))
            ?? pinnedPacks.FirstOrDefault();

        if (matching is not null && RulePackJson.Read(matching.Content).Pack is { } parsed)
        {
            return (matching, parsed);
        }

        return (null, null);
    }

    internal static (IReadOnlyList<MappingSpecification> Baseline, IReadOnlyList<MappingSpecification> Candidate) BuildMappingSpecStacks(
        IReadOnlyList<LibraryItemView> pinnedItems,
        LibraryItemView? baseItem,
        MappingSpecification? baseSpec,
        MappingSpecification candidateSpec)
    {
        var pinned = new List<(LibraryItemView Item, MappingSpecification Spec)>();
        foreach (var item in pinnedItems.Where(i => i.Kind == LibraryItemKind.MappingSpecification))
        {
            if (MappingSpecificationJson.Read(item.Content).Specification is { } parsed)
            {
                pinned.Add((item, parsed));
            }
        }

        pinned = [.. pinned
            .OrderBy(x => string.IsNullOrWhiteSpace(x.Spec.Overrides) ? 0 : 1)
            .ThenBy(x => x.Item.Key, StringComparer.Ordinal)];

        if (pinned.Count == 0)
        {
            IReadOnlyList<MappingSpecification> baselineOnly = baseSpec is null ? [] : [baseSpec];
            IReadOnlyList<MappingSpecification> candidateOnly = baseSpec is not null && !string.IsNullOrWhiteSpace(candidateSpec.Overrides)
                ? [baseSpec, candidateSpec]
                : [candidateSpec];
            return (baselineOnly, candidateOnly);
        }

        var baselineList = pinned.Select(p => p.Spec).ToList();
        if (baseItem is not null && baseSpec is not null && pinned.All(p => p.Item.Id != baseItem.Id))
        {
            baselineList.Add(baseSpec);
        }

        var candidateList = new List<MappingSpecification>(pinned.Count + 1);
        var replaced = false;
        foreach (var (item, spec) in pinned)
        {
            if (baseItem is not null && item.Id == baseItem.Id && string.IsNullOrWhiteSpace(candidateSpec.Overrides))
            {
                candidateList.Add(candidateSpec);
                replaced = true;
            }
            else
            {
                candidateList.Add(spec);
            }
        }

        if (!replaced)
        {
            candidateList.Add(candidateSpec);
        }

        return (baselineList, candidateList);
    }

    internal static (IReadOnlyList<RulePack> Baseline, IReadOnlyList<RulePack> Candidate) BuildRulePackStacks(
        IReadOnlyList<LibraryItemView> pinnedPackItems,
        LibraryItemView? baseItem,
        RulePack? basePack,
        RulePack candidatePack)
    {
        var pinned = new List<(LibraryItemView Item, RulePack Pack)>();
        foreach (var item in pinnedPackItems.Where(i => i.Kind == LibraryItemKind.RulePack))
        {
            if (RulePackJson.Read(item.Content).Pack is { } parsed)
            {
                pinned.Add((item, parsed));
            }
        }

        if (pinned.Count == 0)
        {
            IReadOnlyList<RulePack> baselineOnly = basePack is null ? [] : [basePack];
            return (baselineOnly, [candidatePack]);
        }

        var baselineList = pinned.Select(p => p.Pack).ToList();
        if (baseItem is not null && basePack is not null && pinned.All(p => p.Item.Id != baseItem.Id))
        {
            baselineList.Add(basePack);
        }

        var candidateList = new List<RulePack>(pinned.Count + 1);
        var replaced = false;
        foreach (var (item, pack) in pinned)
        {
            if (baseItem is not null && item.Id == baseItem.Id)
            {
                candidateList.Add(candidatePack);
                replaced = true;
            }
            else
            {
                candidateList.Add(pack);
            }
        }

        if (!replaced)
        {
            candidateList.Add(candidatePack);
        }

        return (baselineList, candidateList);
    }

    internal static IReadOnlyList<MappingSpecification> ParsePinnedMappingSpecs(IReadOnlyList<LibraryItemView> items) =>
        [.. items
            .Where(i => i.Kind == LibraryItemKind.MappingSpecification)
            .Select(i => (Item: i, Spec: MappingSpecificationJson.Read(i.Content).Specification))
            .Where(x => x.Spec is not null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.Spec!.Overrides) ? 0 : 1)
            .ThenBy(x => x.Item.Key, StringComparer.Ordinal)
            .Select(x => x.Spec!)];

    internal static IReadOnlyList<RulePack> ParsePinnedRulePacks(IReadOnlyList<LibraryItemView> items) =>
        [.. items
            .Where(i => i.Kind == LibraryItemKind.RulePack)
            .Select(i => RulePackJson.Read(i.Content).Pack)
            .OfType<RulePack>()];

    internal static ProtocolDesign? SelectActiveProtocolDesign(MaskedSimulationSnapshot snapshot)
    {
        var effectiveVersionIds = snapshot.VisibleEffectivities
            .Where(e => e.StatusAsOf == EffectivityStatus.Effective)
            .Select(e => e.ProtocolVersionId)
            .ToHashSet();

        foreach (var id in effectiveVersionIds.OrderBy(x => x.Value))
        {
            if (snapshot.ProtocolDesigns.TryGetValue(id, out var effectiveDesign))
            {
                return effectiveDesign;
            }
        }

        return snapshot.ProtocolDesigns
            .OrderBy(kv => kv.Key.Value)
            .Select(kv => kv.Value)
            .LastOrDefault();
    }

    internal static NodeRef NodeRefForItem(LibraryItemView item) =>
        item.VersionIndex >= 1 ? NodeRef.For(item.Id, item.VersionIndex) : NodeRef.For(item.Id);

    private static KernelError FormatInvalid(KernelError error, IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
        {
            return error;
        }

        var more = problems.Count > MaxReportedProblems ? $" (and {problems.Count - MaxReportedProblems} more)" : string.Empty;
        return error.WithMessage($"{error.Message} {string.Join(" ", problems.Take(MaxReportedProblems))}{more}");
    }
}
