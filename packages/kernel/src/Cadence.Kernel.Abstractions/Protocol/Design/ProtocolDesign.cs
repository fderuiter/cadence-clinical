using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Protocol.Design;

/// <summary>
/// The executable protocol (spec §6.2, ADR-2472): the machine-readable specification of the experiment that a
/// <c>ProtocolVersion</c> holds as its content. The PDF, the CRF set, the SoA grid and the registry entry are
/// renderings of it; CDISC USDM converts in and out of it but is not its shape.
/// <para>
/// Every element has an <see cref="IProtocolElement.Id"/> that is unique in the design and stays the same across
/// versions (K-PROT-1): a fork copies its parent's elements with their ids, so two versions can be compared element by
/// element (the kernel's structural diff, ADR-2480 §4.6). Elements refer to one another by id.
/// </para>
/// </summary>
/// <remarks>
/// A list the document leaves out, or gives as <c>null</c>, reads as empty: the JSON source generator sets every
/// init-only property it does not find to its default, so the initializers alone would leave those lists null.
/// </remarks>
[SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods and properties that don't access instance data should be static", Justification = "False positive: each init accessor writes the property's own backing field (the field keyword).")]
public sealed record ProtocolDesign
{
    /// <summary>The schema version this kernel reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The design schema version. A document must state it, and only <see cref="CurrentSchemaVersion"/> is accepted;
    /// a design built in code starts at the current version.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The protocol's title.</summary>
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<Objective> Objectives { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Endpoint> Endpoints { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Estimand> Estimands { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Population> Populations { get; init => field = value ?? []; } = [];

    public IReadOnlyList<EligibilityCriterion> EligibilityCriteria { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Arm> Arms { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Epoch> Epochs { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Cohort> Cohorts { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Intervention> Interventions { get; init => field = value ?? []; } = [];

    public IReadOnlyList<PlannedActivity> PlannedActivities { get; init => field = value ?? []; } = [];

    public IReadOnlyList<CollectionSpec> CollectionSpecs { get; init => field = value ?? []; } = [];

    public IReadOnlyList<ProtocolRule> SafetyRules { get; init => field = value ?? []; } = [];

    public IReadOnlyList<ProtocolRule> DiscontinuationRules { get; init => field = value ?? []; } = [];

    /// <summary>The randomization scheme, held elsewhere (RTSM) and referenced here.</summary>
    public DesignReference? RandomizationScheme { get; init; }

    /// <summary>The statistical design (sample size, multiplicity, SAP), referenced as a versioned Artifact.</summary>
    public DesignReference? StatisticalDesign { get; init; }
}

/// <summary>An element of a <see cref="ProtocolDesign"/> with its stable id (K-PROT-1).</summary>
public interface IProtocolElement
{
    /// <summary>
    /// Unique in the design and kept across versions. A letter, then up to 127 letters, digits and <c>_ . : -</c>
    /// (USDM ids such as <c>Objective_1</c> fit).
    /// </summary>
    string Id { get; }
}

/// <summary>A study objective.</summary>
public sealed record Objective(string Id, ObjectiveLevel Level, string Text) : IProtocolElement;

/// <summary>An endpoint that measures an objective; <c>Concept</c> names what it measures when a concept does.</summary>
public sealed record Endpoint(string Id, string ObjectiveId, ObjectiveLevel Level, string Text, ConceptRef? Concept = null) : IProtocolElement;

/// <summary>
/// An estimand (ICH E9(R1)): the treatment condition (<paramref name="ArmIds"/>), the population, the variable (from
/// its endpoint), how intercurrent events are handled, and the summary measure.
/// </summary>
public sealed record Estimand(
    string Id,
    string EndpointId,
    string PopulationId,
    IReadOnlyList<string> ArmIds,
    string Variable,
    IReadOnlyList<IntercurrentEvent> IntercurrentEvents,
    string SummaryMeasure) : IProtocolElement;

/// <summary>An intercurrent event and the strategy an estimand uses for it.</summary>
public sealed record IntercurrentEvent(string Description, IntercurrentEventStrategy Strategy);

/// <summary>An analysis or enrolment population, defined by eligibility criteria.</summary>
public sealed record Population(string Id, string Name, IReadOnlyList<string> CriterionIds, string? Description = null) : IProtocolElement;

/// <summary>
/// An inclusion or exclusion criterion. It is concept-based (<paramref name="Concepts"/>) and, when
/// <paramref name="Expression"/> is set, evaluable over a participant's observations in the rule expression language
/// (ADR-2461).
/// </summary>
public sealed record EligibilityCriterion(
    string Id,
    CriterionCategory Category,
    string Text,
    IReadOnlyList<ConceptRef> Concepts,
    string? Expression = null) : IProtocolElement;

/// <summary>A study arm and the interventions it gives.</summary>
public sealed record Arm(string Id, string Name, ArmType Type, IReadOnlyList<string> InterventionIds) : IProtocolElement;

/// <summary>A study epoch (screening, treatment, follow-up), in <paramref name="Order"/>.</summary>
public sealed record Epoch(string Id, string Name, int Order) : IProtocolElement;

/// <summary>A cohort within one or more arms.</summary>
public sealed record Cohort(string Id, string Name, IReadOnlyList<string> ArmIds, string? Description = null) : IProtocolElement;

/// <summary>An intervention: the product, and how much, by which route and on what regimen it is given.</summary>
public sealed record Intervention(
    string Id,
    string Name,
    ConceptRef? Product = null,
    Dose? Dose = null,
    string? Route = null,
    string? Regimen = null) : IProtocolElement;

/// <summary>A dose: an amount in a UCUM unit.</summary>
public sealed record Dose(decimal Amount, string Unit);

/// <summary>
/// Something the protocol plans to happen to a participant: when (<paramref name="Timing"/>), in which epoch, where it
/// may happen (<paramref name="Locations"/>; not only at a site, ICH E6(R3) Annex 2), what it collects, and why.
/// </summary>
public sealed record PlannedActivity(
    string Id,
    string Name,
    string EpochId,
    IReadOnlyList<ActivityLocation> Locations,
    IReadOnlyList<string> CollectionSpecIds,
    ActivityTiming? Timing = null,
    DataPurpose? Purpose = null) : IProtocolElement;

/// <summary>A planned study day with the window allowed before and after it, in days.</summary>
public sealed record ActivityTiming(int Day, int WindowBefore = 0, int WindowAfter = 0);

/// <summary>
/// Why data is collected (K-PROT-3): the endpoints, estimands or safety rules it serves, or an operational need. An
/// item whose purpose resolves to none of these is a design finding.
/// </summary>
[SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods and properties that don't access instance data should be static", Justification = "False positive: each init accessor writes the property's own backing field (the field keyword).")]
public sealed record DataPurpose
{
    public IReadOnlyList<string> EndpointIds { get; init => field = value ?? []; } = [];

    public IReadOnlyList<string> EstimandIds { get; init => field = value ?? []; } = [];

    public IReadOnlyList<string> SafetyRuleIds { get; init => field = value ?? []; } = [];

    /// <summary>A stated operational need, such as dosing by weight.</summary>
    public string? OperationalNeed { get; init; }
}

/// <summary>What to collect: a set of items, each bound to a concept (K-SEM-1).</summary>
public sealed record CollectionSpec(string Id, string Name, IReadOnlyList<CollectionItem> Items, DataPurpose? Purpose = null) : IProtocolElement;

/// <summary>
/// One item to collect: the concept it records, its UCUM unit when it is a quantity, the pinned terminology its values
/// come from, and why it is collected when that differs from its specification's purpose.
/// </summary>
public sealed record CollectionItem(
    string Id,
    ConceptRef Concept,
    string? Unit = null,
    TerminologyBinding? Terminology = null,
    bool Required = false,
    DataPurpose? Purpose = null) : IProtocolElement;

/// <summary>A value set from a pinned code-system version, optionally narrowed to one codelist.</summary>
public sealed record TerminologyBinding(CodeSystemVersionId CodeSystemVersionId, string? Codelist = null);

/// <summary>A safety or discontinuation rule, concept-based and optionally evaluable.</summary>
public sealed record ProtocolRule(string Id, string Text, IReadOnlyList<ConceptRef> Concepts, string? Expression = null) : IProtocolElement;

/// <summary>A design held outside the protocol (a randomization scheme, a statistical design), by reference.</summary>
public sealed record DesignReference(string Id, string Title, ArtifactId? Artifact = null) : IProtocolElement;

/// <summary>The level of an objective or endpoint.</summary>
public enum ObjectiveLevel
{
    Primary,
    Secondary,
    Exploratory,
}

/// <summary>ICH E9(R1) strategies for an intercurrent event.</summary>
public enum IntercurrentEventStrategy
{
    TreatmentPolicy,
    Hypothetical,
    Composite,
    WhileOnTreatment,
    PrincipalStratum,
}

/// <summary>Whether an eligibility criterion includes or excludes.</summary>
public enum CriterionCategory
{
    Inclusion,
    Exclusion,
}

/// <summary>The kind of arm.</summary>
public enum ArmType
{
    Experimental,
    ActiveComparator,
    PlaceboComparator,
    NoIntervention,
    Other,
}

/// <summary>Where a planned activity may happen.</summary>
public enum ActivityLocation
{
    Site,
    Home,
    Telehealth,
    LocalFacility,
    Remote,
}
