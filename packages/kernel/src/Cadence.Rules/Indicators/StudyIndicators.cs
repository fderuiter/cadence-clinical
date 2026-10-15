using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Indicators;
using Cadence.Kernel.References;
using Cadence.Rules.Expressions;

namespace Cadence.Rules.Indicators;

/// <summary>
/// One indicator's value as a reader sees it. The value is computed over the population records the reader may see:
/// records they may not see are left out before counting, so neither the value nor its contributors reveal them.
/// <see cref="Partial"/> says that some were left out and <see cref="HiddenReasons"/> why, never how many (ADR-2488 §4.7).
/// </summary>
/// <param name="Result">The result over the records the reader may see.</param>
/// <param name="Partial">True when records of the indicator's population were hidden from the reader.</param>
/// <param name="HiddenReasons">Why they were hidden, each reason once; empty when nothing was.</param>
public sealed record IndicatorView(IndicatorResult Result, bool Partial, IReadOnlyList<MaskReason> HiddenReasons);

/// <summary>A study's indicator values at one as-of: every indicator of every set the study pinned, sets ordered by key.</summary>
/// <param name="Study">The study.</param>
/// <param name="AsOf">The instant the populations were read at.</param>
/// <param name="Indicators">The values.</param>
public sealed record StudyIndicatorValues(NodeRef Study, DateTimeOffset AsOf, IReadOnlyList<IndicatorView> Indicators);

/// <summary>
/// A study's Pulse (ADR-2488 §4.6, §4.7): loads the indicator sets the study pinned, reads each population they name
/// from its registered <see cref="IIndicatorPopulationSource"/>, leaves out the records the reader may not see
/// (<see cref="INodeVisibility"/>, one call per evaluation) and evaluates what remains. It never writes. The caller
/// checks that the reader may read the study before calling, as for any study read.
/// </summary>
public sealed class StudyIndicators(
    IndicatorSetLoader loader,
    IndicatorEvaluator evaluator,
    IEnumerable<IIndicatorPopulationSource> sources,
    INodeVisibility? visibility = null)
{
    /// <summary>The most records one population may hold for a study; a larger one is refused rather than miscounted.</summary>
    public const int MaxRecords = 10_000;

    /// <summary>
    /// Every pinned indicator's value for <paramref name="study"/> at <paramref name="asOf"/>, over the records
    /// <paramref name="reader"/> may see. Indicator effective periods are compared with the as-of's UTC date.
    /// </summary>
    public async Task<CommandResult<StudyIndicatorValues>> EvaluateAsync(
        TenantId tenant, Actor reader, StudyId study, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var sets = await loader.LoadForStudyAsync(tenant, study, cancellationToken).ConfigureAwait(false);
        var context = new IndicatorContext(NodeRef.For(study), DateOnly.FromDateTime(asOf.UtcDateTime));
        var names = sets.SelectMany(s => s.Set.Indicators)
            .Where(i => i.Effective.Contains(context.AsOf))
            .Select(i => i.Population)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var bySource = sources.ToDictionary(s => s.Population, StringComparer.Ordinal);
        var unknown = names.Where(n => !bySource.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
        {
            return IndicatorErrors.PopulationUnknown.WithMessage(
                $"No population source is registered for {string.Join(", ", unknown)}, which the study's indicator sets count.");
        }

        var read = new Dictionary<string, IReadOnlyList<PopulationRecord>>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var population = await bySource[name].ReadAsync(tenant, study, asOf, MaxRecords, cancellationToken).ConfigureAwait(false);
            if (population.Truncated)
            {
                return IndicatorErrors.PopulationTooLarge.WithMessage(
                    $"The study has more than {MaxRecords} {name} records; its indicators would be miscounted.");
            }

            read.Add(name, population.Records);
        }

        var nodes = read.Values.SelectMany(r => r).Select(r => r.Node).Distinct().ToList();
        if (visibility is null)
        {
            throw new InvalidOperationException("INodeVisibility is not registered.");
        }

        var hidden = nodes.Count == 0
            ? new Dictionary<NodeRef, MaskReason>()
            : await visibility.MaskAsync(tenant, reader, nodes, cancellationToken).ConfigureAwait(false);
        var populations = read.ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<IndicatorRecord>)[.. p.Value.Where(r => !hidden.ContainsKey(r.Node)).Select(ToIndicatorRecord)],
            StringComparer.Ordinal);
        var reasons = read.ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<MaskReason>)[.. p.Value.Where(r => hidden.ContainsKey(r.Node)).Select(r => hidden[r.Node]).Distinct().Order()],
            StringComparer.Ordinal);

        var views = sets
            .SelectMany(set => evaluator.Evaluate(set, context, populations))
            .Select(r => new IndicatorView(r, reasons[Population(sets, r)].Count > 0, reasons[Population(sets, r)]))
            .ToList();
        return CommandResult<StudyIndicatorValues>.Success(new StudyIndicatorValues(context.Scope, asOf, views));
    }

    private static string Population(IReadOnlyList<LoadedIndicatorSet> sets, IndicatorResult result) =>
        sets.First(s => s.Id == result.Indicator.SetId).Set.Indicators.First(i => i.Id == result.Indicator.IndicatorId).Population;

    private static IndicatorRecord ToIndicatorRecord(PopulationRecord record) => new(
        record.Node,
        record.Values.ToDictionary(v => v.Key, v => RuleValue.FromText(v.Value), StringComparer.Ordinal));
}
