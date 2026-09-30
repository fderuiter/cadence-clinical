using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Indicators;

/// <summary>
/// Reads the records of one population that indicators count (ADR-2488 §4.6): each record is an object and the
/// variables indicator rules read. A source knows what exists, never who may see it; the indicator service masks the
/// objects the reader may not see. The kernel registers sources for its own objects; applications register sources
/// for theirs the same way.
/// </summary>
public interface IIndicatorPopulationSource
{
    /// <summary>The population name indicator sets use, for example <see cref="KernelPopulations.Enrollments"/>.</summary>
    string Population { get; }

    /// <summary>
    /// The study's records at <paramref name="asOf"/>, sorted by object, up to <paramref name="limit"/>. The read reports
    /// <see cref="PopulationRead.Truncated"/> when the study has more.
    /// </summary>
    Task<PopulationRead> ReadAsync(TenantId tenant, StudyId study, DateTimeOffset asOf, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// One record of a population. Values are text, the form rule expressions compare: numbers are written with the
/// invariant culture and compare numerically, a missing value is null.
/// </summary>
/// <param name="Node">The object the record stands for.</param>
/// <param name="Values">The variables by name.</param>
public sealed record PopulationRecord(NodeRef Node, IReadOnlyDictionary<string, string?> Values);

/// <summary>A population read.</summary>
/// <param name="Records">The records, sorted by object.</param>
/// <param name="Truncated">True when the study has more records than the limit.</param>
public sealed record PopulationRead(IReadOnlyList<PopulationRecord> Records, bool Truncated);

/// <summary>The populations the kernel provides, and their variables (docs/kernel/INDICATORS.md).</summary>
public static class KernelPopulations
{
    /// <summary>
    /// A study's enrollments with a status at the as-of: <c>STATUS</c> (an <see cref="Execution.EnrollmentStatus"/>
    /// name), <c>STUDY_SITE</c> (the study site's id) and <c>ENROLLMENT_NUMBER</c> (null before enrollment).
    /// </summary>
    public const string Enrollments = "enrollments";

    /// <summary>
    /// A study's validation findings as currently known: <c>STATUS</c>, <c>SEVERITY</c>, <c>STAGE</c>,
    /// <c>PACK</c>, <c>RULE</c>, <c>DATASET</c>, <c>CLASSIFICATION</c> and <c>BLOCKS</c> (<c>true</c> or <c>false</c>).
    /// </summary>
    public const string ValidationFindings = "validation_findings";
}
