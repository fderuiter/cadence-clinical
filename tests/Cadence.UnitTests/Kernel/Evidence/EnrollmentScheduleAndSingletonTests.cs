using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Evidence;

/// <summary>
/// Unit and HTTP host tests covering singleton cell guards in <c>OpenCollectionInstance</c> and
/// site-scoped governing version + <see cref="ExpectedSchedule.Compute"/> resolution on
/// <c>GET /api/v1/kernel/enrollments/{enrollmentRef}/schedule</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EnrollmentScheduleAndSingletonTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);
    private readonly List<WebApplicationFactory<Cadence.Kernel.Host.Program>> _factories = [];

    /// <summary>Disposes any test host factories created during tests.</summary>
    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    /// <summary>
    /// Opening a second instance of a non-repeating <see cref="CollectionSpec"/> at a non-repeating
    /// <see cref="PlannedActivity"/> fails with <see cref="CollectionErrors.DuplicateSingletonInstance"/> (<c>409</c>),
    /// whereas repeating specs, repeating visits, and unscheduled opens allow multiple instances.
    /// </summary>
    [Fact]
    public async Task OpenCollectionInstanceHandler_EnforcesSingletonCellUniqueness_AndAllowsRepeatingOrUnscheduled()
    {
        var ct = TestContext.Current.CancellationToken;
        var studyId = StudyId.New();
        var studySiteId = StudySiteId.New();
        var rawSiteId = SiteId.New();
        var participantId = ParticipantId.New();
        var enrolledAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

        var design = new ProtocolDesign
        {
            SchemaVersion = 1,
            Title = "Singleton Guard Study",
            Epochs = [new Epoch("ep.treat", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    "visit.singleton",
                    "Week 4",
                    "ep.treat",
                    [ActivityLocation.Site],
                    ["cs.singleton", "cs.repeating"],
                    new ActivityTiming(Day: 28, WindowBefore: 2, WindowAfter: 2),
                    Repeating: false),
                new PlannedActivity(
                    "visit.repeating",
                    "Infusion Cycle",
                    "ep.treat",
                    [ActivityLocation.Site],
                    ["cs.singleton"],
                    new ActivityTiming(Day: 14, WindowBefore: 1, WindowAfter: 1),
                    Repeating: true),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.singleton",
                    "Physical Exam",
                    [new CollectionItem("pe.norm", new ConceptRef(ConceptId.New(), 1))],
                    Repeating: false),
                new CollectionSpec(
                    "cs.repeating",
                    "Vital Signs Repeat",
                    [new CollectionItem("vs.sbp", new ConceptRef(ConceptId.New(), 1))],
                    Repeating: true),
                new CollectionSpec(
                    "cs.unscheduled",
                    "Adverse Event",
                    [new CollectionItem("ae.term", new ConceptRef(ConceptId.New(), 1))],
                    Repeating: true),
            ],
        };

        var version = CreateApprovedProtocolVersion(studyId, 1, design);
        var effectivity = Effectivity.Declare(
            EffectivityId.New(),
            Tenant,
            studyId,
            version.Id,
            new EffectivityScope(EffectivityLevel.StudySite, null, studySiteId),
            enrolledAt.AddDays(-4),
            enrolledAt.AddDays(-4)).Value!;
        effectivity.ChangeStatus(
            EffectivityStatus.Effective,
            [ArtifactVersionId.New()],
            enrolledAt.AddDays(-3),
            enrolledAt.AddDays(-3)).Should().BeNull();

        var site = InstantiateAggregate<Site>(
            ("Id", rawSiteId),
            ("TenantId", Tenant),
            ("Code", "SITE-101"),
            ("Name", "Tokyo Site"),
            ("Country", "JP"));
        var studySite = InstantiateAggregate<StudySite>(
            ("Id", studySiteId),
            ("TenantId", Tenant),
            ("StudyId", studyId),
            ("SiteId", rawSiteId));
        var enrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            participantId,
            studyId,
            studySiteId,
            SubjectCode.From("101-0001"),
            enrolledAt.AddDays(-7),
            enrolledAt.AddDays(-7)).Value!;
        enrollment.ChangeStatus(
            EnrollmentStatus.Enrolled,
            enrolledAt,
            enrolledAt,
            enrollmentNumber: 1).Should().BeNull();

        var work = new InMemoryCollectionWork(enrollment, studySite, site, version, effectivity);
        var context = new StubKernelCommandContext(Tenant, Actor.Person("crc.user"), Now, work);
        using var provider = new ServiceCollection().AddKernelCollectionHandlers().BuildServiceProvider();
        var handler = provider.GetRequiredService<ICommandHandler<OpenCollectionInstance, CollectionInstanceState>>();

        // 1. First singleton open succeeds and computes InWindow timing assessment.
        var firstSingleton = await handler.HandleAsync(
            new OpenCollectionInstance(
                Tenant,
                CollectionInstanceId.New(),
                enrollment.Id,
                "cs.singleton",
                "visit.singleton",
                Now,
                "idem-1",
                "Open Week 4 physical exam"),
            context,
            ct);
        firstSingleton.IsSuccess.Should().BeTrue();
        firstSingleton.Value!.VisitTimingAssessment.Should().NotBeNull();
        firstSingleton.Value.VisitTimingAssessment!.Timing.Should().Be(VisitTiming.InWindow);

        // 2. Second singleton open at the same non-repeating visit is rejected with 409 K.Collection.DuplicateSingletonInstance.
        var duplicateSingleton = await handler.HandleAsync(
            new OpenCollectionInstance(
                Tenant,
                CollectionInstanceId.New(),
                enrollment.Id,
                "cs.singleton",
                "visit.singleton",
                Now,
                "idem-2",
                "Attempt duplicate singleton physical exam"),
            context,
            ct);
        duplicateSingleton.IsSuccess.Should().BeFalse();
        duplicateSingleton.Error.Should().Be(CollectionErrors.DuplicateSingletonInstance);
        duplicateSingleton.Error!.Code.Should().Be("K.Collection.DuplicateSingletonInstance");
        duplicateSingleton.Error.HttpStatus.Should().Be(409);

        // 3. Repeating CollectionSpec at a non-repeating visit allows multiple instances.
        for (int i = 0; i < 2; i++)
        {
            var res = await handler.HandleAsync(
                new OpenCollectionInstance(
                    Tenant,
                    CollectionInstanceId.New(),
                    enrollment.Id,
                    "cs.repeating",
                    "visit.singleton",
                    Now,
                    $"idem-rep-spec-{i}",
                    "Open repeating vitals"),
                context,
                ct);
            res.IsSuccess.Should().BeTrue();
        }

        // 4. Non-repeating CollectionSpec at a repeating visit allows multiple instances.
        for (int i = 0; i < 2; i++)
        {
            var res = await handler.HandleAsync(
                new OpenCollectionInstance(
                    Tenant,
                    CollectionInstanceId.New(),
                    enrollment.Id,
                    "cs.singleton",
                    "visit.repeating",
                    Now,
                    $"idem-rep-visit-{i}",
                    "Open repeating visit physical exam"),
                context,
                ct);
            res.IsSuccess.Should().BeTrue();
        }

        // 5. Unscheduled forms (PlannedActivityId == null) allow multiple instances.
        for (int i = 0; i < 2; i++)
        {
            var res = await handler.HandleAsync(
                new OpenCollectionInstance(
                    Tenant,
                    CollectionInstanceId.New(),
                    enrollment.Id,
                    "cs.unscheduled",
                    null,
                    Now,
                    $"idem-unsched-{i}",
                    "Open unscheduled AE"),
                context,
                ct);
            res.IsSuccess.Should().BeTrue();
        }
    }

    /// <summary>
    /// <c>GET /api/v1/kernel/enrollments/{enrollmentRef}/schedule</c> scopes schedules by site
    /// <c>EffectiveVersions</c> (Site A on V1 while V2 is Approved), returns <c>NoEffectiveVersion</c> guidance when
    /// a site has only a <c>Draft</c> version, computes <c>NotYetOpen</c>, <c>Open</c>, and <c>Closed</c> visit windows
    /// in site local time from <c>StatusSince</c>, and masks unauthorized callers with <c>404</c>.
    /// </summary>
    [Fact]
    public async Task EnrollmentScheduleEndpoint_ResolvesSiteEffectiveVersion_GovernanceGuidance_SiteWindowStatuses_AndMasking()
    {
        var ct = TestContext.Current.CancellationToken;
        var studyId = StudyId.New();
        var draftOnlyStudyId = StudyId.New();
        var siteA = StudySiteId.New();
        var siteB = StudySiteId.New();
        var siteDraftOnly = StudySiteId.New();
        var hiddenEnrollmentId = EnrollmentId.New();

        var enrollSiteA = new EnrollmentState(
            EnrollmentId.New(),
            ParticipantId.New(),
            studyId,
            siteA,
            SubjectCode.From("A-0001"),
            EnrollmentStatus.Enrolled,
            new DateTimeOffset(2026, 3, 1, 22, 30, 0, TimeSpan.FromHours(9)),
            1,
            "sha256:enr-a");
        var enrollSiteB = new EnrollmentState(
            EnrollmentId.New(),
            ParticipantId.New(),
            studyId,
            siteB,
            SubjectCode.From("B-0001"),
            EnrollmentStatus.Enrolled,
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            2,
            "sha256:enr-b");
        var enrollDraftOnly = new EnrollmentState(
            EnrollmentId.New(),
            ParticipantId.New(),
            draftOnlyStudyId,
            siteDraftOnly,
            SubjectCode.From("C-0001"),
            EnrollmentStatus.Screening,
            new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero),
            null,
            "sha256:enr-c");
        var enrollHidden = enrollSiteA with { EnrollmentId = hiddenEnrollmentId };

        var v1Id = ProtocolVersionId.New();
        var v2Id = ProtocolVersionId.New();
        var draftVersionId = ProtocolVersionId.New();

        var concept = new ConceptRef(ConceptId.New(), 1);
        var v1Design = new ProtocolDesign
        {
            SchemaVersion = 1,
            Title = "CADENCE-101 v1",
            Epochs = [new Epoch("ep.1", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity("v.day1", "Baseline", "ep.1", [ActivityLocation.Site], ["cs.vs"], new ActivityTiming(1, 0, 1)),
                new PlannedActivity("v.day15", "Day 15", "ep.1", [ActivityLocation.Site], ["cs.vs"], new ActivityTiming(15, 2, 2)),
                new PlannedActivity("v.day29", "Day 29", "ep.1", [ActivityLocation.Site], ["cs.vs"], new ActivityTiming(29, 2, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec("cs.vs", "Vital Signs", [new CollectionItem("sbp", concept)], Repeating: false),
                new CollectionSpec("cs.ae", "Adverse Events", [new CollectionItem("ae", concept)], Repeating: true),
            ],
        };
        var v2Design = v1Design with
        {
            Title = "CADENCE-101 v2 Amendment",
            PlannedActivities =
            [
                .. v1Design.PlannedActivities,
                new PlannedActivity("v.day57", "Day 57 Amendment Visit", "ep.1", [ActivityLocation.Site], ["cs.vs"], new ActivityTiming(57, 3, 3)),
            ],
        };

        var v1Summary = new ProtocolVersionSummary(v1Id, studyId, 1, null, ProtocolVersionStatus.Approved, "sha256:v1", null, AttestationId.New(), null);
        var v2Summary = new ProtocolVersionSummary(v2Id, studyId, 2, v1Id, ProtocolVersionStatus.Approved, "sha256:v2", "Add Day 57", AttestationId.New(), null);
        var draftSummary = new ProtocolVersionSummary(draftVersionId, draftOnlyStudyId, 1, null, ProtocolVersionStatus.Draft, null, null, null, null);

        var enrollments = new StubScheduleEnrollmentDirectory([enrollSiteA, enrollSiteB, enrollDraftOnly, enrollHidden]);
        var versions = new StubScheduleProtocolVersionReader(
            [v2Summary, v1Summary, draftSummary],
            new Dictionary<ProtocolVersionId, ProtocolDesign>
            {
                [v1Id] = v1Design,
                [v2Id] = v2Design,
            });
        var governance = new StubStudyGovernanceReader(new Dictionary<StudySiteId, StudySiteGovernanceSummary>
        {
            [siteA] = new(
                siteA,
                studyId,
                SiteId.New(),
                "SITE-A",
                "Tokyo Site",
                "JP",
                "Active",
                [new EffectiveVersion(v1Id, 1, EffectivityId.New(), EffectivityLevel.StudySite)]),
            [siteB] = new(
                siteB,
                studyId,
                SiteId.New(),
                "SITE-B",
                "London Site",
                "GB",
                "Active",
                [
                    new EffectiveVersion(v2Id, 2, EffectivityId.New(), EffectivityLevel.StudySite),
                    new EffectiveVersion(v1Id, 1, EffectivityId.New(), EffectivityLevel.StudySite),
                ]),
            [siteDraftOnly] = new(
                siteDraftOnly,
                draftOnlyStudyId,
                SiteId.New(),
                "SITE-C",
                "Draft Site",
                "US",
                "Selected",
                []),
        });
        var visibility = new StubScheduleVisibility([NodeRef.For(hiddenEnrollmentId)]);

        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestBiometricLivenessKey();
            builder.ConfigureServices(services =>
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }

                services.RemoveAll<ITenantDirectory>();
                services.RemoveAll<IEnrollmentDirectory>();
                services.RemoveAll<IProtocolVersionReader>();
                services.RemoveAll<IStudyGovernanceReader>();
                services.RemoveAll<INodeVisibility>();
                services.RemoveAll<IClock>();

                services.AddSingleton<ITenantDirectory>(new StubScheduleTenantDirectory(Tenant));
                services.AddSingleton<IEnrollmentDirectory>(enrollments);
                services.AddSingleton<IProtocolVersionReader>(versions);
                services.AddSingleton<IStudyGovernanceReader>(governance);
                services.AddSingleton<INodeVisibility>(visibility);
                services.AddSingleton<IClock>(new FixedScheduleClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)));
            });
        });
        _factories.Add(factory);

        using var client = factory.CreateSignedClient(TestSecurityContext.DefaultIdentity);

        // 1. Site A is governed by V1 even though V2 is Approved on the study; ExpectedSchedule.Compute yields Closed, Open, NotYetOpen as of 2026-03-15.
        var siteAResp = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/kernel/enrollments/{NodeRef.For(enrollSiteA.EnrollmentId)}/schedule?as_of=2026-03-15&time_zone=Asia/Tokyo",
            ct);
        siteAResp.GetProperty("governance_status").GetString().Should().Be("Effective");
        siteAResp.GetProperty("governing_version").GetProperty("protocol_version_ref").GetString().Should().Be(NodeRef.For(v1Id).ToString());
        siteAResp.GetProperty("governing_version").GetProperty("number").GetInt32().Should().Be(1);
        siteAResp.GetProperty("reference_date").GetString().Should().Be("2026-03-01");

        var siteAVisits = siteAResp.GetProperty("expected_visits").EnumerateArray().ToList();
        siteAVisits.Select(v => v.GetProperty("activity_id").GetString()).Should().Equal("v.day1", "v.day15", "v.day29");
        siteAVisits.Select(v => v.GetProperty("target_date").GetString()).Should().Equal("2026-03-01", "2026-03-15", "2026-03-29");
        siteAVisits.Select(v => v.GetProperty("window_status").GetString()).Should().Equal("Closed", "Open", "NotYetOpen");

        var soaRows = siteAResp.GetProperty("schedule").GetProperty("rows").EnumerateArray().ToList();
        soaRows.Single(r => r.GetProperty("collection_spec_id").GetString() == "cs.vs").GetProperty("repeating").GetBoolean().Should().BeFalse();
        soaRows.Single(r => r.GetProperty("collection_spec_id").GetString() == "cs.ae").GetProperty("repeating").GetBoolean().Should().BeTrue();

        // 2. Site B is governed by V2 and includes the Day 57 amendment visit.
        var siteBResp = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/kernel/enrollments/{enrollSiteB.EnrollmentId.Value:D}/schedule?as_of=2026-03-15",
            ct);
        siteBResp.GetProperty("governing_version").GetProperty("number").GetInt32().Should().Be(2);
        siteBResp.GetProperty("expected_visits").EnumerateArray().Select(v => v.GetProperty("activity_id").GetString())
            .Should().Contain("v.day57");

        // 3. Site C with only a Draft version and zero EffectiveVersions returns 200 NoEffectiveVersion with guidance (never 422, never Draft fallback).
        using var draftRes = await client.GetAsync(
            new Uri($"/api/v1/kernel/enrollments/{NodeRef.For(enrollDraftOnly.EnrollmentId)}/schedule", UriKind.Relative),
            ct);
        draftRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var draftJson = JsonDocument.Parse(await draftRes.Content.ReadAsStringAsync(ct)).RootElement;
        draftJson.GetProperty("governance_status").GetString().Should().Be("NoEffectiveVersion");
        draftJson.GetProperty("latest_study_version_status").GetString().Should().Be("Draft");
        draftJson.GetProperty("schedule").ValueKind.Should().Be(JsonValueKind.Null);
        draftJson.GetProperty("expected_visits").GetArrayLength().Should().Be(0);
        draftJson.GetProperty("guidance").GetString().Should().ContainEquivalentOf("draft");

        // 4. Hidden/blinded enrollment returns non-confirming 404.
        using var hiddenRes = await client.GetAsync(
            new Uri($"/api/v1/kernel/enrollments/{NodeRef.For(hiddenEnrollmentId)}/schedule", UriKind.Relative),
            ct);
        hiddenRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static ProtocolVersion CreateApprovedProtocolVersion(
        StudyId studyId,
        int number,
        ProtocolDesign design)
    {
        var json = ProtocolDesignDocument.Write(design);
        var pv = InstantiateAggregate<ProtocolVersion>(
            ("Id", ProtocolVersionId.New()),
            ("TenantId", Tenant),
            ("StudyId", studyId),
            ("Number", number),
            ("Status", ProtocolVersionStatus.Approved));

        for (var t = typeof(ProtocolVersion); t is not null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(ProtocolDesign))
                {
                    field.SetValue(pv, design);
                }
                else if (field.FieldType == typeof(string)
                    && (field.Name.Contains("Content", StringComparison.OrdinalIgnoreCase)
                        || field.Name.Contains("Design", StringComparison.OrdinalIgnoreCase)
                        || field.Name.Contains("Json", StringComparison.OrdinalIgnoreCase)
                        || field.Name.Contains("Document", StringComparison.OrdinalIgnoreCase))
                    && !field.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Contains("Intent", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase))
                {
                    field.SetValue(pv, json);
                }
            }
        }

        return pv;
    }

    private static T InstantiateAggregate<T>(params (string Name, object? Value)[] props)
    {
        var instance = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
        foreach (var (name, value) in props)
        {
            for (var type = typeof(T); type is not null; type = type.BaseType)
            {
                var field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(instance, value);
                    break;
                }
            }
        }

        return instance;
    }

    private sealed class FixedScheduleClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class StubScheduleTenantDirectory(TenantId tenant) : ITenantDirectory
    {
        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<TenantId?>(tenant);
    }

    private sealed class StubScheduleVisibility(IReadOnlyCollection<NodeRef> hidden) : INodeVisibility
    {
        private readonly HashSet<NodeRef> _hidden = [.. hidden.Select(h => h.Unversioned())];

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(
                nodes.Where(n => _hidden.Contains(n.Unversioned())).ToDictionary(n => n, _ => MaskReason.NotPermitted));
    }

    private sealed class StubScheduleEnrollmentDirectory(IReadOnlyList<EnrollmentState> enrollments) : IEnrollmentDirectory
    {
        public Task<EnrollmentState?> FindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult(enrollments.FirstOrDefault(e => e.EnrollmentId == enrollment));

        public Task<EnrollmentCodeLookupResult?> LookupByCodeAsync(
            TenantId tenant, string studyCode, SubjectCode subjectCode, string? siteCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EnrollmentState?> ResolveParticipantAsync(
            TenantId tenant, StudyId study, SubjectCode code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EnrollmentStatus?> StatusAsOfAsync(
            TenantId tenant, EnrollmentId enrollment, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<(StudyId StudyId, StudySiteId StudySiteId)?> FindStudySiteAsync(
            TenantId tenant, string studyCode, string siteCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> NextEnrollmentNumberAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubScheduleProtocolVersionReader(
        IReadOnlyList<ProtocolVersionSummary> summaries,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> designs) : IProtocolVersionReader
    {
        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>([.. summaries.Where(s => s.StudyId == study)]);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(summaries.FirstOrDefault(s => s.Id == version));

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(designs.TryGetValue(version, out var d) ? ScheduleOfActivities.Compile(d) : null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(designs.GetValueOrDefault(version));
    }

    private sealed class StubStudyGovernanceReader(IReadOnlyDictionary<StudySiteId, StudySiteGovernanceSummary> sites) : IStudyGovernanceReader
    {
        public Task<IReadOnlyList<StudyArtifactSummary>> ListStudyArtifactsAsync(
            TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyArtifactSummary>>([]);

        public Task<IReadOnlyList<StudyEffectivitySummary>> ListStudyEffectivitiesAsync(
            TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyEffectivitySummary>>([]);

        public Task<StudyEffectivitySummary?> FindEffectivityAsync(
            TenantId tenant, EffectivityId effectivityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudyEffectivitySummary?>(null);

        public Task<IReadOnlyList<StudySiteGovernanceSummary>> ListStudySitesAsync(
            TenantId tenant, StudyId studyId, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteGovernanceSummary>>([.. sites.Values.Where(s => s.StudyId == studyId)]);

        public Task<StudySiteGovernanceSummary?> FindStudySiteAsync(
            TenantId tenant, StudySiteId studySiteId, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(sites.GetValueOrDefault(studySiteId));

        public Task<SiteLookupEntry?> FindSiteByCodeAsync(
            TenantId tenant, string siteCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteLookupEntry?>(null);
    }

    private sealed class StubKernelCommandContext(
        TenantId tenantId,
        Actor actor,
        DateTimeOffset recordedAt,
        IKernelUnitOfWork work) : ICommandContext
    {
        public TenantId TenantId => tenantId;
        public Actor Actor => actor;
        public DateTimeOffset RecordedAt => recordedAt;
        public IKernelUnitOfWork Work => work;
    }

    private sealed class InMemoryCollectionWork(
        Enrollment enrollment,
        StudySite studySite,
        Site site,
        ProtocolVersion version,
        Effectivity effectivity) : IKernelUnitOfWork
    {
        private readonly List<CollectionInstance> _instances = [];
        public List<object> Raised { get; } = [];

        public Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            ReadAsync<TAggregate>(id, cancellationToken);

        public Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            if (typeof(TAggregate) == typeof(CollectionInstance) && id is CollectionInstanceId cid)
            {
                return Task.FromResult(_instances.Any(i => i.Id == cid));
            }

            return Task.FromResult(false);
        }

        public Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            if (typeof(TAggregate) == typeof(Enrollment) && id is EnrollmentId eid && eid == enrollment.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)enrollment);
            }

            if (typeof(TAggregate) == typeof(StudySite) && id is StudySiteId ssid && ssid == studySite.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)studySite);
            }

            if (typeof(TAggregate) == typeof(Site) && id is SiteId sid && sid == site.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)site);
            }

            if (typeof(TAggregate) == typeof(ProtocolVersion) && id is ProtocolVersionId vid && vid == version.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)version);
            }

            return Task.FromResult<TAggregate?>(null);
        }

        public Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
            Expression<Func<TAggregate, bool>> filter,
            CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            var compiled = filter.Compile();
            if (typeof(TAggregate) == typeof(CollectionInstance))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. _instances.Cast<TAggregate>().Where(compiled)]);
            }

            if (typeof(TAggregate) == typeof(Effectivity))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. new[] { effectivity }.Cast<TAggregate>().Where(compiled)]);
            }

            if (typeof(TAggregate) == typeof(ProtocolVersion))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. new[] { version }.Cast<TAggregate>().Where(compiled)]);
            }

            return Task.FromResult<IReadOnlyList<TAggregate>>([]);
        }

        public Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.CompletedTask;

        public Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Add(AggregateRootBase aggregate)
        {
            if (aggregate is CollectionInstance ci)
            {
                _instances.Add(ci);
            }
        }

        public void Raise(object domainEvent, DateTimeOffset? occurredValid = null) => Raised.Add(domainEvent);

        public void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null, NodeRef? target = null)
        {
        }

        public void IncludePart(NodeRef part, bool newVersion)
        {
        }

        public void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind)
        {
        }
    }
}
