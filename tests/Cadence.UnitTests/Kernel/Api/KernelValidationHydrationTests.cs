using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Kernel.Api;

/// <summary>
/// HTTP endpoint tests for 6-dimension clinical <see cref="NodeRef"/> hydration and <see cref="INodeVisibility"/> masking
/// across <c>KernelValidationEndpoints</c> read and preview routes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelValidationHydrationTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009001")); // deid-ignore
    private static readonly StudyId Study = StudyId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009002")); // deid-ignore
    private static readonly StudySiteId Site = StudySiteId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009003")); // deid-ignore
    private static readonly EnrollmentId Enrollment = EnrollmentId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009004")); // deid-ignore
    private static readonly ParticipantId Participant = ParticipantId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009005")); // deid-ignore
    private static readonly ProtocolVersionId ProtocolVersion = ProtocolVersionId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009006")); // deid-ignore
    private static readonly CollectionInstanceId FormInstance = CollectionInstanceId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009007")); // deid-ignore
    private static readonly ObservationId HrObservation = ObservationId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009008")); // deid-ignore
    private static readonly LibraryItemId RulePackItem = LibraryItemId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009009")); // deid-ignore
    private static readonly Guid RaisedQueryId = Guid.Parse("01950000-0000-7000-8000-000000009010"); // deid-ignore
    private static readonly ConceptRef HrConcept = new(ConceptId.FromGuid(Guid.Parse("01950000-0000-7000-8000-000000009011")), 1); // deid-ignore
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly List<WebApplicationFactory<Cadence.Kernel.Host.Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Fact(DisplayName = "ValidationEndpoints_HydrateSixClinicalDimensionsAndExplainEvidence_OnReadAndPreview")]
    public async Task ValidationEndpointsHydrateSixClinicalDimensionsAndExplainEvidenceOnReadAndPreview()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, findingId) = CreateHydrationClient(new Dictionary<NodeRef, MaskReason>());
        using var _ = client;

        var studyRef = NodeRef.For(Study).ToString();
        var findingRef = NodeRef.For(findingId).ToString();
        var rulePackRef = NodeRef.For(RulePackItem).ToString();
        var expectedQueryRef = new NodeRef(NodeKinds.FormQuery, RaisedQueryId).ToString();

        using var listResponse = await client.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/validation/findings", UriKind.Relative), ct);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(ct));
        var hydrated = listDoc.RootElement.GetProperty("findings").EnumerateArray().Should().ContainSingle().Subject;

        hydrated.GetProperty("target_kind").GetString().Should().Be("DatasetRecord");
        hydrated.GetProperty("target_node_ref").GetString().Should().Be(NodeRef.For(FormInstance).ToString());
        hydrated.GetProperty("target_label").GetString().Should().Be("Vital Signs (Week 4 Visit) · Resting Heart Rate");
        hydrated.GetProperty("rule_pack_ref").GetString().Should().Be(rulePackRef);
        hydrated.GetProperty("rule_pack_label").GetString().Should().Be("Vital Signs Safety Edit Checks");
        hydrated.GetProperty("site_ref").GetString().Should().Be(NodeRef.For(Site).ToString());
        hydrated.GetProperty("site_label").GetString().Should().Be("SITE-102 · Boston Medical Center");
        hydrated.GetProperty("participant_ref").GetString().Should().Be(NodeRef.For(Enrollment).ToString());
        hydrated.GetProperty("participant_id").GetString().Should().Be(NodeRef.For(Participant).ToString());
        hydrated.GetProperty("participant_label").GetString().Should().Be("SUBJ-1002");
        hydrated.GetProperty("planned_activity_id").GetString().Should().Be("visit.week4");
        hydrated.GetProperty("visit_label").GetString().Should().Be("Week 4 Visit");
        hydrated.GetProperty("form_ref").GetString().Should().Be(NodeRef.For(FormInstance).ToString());
        hydrated.GetProperty("form_id").GetString().Should().Be(FormInstance.Value.ToString("D"));
        hydrated.GetProperty("form_label").GetString().Should().Be("Vital Signs");
        hydrated.GetProperty("collection_spec_id").GetString().Should().Be("VS");
        hydrated.GetProperty("item_id").GetString().Should().Be("HR");
        hydrated.GetProperty("item_label").GetString().Should().Be("Resting Heart Rate");
        hydrated.GetProperty("observation_ref").GetString().Should().Be(NodeRef.For(HrObservation).ToString());
        hydrated.GetProperty("observation_label").GetString().Should().Be("Resting Heart Rate: 112 /min");
        hydrated.GetProperty("query_ref").GetString().Should().Be(expectedQueryRef);
        hydrated.GetProperty("query_id").GetGuid().Should().Be(RaisedQueryId);
        hydrated.GetProperty("query_label").GetString().Should().Be("Query: Resting heart rate exceeds 100 /min");

        using var detailResponse = await client.GetAsync(new Uri($"/api/v1/kernel/validation/findings/{findingRef}", UriKind.Relative), ct);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detailDoc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync(ct));
        var explanation = detailDoc.RootElement.GetProperty("explanation");
        explanation.GetProperty("claim").GetString().Should().NotContain(FormInstance.Value.ToString("D"));
        var targetStepEvidence = explanation.GetProperty("steps")[1]
            .GetProperty("evidence")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
        targetStepEvidence.Should().Contain([
            NodeRef.For(FormInstance).ToString(),
            NodeRef.For(HrObservation).ToString(),
            expectedQueryRef,
        ]);
        explanation.GetProperty("next_actions")[0].GetProperty("target").GetString().Should().Be(NodeRef.For(FormInstance).ToString());

        using var previewContent = new StringContent("{}", Encoding.UTF8, "application/json");
        using var previewResponse = await client.PostAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview", UriKind.Relative), previewContent, ct);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var previewDoc = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync(ct));
        var violation = previewDoc.RootElement.GetProperty("violations").EnumerateArray().Should().ContainSingle().Subject;
        violation.GetProperty("site_ref").GetString().Should().Be(NodeRef.For(Site).ToString());
        violation.GetProperty("participant_ref").GetString().Should().Be(NodeRef.For(Enrollment).ToString());
        violation.GetProperty("visit_label").GetString().Should().Be("Week 4 Visit");
        violation.GetProperty("form_ref").GetString().Should().Be(NodeRef.For(FormInstance).ToString());
        violation.GetProperty("observation_ref").GetString().Should().Be(NodeRef.For(HrObservation).ToString());
        violation.GetProperty("query_ref").GetString().Should().Be(expectedQueryRef);
        violation.GetProperty("query_id").GetGuid().Should().Be(RaisedQueryId);
    }

    [Fact(DisplayName = "ValidationEndpoints_RenderMaskedPlaceholders_WhenRelatedClinicalNodesAreNotVisible")]
    public async Task ValidationEndpointsRenderMaskedPlaceholdersWhenRelatedClinicalNodesAreNotVisible()
    {
        var ct = TestContext.Current.CancellationToken;
        var queryNode = new NodeRef(NodeKinds.FormQuery, RaisedQueryId);
        var studyRef = NodeRef.For(Study).ToString();
        var rulePackRef = NodeRef.For(RulePackItem).ToString();

        var (client, _) = CreateHydrationClient(new Dictionary<NodeRef, MaskReason>
        {
            [NodeRef.For(Site)] = MaskReason.NotPermitted,
            [NodeRef.For(RulePackItem)] = MaskReason.NotPermitted,
            [queryNode] = MaskReason.Blinded,
        });
        using var _ = client;

        using var listResponse = await client.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/validation/findings", UriKind.Relative), ct);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(ct));
        var hydrated = listDoc.RootElement.GetProperty("findings").EnumerateArray().Should().ContainSingle().Subject;

        hydrated.GetProperty("rule_pack_label").GetString().Should().Be("masked");
        hydrated.GetProperty("site_ref").ValueKind.Should().Be(JsonValueKind.Null);
        hydrated.GetProperty("site_label").GetString().Should().Be("masked");
        hydrated.GetProperty("query_ref").ValueKind.Should().Be(JsonValueKind.Null);
        hydrated.GetProperty("query_id").ValueKind.Should().Be(JsonValueKind.Null);
        hydrated.GetProperty("query_label").GetString().Should().Be("masked");

        // When the referenced observation itself is masked (K-AUTH-4), preview masks observation_ref/label
        // while findings list/detail suppress the finding altogether.
        var (obsMaskedClient, findingId) = CreateHydrationClient(new Dictionary<NodeRef, MaskReason>
        {
            [NodeRef.For(Site)] = MaskReason.NotPermitted,
            [NodeRef.For(HrObservation)] = MaskReason.Blinded,
            [queryNode] = MaskReason.Blinded,
        });
        using var __ = obsMaskedClient;

        using var previewContent = new StringContent("{}", Encoding.UTF8, "application/json");
        using var previewResponse = await obsMaskedClient.PostAsync(
            new Uri($"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview", UriKind.Relative),
            previewContent,
            ct);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var previewDoc = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync(ct));
        var previewViolation = previewDoc.RootElement.GetProperty("violations").EnumerateArray().Should().ContainSingle().Subject;
        previewViolation.GetProperty("site_ref").ValueKind.Should().Be(JsonValueKind.Null);
        previewViolation.GetProperty("site_label").GetString().Should().Be("masked");
        previewViolation.GetProperty("observation_ref").ValueKind.Should().Be(JsonValueKind.Null);
        previewViolation.GetProperty("observation_label").GetString().Should().Be("masked");
        previewViolation.GetProperty("query_ref").ValueKind.Should().Be(JsonValueKind.Null);
        previewViolation.GetProperty("query_label").GetString().Should().Be("masked");

        using var suppressedList = await obsMaskedClient.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/validation/findings", UriKind.Relative), ct);
        suppressedList.StatusCode.Should().Be(HttpStatusCode.OK);
        using var suppressedDoc = JsonDocument.Parse(await suppressedList.Content.ReadAsStringAsync(ct));
        suppressedDoc.RootElement.GetProperty("findings").GetArrayLength().Should().Be(0);

        using var suppressedDetail = await obsMaskedClient.GetAsync(new Uri($"/api/v1/kernel/validation/findings/{NodeRef.For(findingId)}", UriKind.Relative), ct);
        suppressedDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private (HttpClient Client, ValidationFindingId FindingId) CreateHydrationClient(IReadOnlyDictionary<NodeRef, MaskReason> maskedNodes)
    {
        var rulePack = new RulePack(
            RuleAuthority.Study,
            "Vital Signs Safety Edit Checks",
            [
                new Rule(
                    "VS_HR_HIGH",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Protocol Edit Check Spec", "1.0", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(
                        ComparisonOperator.Greater,
                        new VariableExpression("HR"),
                        new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Resting heart rate exceeds 100 /min.",
                    "50 to 100",
                    EffectivePeriod.Always),
            ]);

        var libraryItem = new LibraryItemView(
            RulePackItem,
            LibraryItemKind.RulePack,
            "study-vs-safety",
            1,
            "Vital Signs Safety Edit Checks",
            RulePackJson.ToJson(rulePack),
            "sha256:" + new string('b', 64),
            GovernanceStatus.Approved,
            [],
            AttestationId.New(),
            null);

        var profile = new StudyStandardsProfileView(
            StudyStandardsProfileId.New(),
            Study,
            1,
            [],
            [],
            [RulePackItem]);

        var items = new CollectionItem[]
        {
            new("HR", HrConcept, Required: true, ValueKind: ObservationValueKind.Quantity, Unit: "/min", Prompt: "Resting Heart Rate"),
        };

        var formRecord = new CollectionInstanceRecord(
            FormInstance,
            Enrollment,
            Participant,
            Study,
            Site,
            ProtocolVersion,
            "VS",
            "visit.week4",
            T0,
            "crc-102",
            T0,
            Version: 2,
            items,
            [new CollectedValueView("HR", HrObservation, HrConcept, new QuantityValue(112m, "/min"), T0, 1, "crc-102", T0)],
            [],
            [],
            [],
            "hash-vs",
            CollectionTitle: "Vital Signs");

        var identity = new FindingIdentity(
            Study,
            ValidationStage.Collection,
            "study-vs-safety",
            "VS_HR_HIGH",
            new FindingTarget(FindingTargetKind.DatasetRecord, "VS", FormInstance.Value.ToString("D"), "HR"));
        var findingId = ValidationFindingId.For(Tenant, identity);
        var runId = ValidationRunId.New();

        var finding = new ValidationFindingView(
            findingId,
            Study,
            ValidationStage.Collection,
            "study-vs-safety",
            RulePackItem,
            "VS_HR_HIGH",
            1,
            ValidationSeverity.Error,
            identity.Target,
            "Resting heart rate exceeds 100 /min.",
            "50 to 100",
            [new ObservedVariable("HR", "112")],
            null,
            null,
            null,
            FindingStatus.Open,
            runId,
            runId,
            null,
            null,
            null);

        var queryNode = new NodeRef(NodeKinds.FormQuery, RaisedQueryId);
        var summaries = new Dictionary<NodeRef, NodeSummary>
        {
            [NodeRef.For(Study)] = new(NodeRef.For(Study), "Cadence Demo Study", "DEMO-01", "Active"),
            [NodeRef.For(Site)] = new(NodeRef.For(Site), "Boston Medical Center", "SITE-102", "Active"),
            [NodeRef.For(Enrollment)] = new(NodeRef.For(Enrollment), "SUBJ-1002", "SUBJ-1002", "Enrolled"),
            [NodeRef.For(FormInstance)] = new(NodeRef.For(FormInstance), "Vital Signs", "VS", "InProgress"),
            [NodeRef.For(HrObservation)] = new(NodeRef.For(HrObservation), "Resting Heart Rate: 112 /min"),
            [NodeRef.For(RulePackItem)] = new(NodeRef.For(RulePackItem), "Vital Signs Safety Edit Checks", "study-vs-safety", "Approved"),
            [queryNode] = new(queryNode, "Query: Resting heart rate exceeds 100 /min", null, "Open"),
        };

        var design = new ProtocolDesign
        {
            Title = "Cadence Demo Study",
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            CollectionSpecs = [new CollectionSpec("VS", "Vital Signs", items)],
            PlannedActivities = [new PlannedActivity("visit.week4", "Week 4 Visit", "ep.treatment", [ActivityLocation.Site], ["VS"], new ActivityTiming(28))],
        };

        var findingsStore = new InMemoryFindingsStore(finding);
        var collections = new InMemoryCollectionRecords([formRecord]);
        var registry = new InMemoryStandardsRegistry(profile, libraryItem);
        var validationService = new CollectionValidationService(
            new RulePackLoader(registry),
            registry,
            new DotNetRuleEvaluator(),
            collections,
            findingsStore,
            new NoOpCommandSender());
        var navigator = new InMemoryNavigator(summaries, [new StudyEnrollmentNode(Enrollment, "SUBJ-1002", Site, "SITE-102")]);
        var lineage = new InMemoryLineage(new Dictionary<NodeRef, NodeRef>
        {
            [NodeRef.For(findingId)] = queryNode,
            [NodeRef.For(HrObservation)] = queryNode,
        });
        var protocols = new InMemoryProtocolReader(ProtocolVersion, Study, design);
        var visibility = new InMemoryVisibility(maskedNodes);
        var tenantDirectory = new InMemoryTenantDirectory(TestSecurityContext.TenantCode, Tenant);

        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                var backgroundJobs = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.Namespace?.StartsWith("Cadence.Kernel.Persistence", StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var job in backgroundJobs)
                {
                    services.Remove(job);
                }

                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FakeClock(T0));
                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(tenantDirectory);
                services.RemoveAll<IValidationFindings>();
                services.AddSingleton<IValidationFindings>(findingsStore);
                services.RemoveAll<ICollectionInstanceRecords>();
                services.AddSingleton<ICollectionInstanceRecords>(collections);
                services.RemoveAll<IStandardsRegistry>();
                services.AddSingleton<IStandardsRegistry>(registry);
                services.RemoveAll<ICollectionValidationService>();
                services.AddSingleton<ICollectionValidationService>(validationService);
                services.RemoveAll<IKernelNavigator>();
                services.AddSingleton<IKernelNavigator>(navigator);
                services.RemoveAll<IKernelLineage>();
                services.AddSingleton<IKernelLineage>(lineage);
                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(protocols);
                services.RemoveAll<IConceptRegistry>();
                services.AddSingleton<IConceptRegistry>(new InMemoryConceptRegistry());
                services.RemoveAll<IAuthorizationProbe>();
                services.AddSingleton<IAuthorizationProbe>(new InMemoryAuthorizationProbe());
                services.RemoveAll<INodeVisibility>();
                services.AddSingleton<INodeVisibility>(visibility);
            });
        });
        _factories.Add(factory);

        return (factory.CreateSignedClient(TestSecurityContext.DefaultIdentity), findingId);
    }

    private sealed class InMemoryTenantDirectory(string expectedCode, TenantId tenantId) : ITenantDirectory
    {
        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<TenantId?>(string.Equals(code, expectedCode, StringComparison.Ordinal) ? tenantId : null);
    }

    private sealed class InMemoryVisibility(IReadOnlyDictionary<NodeRef, MaskReason> masks) : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant,
            Actor reader,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken)
        {
            var matched = new Dictionary<NodeRef, MaskReason>();
            foreach (var node in nodes)
            {
                if (masks.TryGetValue(node.Unversioned(), out var reason))
                {
                    matched[node] = reason;
                }
            }

            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(matched);
        }
    }

    private sealed class InMemoryFindingsStore(ValidationFindingView storedFinding) : IValidationFindings
    {
        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationFindingView?>(finding == storedFinding.Id ? storedFinding : null);

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new FindingPage(query.Study == storedFinding.Study ? [storedFinding] : [], query.Study == storedFinding.Study ? 1 : 0));

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationRunSummary?>(null);
    }

    private sealed class InMemoryCollectionRecords(IReadOnlyList<CollectionInstanceRecord> records) : ICollectionInstanceRecords
    {
        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(records.FirstOrDefault(r => r.CollectionInstanceId == instance));

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. records.Where(r => r.EnrollmentId == enrollment)]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. records.Where(r => r.StudyId == study)]);
    }

    private sealed class InMemoryStandardsRegistry(StudyStandardsProfileView profile, LibraryItemView item) : IStandardsRegistry
    {
        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StandardVersionSummary>>([]);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardPackage?>(null);

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult<LibraryItemView?>(id == item.Id ? item : null);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(kind == item.Kind && key == item.Key ? [item] : []);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemsAsync(TenantId tenant, LibraryItemKind? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(kind is null || kind == item.Kind ? [item] : []);

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<StudyStandardsProfileView?>(study == profile.StudyId ? profile : null);
    }

    private sealed class InMemoryNavigator(
        IReadOnlyDictionary<NodeRef, NodeSummary> summaries,
        IReadOnlyList<StudyEnrollmentNode> enrollments) : IKernelNavigator
    {
        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult(summaries.TryGetValue(node.Unversioned(), out var s) ? s : null);

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ZoomStep>?>([new ZoomStep(node.Kind, node.ToString(), node)]);

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScopeChild>?>([]);

        public Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyEnrollmentNode>?>(study == Study ? enrollments : null);
    }

    private sealed class InMemoryLineage(IReadOnlyDictionary<NodeRef, NodeRef> derivedQueries) : IKernelLineage
    {
        public Task<LineageGraph> TraceAsync(TenantId tenant, Actor reader, LineageQuery query, CancellationToken cancellationToken = default)
        {
            var start = query.Node.Unversioned();
            var nodes = new List<LineageNode>
            {
                new(start.ToString(), start.Kind, start, null, 0),
            };
            var edges = new List<LineageEdge>();
            if (derivedQueries.TryGetValue(start, out var qRef))
            {
                nodes.Add(new LineageNode(qRef.ToString(), qRef.Kind, qRef, null, 1));
                edges.Add(new LineageEdge(start.ToString(), qRef.ToString(), LineageEdgeKind.DerivedFrom, null, T0, T0));
            }

            return Task.FromResult(new LineageGraph(start.ToString(), query.Direction, query.Depth, T0, T0, nodes, edges, Truncated: false));
        }

        public Task<LineageDossier> DossierAsync(
            TenantId tenant,
            Actor reader,
            NodeRef node,
            int depth = LineageQuery.DefaultDepth,
            DateTimeOffset? validAt = null,
            DateTimeOffset? knownAt = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DependentsReport> DependentsAsync(TenantId tenant, Actor reader, DependentsQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryProtocolReader(ProtocolVersionId versionId, StudyId studyId, ProtocolDesign design) : IProtocolVersionReader
    {
        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>(study == studyId
                ? [new ProtocolVersionSummary(versionId, studyId, 1, null, ProtocolVersionStatus.Approved, "sha256:123", null, AttestationId.New(), null)]
                : []);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<ProtocolVersionSummary?>(version == versionId
                ? new ProtocolVersionSummary(versionId, studyId, 1, null, ProtocolVersionStatus.Approved, "sha256:123", null, AttestationId.New(), null)
                : null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<ProtocolDesign?>(version == versionId ? design : null);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(null);
    }

    private sealed class NoOpCommandSender : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CommandResult<TResult>.Success(default!));
    }

    private sealed class InMemoryAuthorizationProbe : IAuthorizationProbe
    {
        public ValueTask<AuthorizationDecision> CanIAsync(
            TenantId tenant,
            Capability capability,
            NodeRef resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(
            TenantId tenant,
            NodeRef resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CommandAvailability>>([]);
    }

    private sealed class InMemoryConceptRegistry : IConceptRegistry
    {
        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConceptVersionView?>(null);

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConceptVersionView?>(null);

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConceptVersionView?>(null);

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<CodeSystemVersionView?>(null);

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);
    }
}
