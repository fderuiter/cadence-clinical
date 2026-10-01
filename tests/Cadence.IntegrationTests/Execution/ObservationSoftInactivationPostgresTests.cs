using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Integration test verifying observation soft-inactivation behavior against real PostgreSQL using EF Core 10.
/// @req:PRD-QRY-003
/// </summary>
public sealed class ObservationSoftInactivationPostgresTests(ExecutionPostgresFixture fixture) : IClassFixture<ExecutionPostgresFixture>
{
    private sealed class TestAuditContextAccessor(string userId, string changeReason) : ICurrentAuditContextAccessor
    {
        public string UserId { get; set; } = userId;
        public string ChangeReason { get; set; } = changeReason;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdatePayloadAsync_SoftInactivatesPreviousObservations_InPostgresDatabase()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var studyCode = new StudyCode($"STUDY-SOFT-{Guid.NewGuid():N}");
        var subjectCode = new SubjectCode($"SUBJ-SOFT-{Guid.NewGuid():N}");

        var connString = fixture.ConnectionString!;

        string formIdStr;

        // 1. Create form submission and extract initial observations
        await using (var context = ExecutionPostgresFixture.CreateDbContext(connString))
        {
            var formRepo = new FormSubmissionRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var obsRepo = new ObservationRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var ucum = new UcumNormalizationService();
            var audit = new TestAuditContextAccessor("user-01", "Initial eCRF entry");

            var service = new FormSubmissionApplicationService(formRepo, obsRepo, ucum, audit);

            var initialPayload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120, \"vsdpb\": 80}";
            var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
                StudyId: studyCode.Value,
                SubjectId: subjectCode.Value,
                FormId: "FORM-VS",
                Payload: initialPayload), ct);

            createResult.IsSuccess.Should().BeTrue();
            formIdStr = createResult.Value!.Id;
        }

        // 2. Verify initial active observations in Postgres
        await using (var context = ExecutionPostgresFixture.CreateDbContext(connString))
        {
            var obsRepo = new ObservationRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var activeObs = await obsRepo.ListBySubjectAsync(studyCode, subjectCode, ct);
            activeObs.Should().HaveCount(2);
            activeObs.All(o => o.IsActive).Should().BeTrue();
        }

        // 3. Update payload on form submission
        await using (var context = ExecutionPostgresFixture.CreateDbContext(connString))
        {
            var formRepo = new FormSubmissionRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var obsRepo = new ObservationRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var ucum = new UcumNormalizationService();
            var audit = new TestAuditContextAccessor("user-01", "Payload correction");

            var service = new FormSubmissionApplicationService(formRepo, obsRepo, ucum, audit);

            var updatedPayload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 130, \"vsdpb\": 85}";
            var updateResult = await service.UpdatePayloadAsync(formIdStr, updatedPayload, ct);
            updateResult.IsSuccess.Should().BeTrue();
        }

        // 4. Verify in Postgres that active count remains 2 and total count is 4
        await using (var context = ExecutionPostgresFixture.CreateDbContext(connString))
        {
            var obsRepo = new ObservationRepository(context, SaveChangesResiliencePipelineFactory.Create());

            var activeObs = await obsRepo.ListBySubjectAsync(studyCode, subjectCode, includeInactive: false, ct);
            activeObs.Should().HaveCount(2);
            activeObs.Select(o => o.Value).Should().BeEquivalentTo(new double[] { 130, 85 });

            var allObs = await obsRepo.ListBySubjectAsync(studyCode, subjectCode, includeInactive: true, ct);
            allObs.Should().HaveCount(4);

            var inactiveObs = allObs.Where(o => !o.IsActive).ToList();
            inactiveObs.Should().HaveCount(2);
            inactiveObs.Select(o => o.Value).Should().BeEquivalentTo(new double[] { 120, 80 });
            inactiveObs.All(o => o.FormSubmissionId.HasValue && o.FormSubmissionId.Value.Value.ToString() == formIdStr).Should().BeTrue();
        }
    }
}
