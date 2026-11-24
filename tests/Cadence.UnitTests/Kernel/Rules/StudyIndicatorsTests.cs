using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Indicators;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Indicators;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// A study's Pulse (ADR-2488 §4.6, §4.7): the pinned indicator sets are evaluated over the populations the registered
/// sources read, and records the reader may not see are left out before counting, so a value never reveals them.
/// </summary>
public sealed class StudyIndicatorsTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly Actor Reader = Actor.Person("monitor@cadence.test");
    private static readonly DateTimeOffset AsOf = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Evaluate_CountsThePinnedIndicatorsOverTheSourcesRecords()
    {
        var enrollments = Source(KernelPopulations.Enrollments, Enrollment("Enrolled"), Enrollment("ScreenFailed"), Enrollment("Randomized"), Enrollment("Enrolled"));
        var findings = Source(KernelPopulations.ValidationFindings, Finding("Open", "Error"), Finding("Resolved", "Error"));

        var values = await EvaluateAsync(Pinned(), [enrollments, findings], new Masking());

        values.Study.Should().Be(NodeRef.For(Study));
        values.AsOf.Should().Be(AsOf);
        values.Indicators.Select(v => (v.Result.Indicator.IndicatorId, v.Result.Value, v.Result.Breached, v.Partial)).Should().Equal(
            ("pulse.enrolled", 3m, false, false),
            ("pulse.screen_failure_rate", 25m, false, false),
            ("qtl.open_error_findings", 1m, true, false));
        values.Indicators.Should().OnlyContain(v => v.HiddenReasons.Count == 0);
        enrollments.Asked.Should().ContainSingle().Which.Should().Be((Study, AsOf, StudyIndicators.MaxRecords));
    }

    /// <summary>
    /// A record the reader may not see is not counted and not listed as a contributor. The view says the value is
    /// partial and why, never how many were left out; indicators over other populations are unaffected.
    /// </summary>
    [Fact]
    public async Task Evaluate_LeavesOutRecordsTheReaderMayNotSee()
    {
        var otherSite = Enrollment("ScreenFailed");
        var blinded = Enrollment("Enrolled");
        var enrollments = Source(KernelPopulations.Enrollments, Enrollment("Enrolled"), Enrollment("Enrolled"), otherSite, blinded);
        var findings = Source(KernelPopulations.ValidationFindings, Finding("Open", "Error"));
        var masking = new Masking { [otherSite.Node] = MaskReason.NotPermitted, [blinded.Node] = MaskReason.Blinded };

        var values = await EvaluateAsync(Pinned(), [enrollments, findings], masking);

        var enrolled = values.Indicators[0];
        enrolled.Result.Value.Should().Be(2m);
        enrolled.Result.Population.Should().Be(2, "hidden records are not in the denominator either");
        enrolled.Result.Contributors.Should().NotContain([otherSite.Node, blinded.Node]);
        enrolled.Partial.Should().BeTrue();
        enrolled.HiddenReasons.Should().Equal(MaskReason.NotPermitted, MaskReason.Blinded);
        values.Indicators[1].Result.Value.Should().Be(0m, "the screen failure is at a site the reader may not see");
        values.Indicators[1].Result.Breached.Should().BeFalse();
        values.Indicators[2].Partial.Should().BeFalse();
        masking.Calls.Should().Be(1, "visibility is asked once per evaluation");
    }

    [Fact]
    public async Task Evaluate_RefusesAPopulationNoSourceProvides()
    {
        var result = await Service(Pinned(), [Source(KernelPopulations.Enrollments)], new Masking())
            .EvaluateAsync(Tenant, Reader, Study, AsOf, TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(IndicatorErrors.PopulationUnknown.Code);
        result.Error.Message.Should().Contain(KernelPopulations.ValidationFindings);
    }

    [Fact]
    public async Task Evaluate_RefusesAPopulationLargerThanOneEvaluationReads()
    {
        var truncated = Source(KernelPopulations.Enrollments) with { Truncated = true };

        var result = await Service(Pinned(), [truncated, Source(KernelPopulations.ValidationFindings)], new Masking())
            .EvaluateAsync(Tenant, Reader, Study, AsOf, TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(IndicatorErrors.PopulationTooLarge.Code);
    }

    [Fact]
    public async Task Evaluate_AStudyWithNoPinnedSetsHasNoIndicatorsAndReadsNothing()
    {
        var enrollments = Source(KernelPopulations.Enrollments, Enrollment("Enrolled"));
        var masking = new Masking();

        var values = await EvaluateAsync(new Registry(null), [enrollments], masking);

        values.Indicators.Should().BeEmpty();
        enrollments.Asked.Should().BeEmpty();
        masking.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_WrapsPopulationRecordMapsWithoutCopyingAndCapsContributors()
    {
        var nonEnumerableMaps = Enumerable.Range(0, IndicatorEvaluator.MaxContributors + 50)
            .Select(i => new NonEnumerableVariables(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["STATUS"] = i % 5 == 0 ? "ScreenFailed" : "Enrolled",
            }))
            .ToArray();
        var enrollmentRecords = nonEnumerableMaps
            .Select(m => new PopulationRecord(NodeRef.For(EnrollmentId.New()), m))
            .ToArray();
        var masked = enrollmentRecords[^1];
        var masking = new Masking { [masked.Node] = MaskReason.Blinded };
        var findings = Source(KernelPopulations.ValidationFindings, Finding("Open", "Error"));

        var values = await EvaluateAsync(Pinned(), [Source(KernelPopulations.Enrollments, enrollmentRecords), findings], masking);

        // StudyIndicators wraps PopulationRecord.Values directly instead of copying into a detached Dictionary<string, RuleValue>:
        // rule evaluation reads STATUS directly from each visible source map via TryGetValue, and skips the masked map completely.
        nonEnumerableMaps[..^1].Should().OnlyContain(m => m.TryGetValueCalls > 0);
        nonEnumerableMaps[^1].TryGetValueCalls.Should().Be(0);
        nonEnumerableMaps[^1].EnumeratorCalls.Should().Be(0);

        var enrolled = values.Indicators[0];
        enrolled.Partial.Should().BeTrue();
        enrolled.HiddenReasons.Should().Equal(MaskReason.Blinded);
        enrolled.Result.Population.Should().Be(enrollmentRecords.Length - 1);
        enrolled.Result.Contributors.Should().HaveCount(IndicatorEvaluator.MaxContributors);
        enrolled.Result.Contributors.Should().NotContain(masked.Node);

        // Verify InputHash and ResultHash match direct IndicatorEvaluator evaluation over copied dictionaries.
        var directSet = new LoadedIndicatorSet(
            enrolled.Result.Indicator.SetId, "sample-pulse", 1, new string('a', 64), GovernanceStatus.Approved, SampleIndicatorSets.Pulse());
        var visibleDirectEnrollments = enrollmentRecords
            .Where(r => r.Node != masked.Node)
            .Select(r => new IndicatorRecord(
                r.Node,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["STATUS"] = r.Values["STATUS"] is { } s ? RuleValue.FromText(s) : RuleValue.Null,
                }))
            .ToList();
        var directFindings = findings.Records
            .Select(r => new IndicatorRecord(
                r.Node,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["STATUS"] = RuleValue.FromText(r.Values["STATUS"]!),
                    ["SEVERITY"] = RuleValue.FromText(r.Values["SEVERITY"]!),
                }))
            .ToList();
        var directResults = new IndicatorEvaluator(new DotNetRuleEvaluator()).Evaluate(
            directSet,
            new IndicatorContext(NodeRef.For(Study), DateOnly.FromDateTime(AsOf.UtcDateTime)),
            new Dictionary<string, IReadOnlyList<IndicatorRecord>>(StringComparer.Ordinal)
            {
                [KernelPopulations.Enrollments] = visibleDirectEnrollments,
                [KernelPopulations.ValidationFindings] = directFindings,
            });

        values.Indicators.Select(i => (i.Result.InputHash, i.Result.ResultHash, i.Result.Counted))
            .Should().Equal(directResults.Select(r => (r.InputHash, r.ResultHash, r.Counted)));
        values.Indicators[0].Result.Contributors.Should().Equal(directResults[0].Contributors);
    }

    private static async Task<StudyIndicatorValues> EvaluateAsync(Registry registry, IEnumerable<IIndicatorPopulationSource> sources, Masking masking)
    {
        var result = await Service(registry, sources, masking).EvaluateAsync(Tenant, Reader, Study, AsOf, TestContext.Current.CancellationToken);
        result.Error.Should().BeNull();
        return result.Value!;
    }

    private static StudyIndicators Service(Registry registry, IEnumerable<IIndicatorPopulationSource> sources, Masking masking) =>
        new(new IndicatorSetLoader(registry), new IndicatorEvaluator(new DotNetRuleEvaluator()), sources, masking);

    private static Registry Pinned()
    {
        var content = IndicatorSetJson.ToJson(SampleIndicatorSets.Pulse());
        var item = new LibraryItemView(
            LibraryItemId.New(), LibraryItemKind.IndicatorSet, "sample-pulse", 1, "Sample Study Pulse", content, new string('a', 64),
            GovernanceStatus.Approved, [], AttestationId.New(), null);
        return new Registry(item);
    }

    private static FakeSource Source(string population, params PopulationRecord[] records) => new(population, records);

    private static PopulationRecord Enrollment(string status) =>
        new(NodeRef.For(EnrollmentId.New()), new Dictionary<string, string?>(StringComparer.Ordinal) { ["STATUS"] = status });

    private static PopulationRecord Finding(string status, string severity) =>
        new(new NodeRef(NodeKinds.ValidationFinding, Guid.NewGuid()),
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["STATUS"] = status, ["SEVERITY"] = severity });

    private sealed record FakeSource(string Population, IReadOnlyList<PopulationRecord> Records) : IIndicatorPopulationSource
    {
        public bool Truncated { get; init; }

        public List<(StudyId Study, DateTimeOffset AsOf, int Limit)> Asked { get; } = [];

        public Task<PopulationRead> ReadAsync(TenantId tenant, StudyId study, DateTimeOffset asOf, int limit, CancellationToken cancellationToken)
        {
            Asked.Add((study, asOf, limit));
            return Task.FromResult(new PopulationRead(Records, Truncated));
        }
    }

    private sealed class Masking : Dictionary<NodeRef, MaskReason>, INodeVisibility
    {
        public int Calls { get; private set; }

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(
                nodes.Where(ContainsKey).ToDictionary(n => n, n => this[n]));
        }
    }

    private sealed class Registry(LibraryItemView? pinned) : IStandardsRegistry
    {
        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(pinned?.Id == id ? pinned : null);

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult(pinned is null
                ? null
                : new StudyStandardsProfileView(StudyStandardsProfileId.New(), study, 1, [], [], [pinned.Id]));

        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NonEnumerableVariables(IReadOnlyDictionary<string, string?> inner) : IReadOnlyDictionary<string, string?>
    {
        public int EnumeratorCalls { get; private set; }

        public int TryGetValueCalls { get; private set; }

        public string? this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<string?> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string? value)
        {
            TryGetValueCalls++;
            return inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
        {
            EnumeratorCalls++;
            return inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
