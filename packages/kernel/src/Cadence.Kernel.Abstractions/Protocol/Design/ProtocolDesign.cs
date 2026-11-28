using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Kernel.Evidence;
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

    /// <summary>
    /// Readable notes recorded when importing the design from an external format (such as unmapped terminology codes or
    /// optional vendor attributes). Null when none were recorded so pre-existing designs serialize identically.
    /// </summary>
    public IReadOnlyList<ProtocolImportNote>? ImportNotes { get; init; }
}

/// <summary>A readable note attached to an imported protocol draft about unmapped terminology or vendor metadata.</summary>
/// <param name="Source">The element id, OID or path in the imported file that produced the note.</param>
/// <param name="Message">Readable explanation of how the unmapped terminology or optional vendor attribute was handled.</param>
public sealed record ProtocolImportNote(string Source, string Message);

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
/// An estimand (ICH E9(R1)): the treatment condition (<paramref name="ArmIds"/>, and the interventions compared when
/// they are named, <paramref name="InterventionIds"/>), the population, the variable (from its endpoint), how
/// intercurrent events are handled, and the population-level summary measure.
/// </summary>
public sealed record Estimand(
    string Id,
    string EndpointId,
    string PopulationId,
    IReadOnlyList<string> ArmIds,
    string Variable,
    IReadOnlyList<IntercurrentEvent> IntercurrentEvents,
    string SummaryMeasure,
    IReadOnlyList<string>? InterventionIds = null) : IProtocolElement;

/// <summary>
/// An intercurrent event and the strategy an estimand uses for it. <paramref name="StrategyText"/> states the strategy
/// in words; it is required when the strategy is <see cref="IntercurrentEventStrategy.Other"/>.
/// </summary>
public sealed record IntercurrentEvent(string Description, IntercurrentEventStrategy Strategy, string? StrategyText = null);

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
/// <param name="Id">The planned activity's element id, stable across versions (K-PROT-1).</param>
/// <param name="Name">Its display name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="Locations">Where it may take place.</param>
/// <param name="CollectionSpecIds">The collection specifications collected during this activity.</param>
/// <param name="Timing">Planned study day and window, or null when event-driven.</param>
/// <param name="Purpose">Why this activity occurs, when stated.</param>
/// <param name="Repeating">Whether multiple instances of this planned activity may occur for one enrollment.</param>
public sealed record PlannedActivity(
    string Id,
    string Name,
    string EpochId,
    IReadOnlyList<ActivityLocation> Locations,
    IReadOnlyList<string> CollectionSpecIds,
    ActivityTiming? Timing = null,
    DataPurpose? Purpose = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Repeating = false) : IProtocolElement;

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
/// <param name="Id">The collection specification's element id, stable across versions (K-PROT-1).</param>
/// <param name="Name">Its display name.</param>
/// <param name="Items">Ordered items collected by the specification.</param>
/// <param name="Purpose">Why the specification is collected, when stated.</param>
/// <param name="Repeating">Whether multiple instances of this specification may be opened per planned activity or as a repeating log.</param>
public sealed record CollectionSpec(
    string Id,
    string Name,
    IReadOnlyList<CollectionItem> Items,
    DataPurpose? Purpose = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Repeating = false) : IProtocolElement;

/// <summary>
/// One item to collect: the concept it records, its UCUM unit when it is a quantity, the pinned terminology its values
/// come from, the kind of value it takes, and why it is collected when that differs from its specification's purpose.
/// </summary>
/// <param name="Id">The item's id, stable across versions (K-PROT-1).</param>
/// <param name="Concept">What the item records (K-SEM-1).</param>
/// <param name="Unit">The UCUM unit a quantity is collected in.</param>
/// <param name="Terminology">The pinned terminology a code is chosen from.</param>
/// <param name="Required">Whether the item must have a value, a reasoned missing value included.</param>
/// <param name="Purpose">Why the item is collected, when that differs from its specification's purpose.</param>
/// <param name="ValueKind">
/// The kind of value the item takes (ADR-2623). When null it follows from the item: a quantity when it has a
/// <paramref name="Unit"/>, a code when it has a <paramref name="Terminology"/>, otherwise any kind.
/// </param>
/// <param name="Prompt">Optional form-specific item prompt or question label preserved from an imported definition.</param>
/// <param name="SdvRequired">Whether recorded values for this item require source data verification before database lock (defaults to true).</param>
/// <param name="MedicalReviewRequired">Whether recorded values for this item require medical monitor source data review before database lock (defaults to false).</param>
public sealed record CollectionItem(
    string Id,
    ConceptRef Concept,
    string? Unit = null,
    TerminologyBinding? Terminology = null,
    bool Required = false,
    DataPurpose? Purpose = null,
    ObservationValueKind? ValueKind = null,
    string? Prompt = null,
    bool SdvRequired = true,
    bool MedicalReviewRequired = false) : IProtocolElement
{
    /// <summary>The kind of value the item takes, or null when it takes any kind. Derived, so never serialised.</summary>
    [JsonIgnore]
    public ObservationValueKind? ExpectedKind
    {
        get
        {
            if (ValueKind is not null || Unit is not null)
            {
                return ValueKind ?? ObservationValueKind.Quantity;
            }

            return Terminology is not null ? ObservationValueKind.Coded : null;
        }
    }

    /// <summary>
    /// Why <paramref name="value"/> cannot be recorded for this item, or null when it can (ADR-2623): it must be valid,
    /// of the item's kind, in the item's UCUM unit and from the item's code-system version. A missing value with a
    /// reason fits every item (K-FACT-3). Whether a code is in the item's codelist needs the Concept Registry and is
    /// checked where the value is recorded.
    /// </summary>
    public string? Admits(ObservationValue? value)
    {
        if (ObservationValue.ProblemOf(value) is { } problem)
        {
            return problem;
        }

        if (value is MissingValue)
        {
            return null;
        }

        if (ExpectedKind is { } kind && value!.Kind != kind)
        {
            return $"Item {Id} takes a {kind} value, not a {value.Kind} value.";
        }

        if (Unit is not null && value is QuantityValue quantity && !string.Equals(quantity.Unit, Unit, System.StringComparison.Ordinal))
        {
            return $"Item {Id} is collected in {Unit}, not {quantity.Unit}.";
        }

        if (Terminology is not null && value is CodedValue coded && coded.CodeSystemVersionId != Terminology.CodeSystemVersionId)
        {
            return $"Item {Id} takes codes from code-system version {Terminology.CodeSystemVersionId}, not {coded.CodeSystemVersionId}.";
        }

        return null;
    }
}

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

    /// <summary>A strategy stated only in words (<see cref="IntercurrentEvent.StrategyText"/>), as USDM allows.</summary>
    Other,
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
