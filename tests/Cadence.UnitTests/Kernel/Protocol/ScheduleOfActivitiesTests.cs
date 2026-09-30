using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Protocol;

/// <summary>
/// The Schedule of Activities (ADR-2475): a deterministic matrix compiled from a protocol design, and the typed
/// schedule and eligibility directives between two designs that an amendment's impacted elements are chosen from.
/// </summary>
public sealed class ScheduleOfActivitiesTests
{
    /// <summary>
    /// The CDISC USDM 4 example study compiles to the checked-in matrix. Set <c>CADENCE_UPDATE_GOLDEN=1</c> to rewrite
    /// the expected files after an intended change, then review their diff.
    /// </summary>
    [Fact(DisplayName = "Compile_CdiscExample_MatchesTheExpectedMatrix")]
    public Task CompileCdiscExampleMatchesTheExpectedMatrix() =>
        MatchesGoldenAsync("cdisc_usdm4_full_1.soa.json", ScheduleOfActivities.Write(ScheduleOfActivities.Compile(SoaSamples.CdiscExample())));

    /// <summary>The CARDIO-2 schedule compiles to the checked-in matrix.</summary>
    [Fact(DisplayName = "Compile_Cardio_MatchesTheExpectedMatrix")]
    public Task CompileCardioMatchesTheExpectedMatrix() =>
        MatchesGoldenAsync("cardio_2.soa.json", ScheduleOfActivities.Write(ScheduleOfActivities.Compile(SoaSamples.Cardio())));

    /// <summary>The CDISC example and its amendment compare to the checked-in directives.</summary>
    [Fact(DisplayName = "Compare_CdiscAmendment_MatchesTheExpectedDirectives")]
    public Task CompareCdiscAmendmentMatchesTheExpectedDirectives() =>
        MatchesGoldenAsync(
            "cdisc_usdm4_full_1.amendment.diff.json",
            ScheduleOfActivitiesDiff.Write(ScheduleOfActivitiesDiff.Compare(SoaSamples.CdiscExample(), SoaSamples.CdiscAmendment())));

    /// <summary>
    /// The same content gives byte-identical output: a design read back from its own document compiles to the same text
    /// and the same content hash, which is the hash of that text.
    /// </summary>
    [Fact(DisplayName = "Compile_SameContent_GivesByteIdenticalOutput")]
    public void CompileSameContentGivesByteIdenticalOutput()
    {
        var content = ProtocolDesignDocument.Write(SoaSamples.Cardio());

        var first = ScheduleOfActivities.Write(ScheduleOfActivities.Compile(ProtocolDesignDocument.Read(content).Design!));
        var second = ScheduleOfActivities.Write(ScheduleOfActivities.Compile(ProtocolDesignDocument.Read(content).Design!));

        second.Should().Be(first);
        ScheduleOfActivities.Hash(ScheduleOfActivities.Compile(SoaSamples.Cardio())).Should().Be(ContentHash.Compute(first));
        first.Should().NotContain("\r");
    }

    /// <summary>
    /// Columns go epoch by epoch in epoch order, whatever order the design lists them in; inside an epoch, timed
    /// activities by study day, then event-driven ones. Each epoch header spans its own columns.
    /// </summary>
    [Fact(DisplayName = "Compile_GroupsColumnsByEpochAndStudyDay")]
    public void CompileGroupsColumnsByEpochAndStudyDay()
    {
        var matrix = ScheduleOfActivities.Compile(SoaSamples.Cardio());

        matrix.Columns.Select(c => c.ActivityId).Should().Equal(
            "visit.screening", "visit.baseline", "visit.week4", "visit.week8", "visit.week12", "visit.et");
        matrix.Epochs.Should().Equal(
            new SoaEpoch("epoch.screening", "Screening", 1, 0, 1),
            new SoaEpoch("epoch.treatment", "Treatment", 2, 1, 5));
        matrix.Columns[^1].StudyDay.Should().BeNull();
    }

    /// <summary>
    /// A row marks the columns that collect it and counts them; footnotes say what the grid cannot: windows, places
    /// other than the site, event-driven visits, operational purposes and rows nothing collects. The same text is one
    /// footnote, numbered in order of first use.
    /// </summary>
    [Fact(DisplayName = "Compile_MarksCellsAndFootnotes")]
    public void CompileMarksCellsAndFootnotes()
    {
        var matrix = ScheduleOfActivities.Compile(SoaSamples.Cardio());
        var row = matrix.Rows.ToDictionary(r => r.CollectionSpecId);

        row["cs.vitals"].Columns.Should().Equal(0, 1, 2, 3, 4, 5);
        row["cs.labs"].Columns.Should().Equal(1, 3, 4, 5);
        row["cs.labs"].Frequency.Should().Be(4);
        row["cs.pk"].Frequency.Should().Be(0);
        row["cs.vitals"].Concepts.Should().Equal(SoaSamples.Sbp);

        matrix.Footnotes.Should().Equal(
            new SoaFootnote(1, SoaFootnoteKind.Window, "Window: 7 days before, 0 days after."),
            new SoaFootnote(2, SoaFootnoteKind.Location, "May take place: by telehealth."),
            new SoaFootnote(3, SoaFootnoteKind.Window, "Window: 3 days either side."),
            new SoaFootnote(4, SoaFootnoteKind.Location, "May take place: at the site, at home."),
            new SoaFootnote(5, SoaFootnoteKind.EventDriven, "Not on a fixed day: done when the event that triggers it occurs."),
            new SoaFootnote(6, SoaFootnoteKind.Purpose, "Collected for: Documenting consent before any study procedure"),
            new SoaFootnote(7, SoaFootnoteKind.NotScheduled, "No planned activity collects it."));
        matrix.Columns.Select(c => c.Footnotes).Should().BeEquivalentTo(
            new[] { new[] { 1, 2 }, [], [3, 4], [3], [3], [5] }, o => o.WithStrictOrdering());
        row["cs.consent"].Footnotes.Should().Equal(6);
        row["cs.pk"].Footnotes.Should().Equal(7);
    }

    /// <summary>
    /// Each change becomes one typed directive with the elements and concept versions it touches: a moved visit and its
    /// new place, a widened window, a new visit, a change in how often an activity is collected and where, a reworded
    /// criterion with the population it defines, a new criterion. The diff says it changes the schedule of participants
    /// on study and eligibility, and which impacted elements an amendment's declaration leaves out.
    /// </summary>
    [Fact(DisplayName = "Compare_EmitsTypedDirectivesForAnAmendment")]
    public void CompareEmitsTypedDirectivesForAnAmendment()
    {
        var diff = ScheduleOfActivitiesDiff.Compare(SoaSamples.CdiscExample(), SoaSamples.CdiscAmendment());

        diff.Directives.Select(d => (d.Kind, d.ElementId)).Should().Equal(
            (SoaDirectiveKind.VisitWindowChanged, "ScheduledActivityInstance_4"),
            (SoaDirectiveKind.VisitMoved, "ScheduledActivityInstance_6"),
            (SoaDirectiveKind.VisitLocationsChanged, "ScheduledActivityInstance_6"),
            (SoaDirectiveKind.VisitAdded, "ScheduledActivityInstance_7"),
            (SoaDirectiveKind.ActivityFrequencyChanged, "Activity_2"),
            (SoaDirectiveKind.ActivityFrequencyChanged, "Activity_3"),
            (SoaDirectiveKind.CriterionChanged, "EligibilityCriterion_4"),
            (SoaDirectiveKind.CriterionAdded, "EligibilityCriterion_6"));

        var moved = diff.Directives.Single(d => d.Kind == SoaDirectiveKind.VisitMoved);
        (moved.Before!.StudyDay, moved.After!.StudyDay).Should().Be((43, 57));
        var weight = diff.Directives.Single(d => d.ElementId == "Activity_3");
        (weight.FrequencyBefore, weight.FrequencyAfter).Should().Be((2, 1));
        weight.CollectedAtRemoved.Should().Equal("ScheduledActivityInstance_4");
        weight.CollectedAtAdded.Should().BeEmpty();
        weight.ImpactedElements.Should().Equal("Activity_3", "ScheduledActivityInstance_4");
        diff.Directives.Single(d => d.ElementId == "EligibilityCriterion_4").ImpactedElements
            .Should().Equal("EligibilityCriterion_4", "StudyDesignPopulation_1");

        diff.ChangesSchedule.Should().BeTrue();
        diff.ChangesEligibility.Should().BeTrue();
        diff.Undeclared(diff.ImpactedElements.Where(e => !e.StartsWith("Eligibility", StringComparison.Ordinal)))
            .Should().Equal("EligibilityCriterion_4", "EligibilityCriterion_6");
    }

    /// <summary>
    /// Against no design every visit, activity and criterion is added; a design against itself has no directives; a
    /// changed name alone is left to the structural diff; a rule-only change touches eligibility but not the schedule.
    /// </summary>
    [Fact(DisplayName = "Compare_EdgeCases")]
    public void CompareEdgeCases()
    {
        var design = SoaSamples.Cardio();

        var fresh = ScheduleOfActivitiesDiff.Compare(null, design);
        fresh.Directives.Should().HaveCount(design.PlannedActivities.Count + design.CollectionSpecs.Count + design.EligibilityCriteria.Count)
            .And.OnlyContain(d => d.Kind == SoaDirectiveKind.VisitAdded || d.Kind == SoaDirectiveKind.ActivityAdded || d.Kind == SoaDirectiveKind.CriterionAdded);

        ScheduleOfActivitiesDiff.Compare(design, design).Directives.Should().BeEmpty();
        ScheduleOfActivitiesDiff.Compare(design, design with
        {
            PlannedActivities = [.. design.PlannedActivities.Select(a => a with { Name = a.Name + " (renamed)" })],
        }).Directives.Should().BeEmpty();

        var eligibilityOnly = ScheduleOfActivitiesDiff.Compare(design, design with
        {
            EligibilityCriteria = [design.EligibilityCriteria[0] with { Expression = "sbp >= 140" }],
        });
        eligibilityOnly.Directives.Should().ContainSingle().Which.Kind.Should().Be(SoaDirectiveKind.CriterionChanged);
        (eligibilityOnly.ChangesSchedule, eligibilityOnly.ChangesEligibility).Should().Be((false, true));

        var items = ScheduleOfActivitiesDiff.Compare(design, design with
        {
            CollectionSpecs = [.. design.CollectionSpecs.Select(s => s.Id == "cs.labs" ? s with { Items = [.. s.Items, new CollectionItem("item.glucose", SoaSamples.QtInterval)] } : s)],
        });
        items.Directives.Should().ContainSingle().Which.Should().BeEquivalentTo(new SoaDirective(
            SoaDirectiveKind.ActivityConceptsChanged, "cs.labs", ["cs.labs", "item.glucose"], [SoaSamples.QtInterval]));
    }

    /// <summary>
    /// Generating the matrix from a stored version's content (read, compile, write) for the CDISC reference protocol
    /// takes well under 50 ms; the median of 21 runs is checked so one slow run on a busy machine does not decide it.
    /// </summary>
    [Fact(DisplayName = "Compile_ReferenceProtocol_IsFast")]
    public void CompileReferenceProtocolIsFast()
    {
        var content = ProtocolDesignDocument.Write(SoaSamples.CdiscExample());
        ScheduleOfActivities.Write(ScheduleOfActivities.Compile(ProtocolDesignDocument.Read(content).Design!));

        var runs = Enumerable.Range(0, 21).Select(_ =>
        {
            var watch = Stopwatch.StartNew();
            ScheduleOfActivities.Write(ScheduleOfActivities.Compile(ProtocolDesignDocument.Read(content).Design!));
            return watch.Elapsed;
        }).Order().ToList();

        runs[runs.Count / 2].Should().BeLessThan(TimeSpan.FromMilliseconds(50));
    }

    private static async Task MatchesGoldenAsync(string fileName, string actual)
    {
        var path = SoaSamples.PathOf(fileName);
        var text = actual + "\n";
        if (Environment.GetEnvironmentVariable("CADENCE_UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        }

        text.Should().Be((await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n"));
    }
}
