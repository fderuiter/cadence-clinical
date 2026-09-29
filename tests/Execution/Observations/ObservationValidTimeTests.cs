using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.UnitTests.Execution.Fakes;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Observations;

/// <summary>
/// An observation's valid time is when it was made, supplied by the caller or the form payload. It is never
/// defaulted to the time the server records it (#4769, kernel §4.1 two clocks).
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed class ObservationValidTimeTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime MeasuredAt = new(2026, 2, 20, 8, 30, 0, DateTimeKind.Utc);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => RecordedAt;
    }

    private sealed class FakeAuditContextAccessor : ICurrentAuditContextAccessor
    {
        public string UserId => "coordinator-01";
        public string ChangeReason => "Data entry";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ObservationApplicationService ObservationService(InMemoryObservationRepository repository) =>
        new(repository, new InMemorySubjectRepository(), new UcumNormalizationService(), new LabRangeService(),
            new AstEditCheckEngine(), new FakeClock(), new FakeAuditContextAccessor());

    private static FormSubmissionApplicationService FormService(InMemoryObservationRepository observations) =>
        new(new InMemoryFormSubmissionRepository(), observations, new UcumNormalizationService(), new FakeAuditContextAccessor());

    private static ObservationCreateRequest Request(DateTime? observationDate) =>
        new(SubjectId: "SUBJ-VT-01", Domain: "VS", TestCode: "SYSBP", TestName: "Systolic BP",
            StudyId: "STUDY-VT", Value: 120, Unit: "mmHg", ObservationDate: observationDate);

    private static Task<System.Collections.Generic.IReadOnlyList<ClinicalObservation>> StoredAsync(InMemoryObservationRepository repository) =>
        repository.ListBySubjectAsync(new StudyCode("STUDY-VT"), new SubjectCode("SUBJ-VT-01"), Ct);

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void Create_WithoutAValidTime_IsRejected()
    {
        var result = ClinicalObservation.Create(
            new StudyCode("STUDY-VT"), new SubjectCode("SUBJ-VT-01"), new DomainCode("VS"), new TestCode("SYSBP"),
            "Systolic BP", observationDate: default, value: 120);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("OBSERVATION_DATE_REQUIRED");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CreateObservation_WithoutObservationDate_IsRejectedAndNothingIsStored()
    {
        var repository = new InMemoryObservationRepository();

        var result = await ObservationService(repository).CreateAsync(Request(observationDate: null), Ct);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("OBSERVATION_DATE_REQUIRED");
        (await StoredAsync(repository)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CreateObservation_KeepsTheCallersValidTime_NotTheRecordedTime()
    {
        var repository = new InMemoryObservationRepository();

        var result = await ObservationService(repository).CreateAsync(Request(MeasuredAt), Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Description);
        result.Value!.ObservationDate.Should().Be(MeasuredAt).And.NotBe(RecordedAt.UtcDateTime);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-002")]
    public async Task FormPayload_WithFieldsButNoObservationDate_IsRejected()
    {
        var observations = new InMemoryObservationRepository();

        var result = await FormService(observations).CreateAsync(
            new FormSubmissionCreateRequest(StudyId: "STUDY-VT", SubjectId: "SUBJ-VT-01", FormId: "FORM-VS",
                Payload: "{\"vssbp\": 120}"),
            Ct);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("OBSERVATION_DATE_REQUIRED");
        (await StoredAsync(observations)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-002")]
    public async Task FormPayload_WithAnInvalidObservationDate_IsRejected()
    {
        var result = await FormService(new InMemoryObservationRepository()).CreateAsync(
            new FormSubmissionCreateRequest(StudyId: "STUDY-VT", SubjectId: "SUBJ-VT-01", FormId: "FORM-VS",
                Payload: "{\"observation_date\": \"yesterday\", \"vssbp\": 120}"),
            Ct);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_OBSERVATION_DATE");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-002")]
    public async Task FormPayload_KeyValueFields_TakeThePayloadsObservationDate()
    {
        var observations = new InMemoryObservationRepository();

        var result = await FormService(observations).CreateAsync(
            new FormSubmissionCreateRequest(StudyId: "STUDY-VT", SubjectId: "SUBJ-VT-01", FormId: "FORM-VS",
                Payload: "{\"observation_date\": \"2026-02-20T08:30:00Z\", \"vssbp\": 120, \"vsdpb\": 80}"),
            Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Description);
        var stored = await StoredAsync(observations);
        stored.Select(o => o.TestCode.Value).Should().BeEquivalentTo("VSSBP", "VSDPB");
        stored.Should().OnlyContain(o => o.ObservationDate == MeasuredAt);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-002")]
    public async Task FormPayload_StructuredItems_UseTheirOwnDateOverThePayloads()
    {
        var observations = new InMemoryObservationRepository();
        const string payload = """
            {
              "observation_date": "2026-02-20T08:30:00Z",
              "observations": [
                { "domain": "VS", "test_code": "SYSBP", "value": 118, "unit": "mmHg" },
                { "domain": "LB", "test_code": "GLUC", "value": 95, "unit": "mg/dL", "observation_date": "2026-02-19T07:00:00Z" }
              ]
            }
            """;

        var result = await FormService(observations).CreateAsync(
            new FormSubmissionCreateRequest(StudyId: "STUDY-VT", SubjectId: "SUBJ-VT-01", FormId: "FORM-MIXED", Payload: payload),
            Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Description);
        var stored = (await StoredAsync(observations)).ToDictionary(o => o.TestCode.Value, o => o.ObservationDate);
        stored["SYSBP"].Should().Be(MeasuredAt);
        stored["GLUC"].Should().Be(new DateTime(2026, 2, 19, 7, 0, 0, DateTimeKind.Utc));
    }
}
