using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Standards;

namespace Cadence.Rules.Packs;

/// <summary>Errors of the rules subsystem (K-ERR-1).</summary>
public static class RuleErrors
{
    /// <summary>No library item has this id in the tenant.</summary>
    public static readonly KernelError PackUnknown =
        new("K.Rules.PackUnknown", 404, "The rule pack does not exist.");

    /// <summary>The library item exists but is not of kind <c>RulePack</c>.</summary>
    public static readonly KernelError NotARulePack =
        new("K.Rules.NotARulePack", 422, "The library item is not a rule pack.");

    /// <summary>The pack is still a draft.</summary>
    public static readonly KernelError PackNotReleased =
        new("K.Rules.PackNotReleased", 422, "The rule pack has not been released; approve it with an attestation first.");

    /// <summary>The stored content does not read as a rule pack.</summary>
    public static readonly KernelError PackInvalid =
        new("K.Rules.PackInvalid", 422, "The rule pack content is not valid.");

    /// <summary>The study's standards profile pins no rule pack, so there is nothing to validate with.</summary>
    public static readonly KernelError NoRulePacks =
        new("K.Rules.NoRulePacks", 422, "The study pins no rule packs; pin released rule packs in its standards profile first.");
}

/// <summary>A loaded pack, or why it could not be loaded.</summary>
public sealed record RulePackLoadResult(LoadedRulePack? Pack, KernelError? Error)
{
    /// <summary>True when <see cref="Pack"/> was loaded.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Loads released rule packs from the Metadata Registry at runtime (K-RULE-1): a new or changed rule reaches the
/// platform by drafting and approving a new library item version, never by a release. A pack is released once its
/// library item is approved, which needs an attestation; a deprecated pack still loads, because studies that pinned
/// it keep validating against it (ADR-2460). A draft never loads.
/// </summary>
public sealed class RulePackLoader(IStandardsRegistry? registry = null)
{
    /// <summary>Loads one released pack by its library item id.</summary>
    public async Task<RulePackLoadResult> LoadAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken = default)
    {
        if (registry is null)
        {
            return new(null, RuleErrors.PackUnknown);
        }

        var item = await registry.FindLibraryItemAsync(tenant, id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return new(null, RuleErrors.PackUnknown);
        }

        if (item.Kind != LibraryItemKind.RulePack)
        {
            return new(null, RuleErrors.NotARulePack.WithMessage($"Library item {id} is a {item.Kind}, not a rule pack."));
        }

        if (item.Status == GovernanceStatus.Draft)
        {
            return new(null, RuleErrors.PackNotReleased.WithMessage($"Rule pack {item.Key} version {item.Version} is a draft."));
        }

        var (pack, problems) = RulePackJson.Read(item.Content);
        return pack is null
            ? new(null, RuleErrors.PackInvalid.WithMessage(string.Join(" ", problems)))
            : new(
                new LoadedRulePack(item.Id, item.Key, item.Version, item.ContentHash, item.Status, pack)
                {
                    GoverningProtocolVersions = item.GoverningProtocolVersions,
                },
                null);
    }

    /// <summary>
    /// The rule packs a study pinned in its standards profile, ordered by key. A study with no profile has none.
    /// </summary>
    /// <exception cref="InvalidOperationException">A pinned pack can no longer be read, which the registry's guards prevent.</exception>
    public async Task<IReadOnlyList<LoadedRulePack>> LoadForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken = default)
    {
        if (registry is null)
        {
            return [];
        }

        var profile = await registry.FindStudyStandardsAsync(tenant, study, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return [];
        }

        var packs = new List<LoadedRulePack>();
        foreach (var pin in profile.LibraryPins)
        {
            var loaded = await LoadAsync(tenant, pin, cancellationToken).ConfigureAwait(false);
            if (loaded.Error?.Code == RuleErrors.NotARulePack.Code)
            {
                continue;
            }

            packs.Add(loaded.Pack ?? throw new InvalidOperationException($"Pinned rule pack {pin} cannot be loaded: {loaded.Error!.Message}"));
        }

        return [.. packs.OrderBy(p => p.Key, StringComparer.Ordinal)];
    }
}

/// <summary>
/// Validates rule pack content when a <c>DraftLibraryItem</c> command drafts a pack, so an invalid pack is refused
/// before it can be approved.
/// </summary>
public sealed class RulePackContentValidator : ILibraryContentValidator
{
    /// <inheritdoc/>
    public LibraryItemKind Kind => LibraryItemKind.RulePack;

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(JsonElement content) => RulePackJson.Read(content).Problems;
}
