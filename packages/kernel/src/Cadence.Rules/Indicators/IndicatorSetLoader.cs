using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Standards;

namespace Cadence.Rules.Indicators;

/// <summary>Errors of indicator sets (K-ERR-1).</summary>
public static class IndicatorErrors
{
    /// <summary>No library item has this id in the tenant.</summary>
    public static readonly KernelError SetUnknown =
        new("K.Indicators.SetUnknown", 404, "The indicator set does not exist.");

    /// <summary>The library item exists but is not of kind <c>IndicatorSet</c>.</summary>
    public static readonly KernelError NotAnIndicatorSet =
        new("K.Indicators.NotAnIndicatorSet", 422, "The library item is not an indicator set.");

    /// <summary>The set is still a draft.</summary>
    public static readonly KernelError SetNotReleased =
        new("K.Indicators.SetNotReleased", 422, "The indicator set has not been released; approve it with an attestation first.");

    /// <summary>The stored content does not read as an indicator set.</summary>
    public static readonly KernelError SetInvalid =
        new("K.Indicators.SetInvalid", 422, "The indicator set content is not valid.");

    /// <summary>A pinned indicator counts a population no registered source provides.</summary>
    public static readonly KernelError PopulationUnknown =
        new("K.Indicators.PopulationUnknown", 422, "An indicator counts a population that no source provides.");

    /// <summary>A population has more records than one evaluation reads, so its values would be wrong.</summary>
    public static readonly KernelError PopulationTooLarge =
        new("K.Indicators.PopulationTooLarge", 422, "A population is too large to count in one evaluation.");
}

/// <summary>A loaded indicator set, or why it could not be loaded.</summary>
/// <param name="Set">The set, when it loaded.</param>
/// <param name="Error">Why it did not.</param>
public sealed record IndicatorSetLoadResult(LoadedIndicatorSet? Set, KernelError? Error)
{
    /// <summary>True when <see cref="Set"/> was loaded.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Loads released indicator sets from the Metadata Registry (ADR-2488). A set is released once its library item is
/// approved with an attestation; a deprecated set still loads for the studies that pinned it, and a draft never loads,
/// so a Pulse number is always computed by a released definition.
/// </summary>
public sealed class IndicatorSetLoader(IStandardsRegistry? registry = null)
{
    /// <summary>Loads one released set by its library item id.</summary>
    public async Task<IndicatorSetLoadResult> LoadAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken = default)
    {
        if (registry is null)
        {
            return new(null, IndicatorErrors.SetUnknown);
        }

        var item = await registry.FindLibraryItemAsync(tenant, id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return new(null, IndicatorErrors.SetUnknown);
        }

        if (item.Kind != LibraryItemKind.IndicatorSet)
        {
            return new(null, IndicatorErrors.NotAnIndicatorSet.WithMessage($"Library item {id} is a {item.Kind}, not an indicator set."));
        }

        if (item.Status == GovernanceStatus.Draft)
        {
            return new(null, IndicatorErrors.SetNotReleased.WithMessage($"Indicator set {item.Key} version {item.Version} is a draft."));
        }

        var (set, problems) = IndicatorSetJson.Read(item.Content);
        return set is null
            ? new(null, IndicatorErrors.SetInvalid.WithMessage(string.Join(" ", problems)))
            : new(new LoadedIndicatorSet(item.Id, item.Key, item.Version, item.ContentHash, item.Status, set), null);
    }

    /// <summary>
    /// The indicator sets a study pinned in its standards profile, ordered by key. A study with no profile has none;
    /// pins of other kinds (rule packs, mapping specifications) are skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException">A pinned set can no longer be read, which the registry's guards prevent.</exception>
    public async Task<IReadOnlyList<LoadedIndicatorSet>> LoadForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken = default)
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

        var sets = new List<LoadedIndicatorSet>();
        foreach (var pin in profile.LibraryPins)
        {
            var loaded = await LoadAsync(tenant, pin, cancellationToken).ConfigureAwait(false);
            if (loaded.Error?.Code == IndicatorErrors.NotAnIndicatorSet.Code)
            {
                continue;
            }

            sets.Add(loaded.Set ?? throw new InvalidOperationException($"Pinned indicator set {pin} cannot be loaded: {loaded.Error!.Message}"));
        }

        return [.. sets.OrderBy(s => s.Key, StringComparer.Ordinal)];
    }
}

/// <summary>
/// Validates indicator set content when a <c>DraftLibraryItem</c> command drafts one, so an invalid set is refused
/// before it can be approved.
/// </summary>
public sealed class IndicatorSetContentValidator : ILibraryContentValidator
{
    /// <inheritdoc/>
    public LibraryItemKind Kind => LibraryItemKind.IndicatorSet;

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(JsonElement content) => IndicatorSetJson.Read(content).Problems;
}
