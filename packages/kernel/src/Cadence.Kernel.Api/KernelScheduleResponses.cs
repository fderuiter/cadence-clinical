using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>
/// A protocol version's Schedule of Activities (ADR-2475): planned activities as columns grouped by epoch, collection
/// specifications as rows. It is compiled from the version's design on every read and never stored.
/// </summary>
/// <param name="Version">The NodeRef of the protocol version.</param>
/// <param name="ContentHash">The version's content hash, once it has been submitted for review.</param>
/// <param name="Hash">The hash of the matrix itself, the same for the same design.</param>
/// <param name="Title">The protocol's title.</param>
/// <param name="Epochs">The epochs that have columns, in epoch order, each with its span of columns.</param>
/// <param name="Columns">The planned activities; timed ones by study day within their epoch, then event-driven ones.</param>
/// <param name="Rows">The collection specifications, in design order.</param>
/// <param name="Footnotes">The footnotes the columns and rows refer to.</param>
public sealed record KernelScheduleOfActivitiesResponse(
    string Version,
    string? ContentHash,
    string Hash,
    string Title,
    IReadOnlyList<KernelSoaEpochResponse> Epochs,
    IReadOnlyList<KernelSoaColumnResponse> Columns,
    IReadOnlyList<KernelSoaRowResponse> Rows,
    IReadOnlyList<KernelSoaFootnoteResponse> Footnotes)
{
    internal static KernelScheduleOfActivitiesResponse From(ProtocolVersionSummary version, SoaMatrix matrix) =>
        new(
            NodeRef.For(version.Id).ToString(),
            version.ContentHash,
            ScheduleOfActivities.Hash(matrix),
            matrix.Title,
            [.. matrix.Epochs.Select(e => new KernelSoaEpochResponse(e.EpochId, e.Name, e.Order, e.FirstColumn, e.ColumnCount))],
            [.. matrix.Columns.Select(c => new KernelSoaColumnResponse(
                c.ActivityId, c.Name, c.EpochId, c.StudyDay, c.WindowBefore, c.WindowAfter, [.. c.Locations.Select(l => l.ToString())], c.Footnotes))],
            [.. matrix.Rows.Select(r => new KernelSoaRowResponse(
                r.CollectionSpecId, r.Name, r.ItemIds, KernelSoaConcepts.Of(r.Concepts), r.Columns, r.Frequency, r.Footnotes))],
            [.. matrix.Footnotes.Select(f => new KernelSoaFootnoteResponse(f.Number, f.Kind.ToString(), f.Text))]);
}

/// <summary>An epoch's header over its columns.</summary>
/// <param name="EpochId">The epoch's element id.</param>
/// <param name="Name">The epoch's name.</param>
/// <param name="Order">The epoch's order in the design.</param>
/// <param name="FirstColumn">The index of its first column.</param>
/// <param name="ColumnCount">How many columns it spans.</param>
public sealed record KernelSoaEpochResponse(string EpochId, string Name, int Order, int FirstColumn, int ColumnCount);

/// <summary>A column: one planned activity.</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Name">Its name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="StudyDay">The planned study day, or null for an event-driven activity.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
/// <param name="Locations">Where it may take place: <c>Site</c>, <c>Home</c>, <c>Telehealth</c>, <c>LocalFacility</c>, <c>Remote</c>.</param>
/// <param name="Footnotes">The numbers of its footnotes.</param>
public sealed record KernelSoaColumnResponse(
    string ActivityId,
    string Name,
    string EpochId,
    int? StudyDay,
    int WindowBefore,
    int WindowAfter,
    IReadOnlyList<string> Locations,
    IReadOnlyList<int> Footnotes);

/// <summary>A row: one collection specification and the columns that collect it.</summary>
/// <param name="CollectionSpecId">The collection specification's element id.</param>
/// <param name="Name">Its name.</param>
/// <param name="ItemIds">Its items' element ids.</param>
/// <param name="Concepts">The pinned NodeRefs of the concept versions its items record.</param>
/// <param name="Columns">The indexes of the columns that collect it, ascending.</param>
/// <param name="Frequency">How many planned activities collect it.</param>
/// <param name="Footnotes">The numbers of its footnotes.</param>
public sealed record KernelSoaRowResponse(
    string CollectionSpecId,
    string Name,
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<int> Columns,
    int Frequency,
    IReadOnlyList<int> Footnotes);

/// <summary>A footnote generated from the design.</summary>
/// <param name="Number">Its number, from 1.</param>
/// <param name="Kind"><c>Window</c>, <c>Location</c>, <c>EventDriven</c>, <c>Purpose</c> or <c>NotScheduled</c>.</param>
/// <param name="Text">Its text.</param>
public sealed record KernelSoaFootnoteResponse(int Number, string Kind, string Text);

/// <summary>
/// The Schedule of Activities and eligibility changes an amendment makes (ADR-2475), from which its impacted elements
/// and transition policy are chosen.
/// </summary>
/// <param name="Directives">The changes: visits, then activities, then criteria.</param>
/// <param name="ImpactedElements">Every element the directives touch, sorted.</param>
/// <param name="ImpactedConcepts">The pinned NodeRefs of every concept version the directives touch.</param>
/// <param name="Undeclared">The impacted elements the amendment does not declare.</param>
/// <param name="ChangesSchedule">True when what participants already on study do, or when, changes.</param>
/// <param name="ChangesEligibility">True when who may enter changes, which concerns only new participants.</param>
public sealed record KernelSoaAmendmentDiffResponse(
    IReadOnlyList<KernelSoaDirectiveResponse> Directives,
    IReadOnlyList<string> ImpactedElements,
    IReadOnlyList<string> ImpactedConcepts,
    IReadOnlyList<string> Undeclared,
    bool ChangesSchedule,
    bool ChangesEligibility)
{
    internal static KernelSoaAmendmentDiffResponse From(SoaAmendmentDiff diff, IEnumerable<string> declared) =>
        new(
            [.. diff.Directives.Select(KernelSoaDirectiveResponse.From)],
            diff.ImpactedElements,
            KernelSoaConcepts.Of(diff.ImpactedConcepts),
            diff.Undeclared(declared),
            diff.ChangesSchedule,
            diff.ChangesEligibility);
}

/// <summary>One typed change between two Schedules of Activities.</summary>
/// <param name="Kind">
/// <c>VisitAdded</c>, <c>VisitRemoved</c>, <c>VisitMoved</c>, <c>VisitWindowChanged</c>, <c>VisitLocationsChanged</c>,
/// <c>ActivityAdded</c>, <c>ActivityRemoved</c>, <c>ActivityFrequencyChanged</c>, <c>ActivityConceptsChanged</c>,
/// <c>CriterionAdded</c>, <c>CriterionRemoved</c> or <c>CriterionChanged</c>.
/// </param>
/// <param name="ElementId">The planned activity, collection specification or criterion that changed.</param>
/// <param name="ImpactedElements">Every element the change touches, this one included.</param>
/// <param name="ImpactedConcepts">The pinned NodeRefs of the concept versions it touches.</param>
/// <param name="Before">For a visit directive, the visit before the change.</param>
/// <param name="After">For a visit directive, the visit after it.</param>
/// <param name="FrequencyBefore">For a frequency change, how many activities collected it.</param>
/// <param name="FrequencyAfter">For a frequency change, how many collect it now.</param>
/// <param name="CollectedAtAdded">For a frequency change, the activities that now collect it.</param>
/// <param name="CollectedAtRemoved">For a frequency change, the activities that no longer do.</param>
public sealed record KernelSoaDirectiveResponse(
    string Kind,
    string ElementId,
    IReadOnlyList<string> ImpactedElements,
    IReadOnlyList<string> ImpactedConcepts,
    KernelSoaVisitResponse? Before,
    KernelSoaVisitResponse? After,
    int? FrequencyBefore,
    int? FrequencyAfter,
    IReadOnlyList<string>? CollectedAtAdded,
    IReadOnlyList<string>? CollectedAtRemoved)
{
    internal static KernelSoaDirectiveResponse From(SoaDirective directive) =>
        new(
            directive.Kind.ToString(),
            directive.ElementId,
            directive.ImpactedElements,
            KernelSoaConcepts.Of(directive.ImpactedConcepts),
            KernelSoaVisitResponse.From(directive.Before),
            KernelSoaVisitResponse.From(directive.After),
            directive.FrequencyBefore,
            directive.FrequencyAfter,
            directive.CollectedAtAdded,
            directive.CollectedAtRemoved);
}

/// <summary>When and where a planned activity happens.</summary>
/// <param name="EpochId">Its epoch.</param>
/// <param name="StudyDay">Its planned study day, or null when event-driven.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
/// <param name="Locations">Where it may take place.</param>
public sealed record KernelSoaVisitResponse(string EpochId, int? StudyDay, int WindowBefore, int WindowAfter, IReadOnlyList<string> Locations)
{
    internal static KernelSoaVisitResponse? From(SoaVisitState? state) =>
        state is null
            ? null
            : new(state.EpochId, state.StudyDay, state.WindowBefore, state.WindowAfter, [.. state.Locations.Select(l => l.ToString())]);
}

/// <summary>A pending re-consent transition blocking new clinical capture on an enrollment (PRD-SUB-007).</summary>
/// <param name="TargetVersionRef">The amended <c>protocol_version</c> NodeRef awaiting re-consent.</param>
/// <param name="TargetVersionNumber">The amended version's 1-indexed version number, when visible.</param>
/// <param name="AmendmentIntent">The amended version's amendment intent summary, when visible.</param>
/// <param name="SourceEffectivityRef">The <c>effectivity</c> NodeRef that initiated the transition.</param>
/// <param name="RequiredSince">When the re-consent requirement took effect in valid time.</param>
public sealed record KernelPendingReconsentResponse(
    string TargetVersionRef,
    int? TargetVersionNumber,
    string? AmendmentIntent,
    string SourceEffectivityRef,
    DateTimeOffset RequiredSince);

/// <summary>
/// An enrollment's bitemporally resolved governing protocol version, compiled Schedule of Activities, and
/// re-consent lock state at a valid-time point (spec §7.5, K-TIME-6, PRD-SUB-007).
/// </summary>
/// <param name="EnrollmentRef">The enrollment's canonical NodeRef.</param>
/// <param name="GoverningVersion">Summary of the enrollment's governing protocol version, or null when none applies.</param>
/// <param name="ScheduleOfActivities">Compiled Schedule of Activities of the governing protocol version, when available.</param>
/// <param name="RequiresReconsent">True when one or more pending <c>RequiresReconsent</c> transitions block new capture.</param>
/// <param name="IsInconsistent">True when K-TIME-6 detected overlapping governing protocol versions requiring resolution.</param>
/// <param name="InconsistencyReason">Explanation of any K-TIME-6 inconsistency, or null when consistent.</param>
/// <param name="PendingTransitions">Unresolved <c>RequiresReconsent</c> transitions for this enrollment.</param>
public sealed record KernelEnrollmentScheduleResponse(
    string EnrollmentRef,
    KernelProtocolVersionResponse? GoverningVersion,
    KernelScheduleOfActivitiesResponse? ScheduleOfActivities,
    bool RequiresReconsent,
    bool IsInconsistent,
    string? InconsistencyReason,
    IReadOnlyList<KernelPendingReconsentResponse> PendingTransitions);

/// <summary>Concept versions as the pinned NodeRefs the Canvas navigates to.</summary>
internal static class KernelSoaConcepts
{
    internal static IReadOnlyList<string> Of(IEnumerable<ConceptRef> concepts) =>
        [.. concepts.Select(c => NodeRef.For(c.ConceptId, c.Version).ToString())];
}
