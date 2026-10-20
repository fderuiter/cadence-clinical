using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Queries;
using Cadence.Contracts;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using MassTransit;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Queries;

public class ClinicalQueryApplicationServiceOutboxTests
{
    [Fact]
    public async Task CreateAsync_WhenQueryRaised_PublishesClinicalQueryRaisedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var audit = new FakeCurrentAuditContextAccessor("cra-user-01", "Query creation test");
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var service = new ClinicalQueryApplicationService(repo, audit, clock, publishEndpoint);

        var request = new QueryCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            TestCode: "SYSBP",
            Explanation: "Systolic BP 210 is out of range",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            Domain: "VS",
            Priority: "HIGH",
            QueryType: "DISCREPANCY");

        var result = await service.CreateAsync(request, ct);

        result.IsSuccess.Should().BeTrue();

        await publishEndpoint.Received(1).Publish(
            Arg.Is<ClinicalQueryRaised>(e =>
                e.StudyId == "STUDY-001" &&
                e.SubjectId == "SUBJ-001" &&
                e.SiteId == "SITE-01" &&
                e.TestCode == "SYSBP" &&
                e.Explanation == "Systolic BP 210 is out of range" &&
                e.Priority == "HIGH" &&
                e.QueryType == "DISCREPANCY" &&
                e.RaisedBy == "cra-user-01"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenPublishEndpointNull_SucceedsWithoutPublishing()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new InMemoryClinicalQueryRepository();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor("cra-user-01", "Query creation test");

        var service = new ClinicalQueryApplicationService(repo, audit, clock, publishEndpoint: null);

        var request = new QueryCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-002",
            TestCode: "SYSBP",
            Explanation: "Systolic BP review");

        var result = await service.CreateAsync(request, ct);

        result.IsSuccess.Should().BeTrue();
    }
}
