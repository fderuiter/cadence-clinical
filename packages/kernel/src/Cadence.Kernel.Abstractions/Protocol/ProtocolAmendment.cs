using System.Collections.Generic;

namespace Cadence.Kernel.Protocol;

/// <summary>
/// One cohort predicate and the transition policy applied to matching enrollments when an amendment uses
/// <see cref="TransitionPolicy.ByCohort"/> (spec §7.5).
/// </summary>
public sealed record CohortTransitionRule(
    string Predicate,
    TransitionPolicy Policy);

/// <summary>
/// The amendment a forked protocol version introduces (spec §6.2): why it changes, how it is classified, what it
/// changes, the protocol elements it affects and how current participants transition. It belongs to the version it
/// introduces and is frozen with it on submission for review.
/// </summary>
/// <param name="Intent">What the amendment sets out to change.</param>
/// <param name="Rationale">Why.</param>
/// <param name="Classification">Substantial, non-substantial or urgent safety measure.</param>
/// <param name="SummaryOfChanges">The summary of changes for regulators and sites.</param>
/// <param name="ImpactedElements">Stable ids of the protocol elements the amendment changes (K-PROT-1, K-PROT-2).</param>
/// <param name="TransitionPolicy">How participants on the previous version move to this one.</param>
/// <param name="CohortRules">Cohort rules evaluated in order when <paramref name="TransitionPolicy"/> is <see cref="TransitionPolicy.ByCohort"/>.</param>
/// <param name="DefaultCohortPolicy">Fallback policy for enrollments matching none of <paramref name="CohortRules"/>.</param>
public sealed record ProtocolAmendment(
    string Intent,
    string Rationale,
    AmendmentClassification Classification,
    string SummaryOfChanges,
    IReadOnlyList<string> ImpactedElements,
    TransitionPolicy TransitionPolicy,
    IReadOnlyList<CohortTransitionRule>? CohortRules = null,
    TransitionPolicy DefaultCohortPolicy = TransitionPolicy.RequiresReconsent);
