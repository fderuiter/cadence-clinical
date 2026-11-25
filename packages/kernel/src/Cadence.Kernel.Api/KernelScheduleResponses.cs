using System;
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
                c.ActivityId, c.Name, c.EpochId, c.StudyDay, c.WindowBefore, c.WindowAfter, [.. c.Locations.Select(l => l.ToString())], c.Footnotes, c.Repeating))],
            [.. matrix.Rows.Select(r => new KernelSoaRowResponse(
                r.CollectionSpecId, r.Name, r.ItemIds, KernelSoaConcepts.Of(r.Concepts), r.Columns, r.Frequency, r.Footnotes, r.Repeating))],
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
/// <param name="Repeating">Whether multiple instances of this planned activity may occur for one enrollment.</param>
public sealed record KernelSoaColumnResponse(
    string ActivityId,
    string Name,
    string EpochId,
    int? StudyDay,
    int WindowBefore,
    int WindowAfter,
    IReadOnlyList<string> Locations,
    IReadOnlyList<int> Footnotes,
    bool Repeating = false);

/// <summary>A row: one collection specification and the columns that collect it.</summary>
/// <param name="CollectionSpecId">The collection specification's element id.</param>
/// <param name="Name">Its name.</param>
/// <param name="ItemIds">Its items' element ids.</param>
/// <param name="Concepts">The pinned NodeRefs of the concept versions its items record.</param>
/// <param name="Columns">The indexes of the columns that collect it, ascending.</param>
/// <param name="Frequency">How many planned activities collect it.</param>
/// <param name="Footnotes">The numbers of its footnotes.</param>
/// <param name="Repeating">Whether multiple instances of this collection specification may be opened per planned activity or as a repeating log.</param>
public sealed record KernelSoaRowResponse(
    string CollectionSpecId,
    string Name,
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<int> Columns,
    int Frequency,
    IReadOnlyList<int> Footnotes,
    bool Repeating = false);

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

/// <summary>One timed planned activity placed on the participant's calendar by <see cref="ExpectedSchedule.Compute"/> (ADR-2487).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Name">The activity's display name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="EpochOrder">The epoch's order in the design.</param>
/// <param name="StudyDay">Normalized planned study day (day 1 is the reference start; no day 0).</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
/// <param name="TargetDate">Planned calendar date in site time.</param>
/// <param name="WindowStart">First allowed calendar date in site time.</param>
/// <param name="WindowEnd">Last allowed calendar date in site time.</param>
/// <param name="WindowStatus">Window status at the evaluation date: <c>NotYetOpen</c>, <c>Open</c>, or <c>Closed</c>.</param>
public sealed record KernelExpectedVisitResponse(
    string ActivityId,
    string Name,
    string EpochId,
    int EpochOrder,
    int StudyDay,
    int WindowBefore,
    int WindowAfter,
    DateOnly TargetDate,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    string WindowStatus)
{
    internal static KernelExpectedVisitResponse From(ExpectedVisit expected) =>
        new(
            expected.Visit.ActivityId,
            expected.Visit.Name,
            expected.Visit.EpochId,
            expected.Visit.EpochOrder,
            expected.Visit.StudyDay,
            expected.Visit.WindowBefore,
            expected.Visit.WindowAfter,
            expected.Target,
            expected.WindowStart,
            expected.WindowEnd,
            expected.Status.ToString());
}

/// <summary>
/// Server-computed participant casebook schedule for an enrollment under its study site's governing protocol version
/// (ADR-2475, ADR-2487, K-TIME-7, K-TIME-8). Pure projection: nothing is persisted on read.
/// </summary>
/// <param name="EnrollmentRef">Canonical NodeRef of the enrollment.</param>
/// <param name="StudyRef">Canonical NodeRef of the study.</param>
/// <param name="StudySiteRef">Canonical NodeRef of the enrollment's linked study site.</param>
/// <param name="SiteCode">Human-facing site code, when resolved.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code of the site, when resolved.</param>
/// <param name="EnrollmentStatus">Current enrollment lifecycle status.</param>
/// <param name="StatusSince">Valid-time instant when the enrollment status began.</param>
/// <param name="ReferenceDate">Reference start date (study day 1) in site calendar time.</param>
/// <param name="AsOfDate">Evaluation date in site calendar time used for window statuses.</param>
/// <param name="GovernanceStatus"><c>Effective</c> when a protocol version governs the site, or <c>NoEffectiveVersion</c> when none is in force.</param>
/// <param name="LatestStudyVersionStatus">Lifecycle status of the latest visible protocol version in the study (for example <c>Draft</c>), when known.</param>
/// <param name="Guidance">Actionable governance guidance when no protocol version is effective at the site.</param>
/// <param name="GoverningVersion">The highest-numbered protocol version effective at the enrollment's study site, or null when none is effective.</param>
/// <param name="EffectiveVersions">All protocol versions currently effective at the enrollment's study site, highest number first.</param>
/// <param name="Schedule">Compiled Schedule of Activities of the governing protocol version, or null when no version governs the site.</param>
/// <param name="ExpectedVisits">Expected calendar dates and window statuses computed by <see cref="ExpectedSchedule.Compute"/>.</param>
public sealed record KernelEnrollmentScheduleResponse(
    string EnrollmentRef,
    string StudyRef,
    string StudySiteRef,
    string? SiteCode,
    string? Country,
    string EnrollmentStatus,
    DateTimeOffset StatusSince,
    DateOnly ReferenceDate,
    DateOnly AsOfDate,
    string GovernanceStatus,
    string? LatestStudyVersionStatus,
    string? Guidance,
    KernelEffectiveVersionResponse? GoverningVersion,
    IReadOnlyList<KernelEffectiveVersionResponse> EffectiveVersions,
    KernelScheduleOfActivitiesResponse? Schedule,
    IReadOnlyList<KernelExpectedVisitResponse> ExpectedVisits);

/// <summary>Concept versions as the pinned NodeRefs the Canvas navigates to.</summary>
internal static class KernelSoaConcepts
{
    internal static IReadOnlyList<string> Of(IEnumerable<ConceptRef> concepts) =>
        [.. concepts.Select(c => NodeRef.For(c.ConceptId, c.Version).ToString())];
}
