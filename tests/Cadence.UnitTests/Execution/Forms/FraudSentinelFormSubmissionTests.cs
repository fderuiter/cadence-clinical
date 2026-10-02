using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Contracts.Events;
using Cadence.Sentinels;
using Cadence.Sentinels.Checks;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using MassTransit;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

public class FraudSentinelFormSubmissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryFormSubmissionRepository _formRepository = new();
    private readonly InMemoryObservationRepository _obsRepository = new();
    private readonly InMemoryClinicalQueryRepository _queryRepository = new();
    private readonly UcumNormalizationService _ucumService = new();
    private readonly FakeCurrentAuditContextAccessor _auditAccessor = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();

    private FormSubmissionApplicationService CreateService(SentinelEngine sentinelEngine)
    {
        var autoQueryDispatchService = new AutoQueryDispatchService(_queryRepository, _clock, _auditAccessor);
        var baselineCache = new FakeFraudBaselineCache();

        return new FormSubmissionApplicationService(
            _formRepository,
            _obsRepository,
            _ucumService,
            _auditAccessor,
            subjectRepository: null,
            editCheckEngine: new AstEditCheckEngine(),
            autoQueryDispatchService: autoQueryDispatchService,
            sentinelEngine: sentinelEngine,
            baselineCache: baselineCache,
            publishEndpoint: _publishEndpoint,
            clock: _clock);
    }

    private sealed class FakeFraudBaselineCache : IFraudBaselineCache
    {
        public Task<StatisticalBaseline> GetOrFetchBaselineAsync(Cadence.Apps.Execution.Domain.Subjects.StudyCode studyId, CancellationToken cancellationToken)
        {
            var fieldStats = new Dictionary<string, FieldBaselineStats>(StringComparer.OrdinalIgnoreCase)
            {
                ["VSSBP"] = new FieldBaselineStats(Mean: 120.0, Variance: 25.0, SampleSize: 100)
            };
            return Task.FromResult(new StatisticalBaseline(new Dictionary<int, long>(), 0, fieldStats));
        }

        public void InvalidateBaseline(Cadence.Apps.Execution.Domain.Subjects.StudyCode studyId) { }
    }

    [Fact]
    public async Task CreateAsync_WithBiasedDigitPayload_TriggersFraudSentinelQueryAndPublishesOutboxEvents()
    {
        var engine = new SentinelEngine();
        engine.Register(new BenfordDigitPreferenceSentinelCheck());
        engine.Register(new SiteVarianceSentinelCheck());

        var service = CreateService(engine);

        // Build a JSON payload with heavy digit bias (90% 9s and 8s)
        var obsList = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            obsList.Add($$"""{"test_code":"VSSBP","value":{{99.0 + i}},"observation_date":"2026-10-02T10:00:00Z"}""");
            obsList.Add($$"""{"test_code":"VSSBP","value":{{88.0 + i}},"observation_date":"2026-10-02T10:00:00Z"}""");
        }

        string payloadJson = $$"""{"observation_date":"2026-10-02T10:00:00Z","observations":[{{string.Join(",", obsList)}}]}""";

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "1.0.0",
            Payload: payloadJson);

        var result = await service.CreateAsync(request, Ct);

        result.IsSuccess.Should().BeTrue();

        // 1. Verify automated discrepancy query was raised
        var queries = await _queryRepository.ListBySubjectAsync(
            new Cadence.Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            new Cadence.Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-001"),
            Ct);

        queries.Should().NotBeEmpty();
        var benfordQuery = queries.FirstOrDefault(q => q.RuleId == "BenfordDigitPreferenceCheck");
        benfordQuery.Should().NotBeNull();
        benfordQuery!.Explanation.Should().Contain("Benford's Law Chi-Square digit preference anomaly detected");

        // 2. Verify MassTransit outbox events were published
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<FraudSentinelFlaggedEvent>(e => e.AnomalyType == "BenfordDigitPreferenceCheck" && e.SubjectId == "SUBJ-001"),
            Arg.Any<CancellationToken>());

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<FraudSentinelEvaluatedEvent>(e => e.IsFlagged && e.SubjectId == "SUBJ-001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithNormalPayload_PassesFraudSentinelsAndPublishesEvaluatedEventOnly()
    {
        var engine = new SentinelEngine();
        engine.Register(new BenfordDigitPreferenceSentinelCheck());
        engine.Register(new SiteVarianceSentinelCheck());

        var service = CreateService(engine);

        string payloadJson = """
        {
          "observation_date": "2026-10-02T10:00:00Z",
          "vssbp": 120.0,
          "vsdpb": 80.0
        }
        """;

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-002",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "1.0.0",
            Payload: payloadJson);

        var result = await service.CreateAsync(request, Ct);

        result.IsSuccess.Should().BeTrue();

        // No fraud queries raised
        var queries = await _queryRepository.ListBySubjectAsync(
            new Cadence.Apps.Execution.Domain.Subjects.StudyCode("STUDY-001"),
            new Cadence.Apps.Execution.Domain.Subjects.SubjectCode("SUBJ-002"),
            Ct);

        queries.Where(q => q.RuleId is "BenfordDigitPreferenceCheck" or "SiteVarianceCheck").Should().BeEmpty();

        // FraudSentinelEvaluatedEvent published with IsFlagged = false
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<FraudSentinelEvaluatedEvent>(e => !e.IsFlagged && e.SubjectId == "SUBJ-002"),
            Arg.Any<CancellationToken>());

        await _publishEndpoint.DidNotReceive().Publish(
            Arg.Any<FraudSentinelFlaggedEvent>(),
            Arg.Any<CancellationToken>());
    }
}
